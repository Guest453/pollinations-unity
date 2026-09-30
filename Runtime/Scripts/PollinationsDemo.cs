// PollinationsDemo — minimal demo showing all three generation types.
// Attach to a GameObject, wire up UI (or use the bundled sample scene), press Play.
//
// Demo flow:
//   1. [Connect Pollen] runs the device flow; the player approves in a browser.
//   2. [Generate Text] fills the text field from the default text model.
//   3. [Generate Image] shows a generated texture on the RawImage.
//   4. [Speak] plays the text aloud via the default audio model.
using System;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pollinations.Unity
{
    public class PollinationsDemo : MonoBehaviour
    {
        [Header("Connection")]
        // Dev-only key. Read from the POLLINATIONS_DEV_KEY environment variable
        // (or set this field in code) so it is never serialized into the scene,
        // a prefab, or a player build. Players use the device flow instead and
        // pay with their own Pollen.
        [NonSerialized] public string DevApiKey = "";
        [SerializeField] private string clientId = "";            // optional pk_ key for attribution
        [SerializeField] private TMP_Text statusLabel;

        [Header("Text")]
        [SerializeField] private TMP_InputField promptInput;
        [SerializeField] private TMP_Text replyLabel;

        [Header("Image")]
        [SerializeField] private RawImage imageTarget;

        [Header("Speech")]
        [SerializeField] private AudioSource audioTarget;

        private PollinationsClient _client;
        private CancellationTokenSource _authCts;
        private string _lastText = "Hello from Unity! Pollinations makes games talk, think and imagine.";

        private void Awake()
        {
            // Dev key comes from the environment, never from a serialized field.
            if (string.IsNullOrEmpty(DevApiKey))
            {
                DevApiKey = Environment.GetEnvironmentVariable("POLLINATIONS_DEV_KEY") ?? "";
            }
            _client = new PollinationsClient { ApiKey = DevApiKey };
        }

        private void OnDestroy()
        {
            _authCts?.Cancel();
            _authCts?.Dispose();
        }

        /// <summary>Run the device flow so the player pays with their own Pollen.</summary>
        public async void OnConnectPollenClicked()
        {
            _authCts = new CancellationTokenSource();
            var auth = new PollinationsAuth(clientId);
            try
            {
                SetStatus("Opening enter.pollinations.ai/device — approve there...");
                var authTask = auth.AuthorizeAsync(
                    p => SetStatus($"{p.State}: {p.Message}" + (string.IsNullOrEmpty(p.UserCode) ? "" : $"  code={p.UserCode}")),
                    _authCts.Token);
                Application.OpenURL("https://enter.pollinations.ai/device"); // convenience; code is in the status label
                var result = await authTask;
                if (result.State == DeviceFlowState.Granted)
                {
                    _client.DeviceToken = result.AccessToken;   // SECRET: keep in memory only
                    SetStatus("Connected! Generations now bill the player's Pollen.");
                }
                else
                {
                    SetStatus($"Device flow ended: {result.State}.");
                }
            }
            catch (OperationCanceledException) { SetStatus("Connection cancelled."); }
            catch (PollinationsException pe) { SetStatus("Auth error: " + pe.Message); }
        }

        /// <summary>Generate text from the prompt input.</summary>
        public async void OnGenerateTextClicked()
        {
            SetStatus("Generating text...");
            try
            {
                var result = await _client.TextAsync("You are a helpful game NPC.", promptInput.text);
                _lastText = result.Text;
                if (replyLabel != null) replyLabel.text = result.Text;
                SetStatus($"Text OK ({result.PromptTokens}→{result.CompletionTokens} tokens, model {result.Model}).");
            }
            catch (PollinationsException pe) { SetStatus("Text error: " + pe.Message); }
        }

        /// <summary>Generate an image from the prompt input.</summary>
        public async void OnGenerateImageClicked()
        {
            SetStatus("Generating image...");
            try
            {
                var result = await _client.ImageAsync(promptInput.text, width: 512, height: 512);
                if (imageTarget != null) imageTarget.texture = result.Texture;
                SetStatus($"Image OK ({result.Width}x{result.Height}, model {result.Model}).");
            }
            catch (PollinationsException pe) { SetStatus("Image error: " + pe.Message); }
        }

        /// <summary>Speak the last generated text aloud.</summary>
        public async void OnSpeakClicked()
        {
            SetStatus("Generating speech...");
            SpeechResult result = null;
            try
            {
                result = await _client.SpeechAsync(_lastText);
                // WAV decodes synchronously in-memory; other containers go through Unity's loader.
                AudioClip clip;
                if (result.MimeType != null && result.MimeType.Contains("wav"))
                {
                    clip = _client.GetAudioClip(result);
                }
                else
                {
                    clip = await PollinationsAudio.LoadClipAsync(result.AudioBytes, result.MimeType);
                }
                if (audioTarget != null)
                {
                    audioTarget.clip = clip;
                    audioTarget.Play();
                }
                SetStatus($"Speech OK ({result.MimeType}, voice {result.Voice}).");
            }
            catch (PollinationsException pe)
            {
                SetStatus("Speech error: " + pe.Message);
            }
        }

        private void SetStatus(string message)
        {
            if (statusLabel != null) statusLabel.text = message;
            Debug.Log($"[PollinationsDemo] {message}");
        }
    }
}
