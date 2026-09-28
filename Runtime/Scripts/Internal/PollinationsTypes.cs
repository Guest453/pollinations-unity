// PollinationsTypes — serializable result/error types shared across the package.
// This file is intentionally UnityEngine-free so it compiles in plain .NET test harnesses.
using System;

namespace Pollinations.Unity
{
    /// <summary>Errors surfaced by the package. Messages never contain the bearer token.</summary>
    [Serializable]
    public class PollinationsException : Exception
    {
        public int StatusCode;

        public PollinationsException(string message) : base(message) { }
        public PollinationsException(string message, Exception inner) : base(message, inner) { }

        public static PollinationsException FromStatus(int code) => new PollinationsException(code switch
        {
            0 => "Connection failed. Check network and try again.",
            400 => "Bad request (400). Check prompt/model parameters.",
            401 => "Unauthorized (401). The API key is missing, expired or revoked — reconnect or set a new key.",
            402 => "Payment required (402). Out of Pollen — top up at enter.pollinations.ai or connect your own account via the device flow.",
            403 => "Forbidden (403). This key cannot use that model.",
            404 => "Not found (404). Check the model name against the live model lists.",
            429 => "Rate limited (429). Wait a moment and retry.",
            500 => "Pollinations server error (500). Try again later.",
            _ => $"Pollinations request failed (HTTP {code})."
        });
    }

    /// <summary>One chat message (OpenAI-compatible shape).</summary>
    [Serializable]
    public class ChatMessage
    {
        public string Role = "user";   // "system" | "user" | "assistant"
        public string Content = "";

        public ChatMessage() { }
        public ChatMessage(string role, string content) { Role = role; Content = content; }
    }

    /// <summary>Result of a text generation.</summary>
    [Serializable]
    public class TextResult
    {
        public string Text;
        public string Model;
        public double? PromptTokens;
        public double? CompletionTokens;
    }

    /// <summary>Result of a speech generation. Decode with PollinationsClient.GetAudioClip (wav)
    /// or PollinationsAudio.LoadClipAsync (mp3/other).</summary>
    [Serializable]
    public class SpeechResult
    {
        public byte[] AudioBytes;   // encoded (mp3/wav) audio exactly as returned by the API
        public string MimeType;
        public string Model;
        public string Voice;
    }
}
