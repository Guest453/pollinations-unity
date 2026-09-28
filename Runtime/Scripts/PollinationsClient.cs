// PollinationsClient — the Unity-facing half: async text/image/speech helpers that
// decode into Texture2D / AudioClip. Pure request/response logic lives in
// PollinationsClient.Core.cs (plain .NET, unit-testable).
//
// Verified against the live API (2026-09):
//   Text   POST /v1/chat/completions   (OpenAI-compatible)
//   Image  POST /v1/images/generations (response_format b64_json)
//   Speech POST /v1/audio/speech       (binary audio; wav decodes in-memory)
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Pollinations.Unity
{
    /// <summary>Result of an image generation. Texture2D is created on the Unity main thread.</summary>
    [Serializable]
    public class ImageResult
    {
        public Texture2D Texture;
        public byte[] PngBytes;
        public string Model;
        public int Width;
        public int Height;
    }

    public sealed partial class PollinationsClient
    {
        // ---------------- Text ----------------

        /// <summary>POST /v1/chat/completions (OpenAI-compatible). Returns the assistant message plus usage.</summary>
        public Task<TextResult> TextAsync(IList<ChatMessage> messages, string model = null, double? temperature = null, int? maxTokens = null)
        {
            var body = Internal.MiniJson.ToJson(BuildChatPayload(messages, model, temperature, maxTokens));
            return SendJsonAsync("POST", GenBase + "/v1/chat/completions", body).ContinueWith(t =>
            {
                if (t.IsFaulted) throw t.Exception.InnerException ?? t.Exception;
                return ParseChatResponse(t.Result);
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        /// <summary>One-shot convenience: system + user message → assistant reply.</summary>
        public Task<TextResult> TextAsync(string systemPrompt, string userPrompt, string model = null)
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage("system", systemPrompt ?? ""),
                new ChatMessage("user", userPrompt ?? "")
            };
            return TextAsync(messages, model);
        }

        // ---------------- Image ----------------

        /// <summary>
        /// POST /v1/images/generations with response_format b64_json, decoded into a
        /// Texture2D (LoadImage must run on the Unity main thread).
        /// </summary>
        public Task<ImageResult> ImageAsync(string prompt, string model = null, int width = 1024, int height = 1024)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return Task.FromException<ImageResult>(new PollinationsException("ImageAsync requires a prompt."));

            var body = Internal.MiniJson.ToJson(BuildImagePayload(prompt, model, width, height));
            var usedModel = string.IsNullOrEmpty(model) ? ImageModel : model;
            return SendJsonAsync("POST", GenBase + "/v1/images/generations", body).ContinueWith(t =>
            {
                if (t.IsFaulted) throw t.Exception.InnerException ?? t.Exception;
                return DecodeImageOnMainThread(t.Result, usedModel);
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        private static ImageResult DecodeImageOnMainThread(string json, string model)
        {
            var b64 = ExtractImageBase64(json);
            byte[] png;
            try { png = Convert.FromBase64String(b64); }
            catch (FormatException e) { throw new PollinationsException("Image b64_json was not valid base64.", e); }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(png))
            {
                UnityEngine.Object.Destroy(tex);
                throw new PollinationsException("Image bytes could not be decoded into a Texture2D.");
            }
            return new ImageResult { Texture = tex, PngBytes = png, Model = model, Width = tex.width, Height = tex.height };
        }

        // ---------------- Speech ----------------

        /// <summary>
        /// POST /v1/audio/speech → binary audio. Default wav (always decodable in Unity);
        /// pass mimeType "audio/mpeg" for mp3 and decode via PollinationsAudio.LoadClipAsync.
        /// </summary>
        public Task<SpeechResult> SpeechAsync(string text, string model = null, string voice = null, string mimeType = "audio/wav")
        {
            if (string.IsNullOrWhiteSpace(text))
                return Task.FromException<SpeechResult>(new PollinationsException("SpeechAsync requires input text."));

            var body = Internal.MiniJson.ToJson(BuildSpeechPayload(text, model, voice, mimeType));
            var headers = Internal.PollinationsTransport.JsonHeaders(EffectiveToken, UserAgent, wantsBinary: true);
            var usedModel = string.IsNullOrEmpty(model) ? SpeechModel : model;
            var usedVoice = string.IsNullOrEmpty(voice) ? SpeechVoice : voice;

            return Internal.PollinationsTransport.SendAsync(
                new Internal.PollinationsRequest(GenBase + "/v1/audio/speech", "POST", body, headers),
                Binary: true).ContinueWith(t =>
            {
                if (t.IsFaulted) throw t.Exception.InnerException ?? t.Exception;
                var r = t.Result;
                if (r.Status >= 400) throw PollinationsException.FromStatus((int)r.Status);
                return new SpeechResult
                {
                    AudioBytes = r.Body,
                    MimeType = string.IsNullOrEmpty(r.ContentType) ? mimeType : r.ContentType,
                    Model = usedModel,
                    Voice = usedVoice
                };
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        /// <summary>Decode a WAV SpeechResult into an AudioClip (main thread). For mp3/other containers use PollinationsAudio.LoadClipAsync.</summary>
        public AudioClip GetAudioClip(SpeechResult result, string clipName = "pollinations-speech")
        {
            if (result?.AudioBytes == null || result.AudioBytes.Length == 0)
                throw new PollinationsException("SpeechResult has no audio bytes.");
            if (result.MimeType == null || !result.MimeType.Contains("wav"))
                throw new PollinationsException("Only WAV SpeechResults decode synchronously; use PollinationsAudio.LoadClipAsync for mp3/other formats.");

            var (samples, channels, frequency) = Internal.WavDecoder.Decode(result.AudioBytes);
            var clip = AudioClip.Create(clipName, samples.Length, channels, frequency, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
