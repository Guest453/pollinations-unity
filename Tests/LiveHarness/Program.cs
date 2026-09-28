// Live-verification harness: runs a REAL Pollinations generation through the actual
// package client code (payload builder → serializer → HTTP → response parser).
// The UnityWebRequest transport is replaced by an HttpClient sender here because
// UnityWebRequest only exists inside Unity; everything above the transport is the
// real shipping code from Runtime/Scripts.
//
// Usage: POLLINATIONS_API_KEY=sk_... dotnet run --project Tests/LiveHarness
// The key is read from the environment only — never logged, never stored.
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Pollinations.Unity;
using Pollinations.Unity.Internal;

namespace Pollinations.Unity.LiveHarness
{
    internal static class Program
    {
        private static readonly HttpClient Http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        private static async Task<int> Main()
        {
            var key = Environment.GetEnvironmentVariable("POLLINATIONS_API_KEY");
            if (string.IsNullOrEmpty(key))
            {
                Console.Error.WriteLine("Set POLLINATIONS_API_KEY to run the live check.");
                return 2;
            }

            // Real HTTP via HttpClient (stands in for UnityWebRequest outside Unity).
            PollinationsTransport.Sender = (req, binary) => Send(req).GetAwaiter().GetResult();

            var client = new PollinationsClient(key);

            // ---- 1. TEXT: real generation through the package's TextAsync ----
            var text = await client.TextAsync(
                new System.Collections.Generic.List<ChatMessage>
                {
                    new ChatMessage("system", "You are a terse game narrator."),
                    new ChatMessage("user", "Say one short sentence welcoming players to a Unity demo.")
                },
                model: "openai/gpt-5.4-nano",
                maxTokens: 60);
            Console.WriteLine("TEXT RESULT");
            Console.WriteLine("  model:    " + text.Model);
            Console.WriteLine("  usage:    prompt=" + text.PromptTokens + " completion=" + text.CompletionTokens);
            Console.WriteLine("  content:  " + text.Text);
            if (string.IsNullOrWhiteSpace(text.Text)) { Console.Error.WriteLine("FAIL: empty text"); return 1; }

            // ---- 2. SPEECH: real TTS through the package's SpeechAsync ----
            var speech = await client.SpeechAsync(text.Text, mimeType: "audio/wav");
            Console.WriteLine("SPEECH RESULT");
            Console.WriteLine("  mime:     " + speech.MimeType);
            Console.WriteLine("  bytes:    " + (speech.AudioBytes?.Length ?? 0));
            if (speech.AudioBytes == null || speech.AudioBytes.Length < 100) { Console.Error.WriteLine("FAIL: tiny audio"); return 1; }

            // ---- 3. WAV decode (the real package decoder) ----
            var (samples, channels, frequency) = WavDecoder.Decode(speech.AudioBytes);
            Console.WriteLine("WAV DECODE");
            Console.WriteLine("  samples:  " + samples.Length);
            Console.WriteLine("  channels: " + channels + "  frequency: " + frequency + " Hz");
            if (samples.Length == 0 || frequency <= 0) { Console.Error.WriteLine("FAIL: bad wav"); return 1; }

            // ---- 3. IMAGE: real generation through the package's pure image pipeline ----
            var imgPayload = Internal.MiniJson.ToJson(client.BuildImagePayload("a tiny pixel-art castle on a hill", null, 512, 512));
            var imgJson = await client.SendJsonAsync("POST", PollinationsClient.GenBase + "/v1/images/generations", imgPayload);
            var b64 = PollinationsClient.ExtractImageBase64(imgJson);
            var imgBytes = Convert.FromBase64String(b64);
            bool isPng = imgBytes[0] == 0x89 && imgBytes[1] == (byte)'P';
            bool isJpeg = imgBytes[0] == 0xFF && imgBytes[1] == 0xD8;
            Console.WriteLine("IMAGE RESULT");
            Console.WriteLine("  image bytes: " + imgBytes.Length);
            Console.WriteLine("  format:      " + (isPng ? "PNG" : isJpeg ? "JPEG" : "unknown"));
            if (imgBytes.Length < 1000 || !(isPng || isJpeg)) { Console.Error.WriteLine("FAIL: not a decodable image"); return 1; }
            // (In Unity, Texture2D.LoadImage decodes both PNG and JPEG — see PollinationsClient.cs.)

            Console.WriteLine("LIVE GENERATION THROUGH PACKAGE CODE: OK");
            return 0;
        }

        private static async Task<PollinationsResponse> Send(PollinationsRequest req)
        {
            using (var m = new HttpRequestMessage(new HttpMethod(req.Method), req.Url))
            {
                if (req.Headers != null)
                    foreach (var kv in req.Headers)
                        m.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                if (req.JsonBody != null)
                    m.Content = new StringContent(req.JsonBody, Encoding.UTF8, "application/json");

                using (var resp = await Http.SendAsync(m))
                {
                    var body = resp.Content == null ? new byte[0] : await resp.Content.ReadAsByteArrayAsync();
                    var contentType = resp.Content?.Headers?.ContentType?.ToString() ?? "";
                    if ((int)resp.StatusCode >= 400)
                        throw PollinationsException.FromStatus((int)resp.StatusCode);
                    return new PollinationsResponse { Status = (long)resp.StatusCode, ContentType = contentType, Body = body };
                }
            }
        }
    }
}
