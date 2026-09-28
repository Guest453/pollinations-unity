// PollinationsClient.Core — the pure, UnityEngine-free half of the client:
// settings fields, token selection, request payload builders, response parsers,
// and the Task-based TextAsync/SpeechAsync calls (no Unity types needed).
// Split out as a partial class so plain .NET test harnesses can compile and test it.
// The Unity-facing half (Texture2D/AudioClip plumbing) lives in PollinationsClient.cs.
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Pollinations.Unity
{
    public sealed partial class PollinationsClient
    {
        public const string GenBase = "https://gen.pollinations.ai";
        public const string AuthBase = "https://enter.pollinations.ai";

        /// <summary>API key (sk_... from enter.pollinations.ai/keys). Leave empty to use the device-flow token.</summary>
        public string ApiKey = "";

        /// <summary>Token captured from the device flow, used when ApiKey is empty. SECRET — keep in memory only.</summary>
        public string DeviceToken = "";

        /// <summary>Default text model (any name from GET /text/models).</summary>
        public string TextModel = "openai/gpt-5.4-nano";

        /// <summary>Default image model (any name from GET /image/models).</summary>
        public string ImageModel = "tongyi-mai/z-image-turbo";

        /// <summary>Default speech model (any name from GET /audio/models).</summary>
        public string SpeechModel = "elevenlabs/eleven-v3";

        /// <summary>Default TTS voice (model-dependent; see the "voices" array in /audio/models).</summary>
        public string SpeechVoice = "alloy";

        /// <summary>User-Agent sent with every request so Pollinations can attribute traffic.</summary>
        public string UserAgent = "Pollinations-Unity/1.0";

        public PollinationsClient() { }
        public PollinationsClient(string apiKey) { ApiKey = apiKey; }

        /// <summary>Which token to use for a request: explicit dev key, else device-flow token.</summary>
        internal string EffectiveToken =>
            !string.IsNullOrWhiteSpace(ApiKey) ? ApiKey :
            !string.IsNullOrWhiteSpace(DeviceToken) ? DeviceToken : null;

        // ---------------- Request builders (pure, unit-tested) ----------------

        internal Dictionary<string, object> BuildChatPayload(IList<ChatMessage> messages, string model, double? temperature, int? maxTokens)
        {
            var payload = new Dictionary<string, object>
            {
                ["model"] = string.IsNullOrEmpty(model) ? TextModel : model,
                ["messages"] = SerializeMessages(messages)
            };
            if (temperature.HasValue) payload["temperature"] = temperature.Value;
            if (maxTokens.HasValue) payload["max_tokens"] = maxTokens.Value;
            return payload;
        }

        internal static List<object> SerializeMessages(IList<ChatMessage> messages)
        {
            if (messages == null || messages.Count == 0)
                throw new PollinationsException("TextAsync requires at least one ChatMessage.");
            var list = new List<object>(messages.Count);
            foreach (var m in messages)
            {
                if (m == null || string.IsNullOrEmpty(m.Content))
                    throw new PollinationsException("ChatMessage with empty content.");
                list.Add(new Dictionary<string, object>
                {
                    ["role"] = string.IsNullOrEmpty(m.Role) ? "user" : m.Role,
                    ["content"] = m.Content
                });
            }
            return list;
        }

        internal Dictionary<string, object> BuildImagePayload(string prompt, string model, int width, int height)
        {
            return new Dictionary<string, object>
            {
                ["model"] = string.IsNullOrEmpty(model) ? ImageModel : model,
                ["prompt"] = prompt,
                ["n"] = 1,
                ["size"] = $"{width}x{height}",
                ["response_format"] = "b64_json"
            };
        }

        internal Dictionary<string, object> BuildSpeechPayload(string text, string model, string voice, string mimeType)
        {
            string format = mimeType == "audio/wav" || string.IsNullOrEmpty(mimeType)
                ? "wav"
                : mimeType.Substring(mimeType.IndexOf('/') + 1);
            return new Dictionary<string, object>
            {
                ["model"] = string.IsNullOrEmpty(model) ? SpeechModel : model,
                ["input"] = text,
                ["voice"] = string.IsNullOrEmpty(voice) ? SpeechVoice : voice,
                ["response_format"] = format
            };
        }

        // ---------------- Response parsers (pure, unit-tested) ----------------

        internal static TextResult ParseChatResponse(string json)
        {
            object root;
            try { root = Internal.MiniJson.Parse(json); }
            catch (FormatException e) { throw new PollinationsException("Chat response was not valid JSON: " + e.Message, e); }
            var choices = Internal.MiniJson.Get(root, "choices") as IEnumerable<object>;
            string text = null;
            if (choices != null)
            {
                foreach (var c in choices)
                {
                    var msg = Internal.MiniJson.Get(c, "message");
                    text = Internal.MiniJson.GetString(msg, "content");
                    break;
                }
            }
            if (text == null)
                throw new PollinationsException("Chat response missing choices[0].message.content.");

            var result = new TextResult
            {
                Text = text,
                Model = Internal.MiniJson.GetString(root, "model")
            };
            if (Internal.MiniJson.Get(root, "usage") is IDictionary<string, object> usage)
            {
                result.PromptTokens = Internal.MiniJson.GetNumber(usage, "prompt_tokens");
                result.CompletionTokens = Internal.MiniJson.GetNumber(usage, "text_tokens") ?? Internal.MiniJson.GetNumber(usage, "completion_tokens");
            }
            return result;
        }

        internal static string ExtractImageBase64(string json)
        {
            object root;
            try { root = Internal.MiniJson.Parse(json); }
            catch (FormatException e) { throw new PollinationsException("Image response was not valid JSON: " + e.Message, e); }
            var data = Internal.MiniJson.Get(root, "data") as IEnumerable<object>;
            if (data == null) throw new PollinationsException("Image response missing data array.");
            foreach (var d in data)
            {
                var b64 = Internal.MiniJson.GetString(d, "b64_json");
                if (b64 != null) return b64;
            }
            throw new PollinationsException("Image response contained no b64_json entry.");
        }

        // ---------------- Text (pure — works in Unity AND plain .NET) ----------------

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

        // ---------------- Speech (pure — binary audio, no Unity types) ----------------

        /// <summary>
        /// POST /v1/audio/speech → binary audio bytes. Default wav (decodable in-memory in
        /// Unity via GetAudioClip); pass mimeType "audio/mpeg" for mp3.
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

        // ---------------- Shared plumbing ----------------

        internal Task<string> SendJsonAsync(string method, string url, string jsonBody)
        {
            var headers = Internal.PollinationsTransport.JsonHeaders(EffectiveToken, UserAgent, wantsBinary: false);
            return Internal.PollinationsTransport.SendAsync(
                new Internal.PollinationsRequest(url, method, jsonBody, headers), Binary: false)
                .ContinueWith(t =>
                {
                    if (t.IsFaulted) throw t.Exception.InnerException ?? t.Exception;
                    var r = t.Result;
                    if (r.Status >= 400) throw PollinationsException.FromStatus((int)r.Status);
                    return Encoding.UTF8.GetString(r.Body);
                }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }
    }
}
