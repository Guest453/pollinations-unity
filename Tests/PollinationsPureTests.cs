// Unit tests for the pure (UnityEngine-free) logic in Pollinations for Unity.
// Plain .NET — no Unity required. Run: dotnet test (see Tests/README or CI).
// Coverage: MiniJson parse/serialize round-trips, chat response parsing,
// image b64 extraction, payload builders, header/token selection, WAV decoding,
// device-flow state transitions (via injected fake transport).
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Pollinations.Unity;
using Pollinations.Unity.Internal;
using Xunit;

namespace Pollinations.Unity.Tests
{
    public class MiniJsonTests
    {
        [Theory]
        [InlineData("1", 1.0)]
        [InlineData("-2.5", -2.5)]
        [InlineData("1e2", 100.0)]
        public void ParsesNumbers(string json, double expected)
        {
            Assert.Equal(expected, (double)MiniJson.Parse(json));
        }

        [Fact]
        public void ParsesBasics()
        {
            Assert.NotNull(MiniJson.Parse("[1,2,3]"));
            Assert.NotNull(MiniJson.Parse("\"hi\""));
            Assert.Equal(1.0, MiniJson.GetNumber(
                (IDictionary<string, object>)MiniJson.Parse("{\"a\":1}"), "a"));
        }

        [Fact]
        public void ParsesNestedObject()
        {
            var v = (IDictionary<string, object>)MiniJson.Parse(
                "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Hello!\"}}],\"model\":\"gpt\",\"usage\":{\"prompt_tokens\":12,\"completion_tokens\":5}}");
            var choices = (IEnumerable<object>)v["choices"];
            foreach (var c in choices)
            {
                var msg = (IDictionary<string, object>)MiniJson.Get(c, "message");
                Assert.Equal("Hello!", msg["content"]);
                break;
            }
            Assert.Equal("gpt", v["model"]);
        }

        [Fact]
        public void RoundTripsStringsAndEscapes()
        {
            var s = "line1\nline2 \"quoted\" back\\slash\ttab ☃ emoji";
            var json = MiniJson.ToJson(new Dictionary<string, object> { ["k"] = s });
            var back = (IDictionary<string, object>)MiniJson.Parse(json);
            Assert.Equal(s, back["k"]);
        }

        [Fact]
        public void ParsesUnicodeEscapesAndSurrogatePairs()
        {
            var v = (string)MiniJson.Parse("\"\\u0041\\u00e9\\ud83d\\ude00\""); // A, é, 😀
            Assert.Equal("Aé😀", v);
        }

        [Fact]
        public void RejectsTrailingGarbage()
        {
            Assert.ThrowsAny<FormatException>(() => MiniJson.Parse("{} oops"));
        }

        [Fact]
        public void RejectsUnterminatedString()
        {
            Assert.ThrowsAny<FormatException>(() => MiniJson.Parse("{\"a\": \"b"));
        }

        [Fact]
        public void SerializesWholeDoublesAsIntegers()
        {
            Assert.Equal("{\"n\":3}", MiniJson.ToJson(new Dictionary<string, object> { ["n"] = 3d }));
            Assert.Equal("{\"t\":true,\"x\":null}", MiniJson.ToJson(new Dictionary<string, object> { ["t"] = true, ["x"] = null }));
        }

        [Fact]
        public void EscapesControlCharacters()
        {
            Assert.Equal("\"a\\nb\"", MiniJson.ToJson("a\nb"));
            Assert.Equal("\"\\u0001\"", MiniJson.ToJson("\u0001"));
        }
    }

    public class ChatResponseTests
    {
        // Captured from a real POST /v1/chat/completions response (trimmed).
        private const string RealShape =
            "{\"id\":\"cmpl-x\",\"model\":\"openai/gpt-5.4-nano\"," +
            "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"Ahoy! Ready to sail.\"},\"finish_reason\":\"stop\"}]," +
            "\"usage\":{\"prompt_tokens\":19,\"completion_tokens\":8,\"total_tokens\":27}}";

        [Fact]
        public void ParsesRealChatShape()
        {
            var r = PollinationsClient.ParseChatResponse(RealShape);
            Assert.Equal("Ahoy! Ready to sail.", r.Text);
            Assert.Equal("openai/gpt-5.4-nano", r.Model);
            Assert.Equal(19, r.PromptTokens);
            Assert.Equal(8, r.CompletionTokens);
        }

        [Fact]
        public void ThrowsOnMissingChoices()
        {
            Assert.Throws<PollinationsException>(() => PollinationsClient.ParseChatResponse("{\"error\":\"nope\"}"));
        }

        [Fact]
        public void ThrowsOnInvalidJson()
        {
            Assert.Throws<PollinationsException>(() => PollinationsClient.ParseChatResponse("not json at all"));
        }
    }

    public class ImageResponseTests
    {
        [Fact]
        public void ExtractsB64FromRealShape()
        {
            const string json = "{\"created\":1765152000,\"data\":[{\"b64_json\":\"aGVsbG8=\"}]}";
            var b64 = PollinationsClient.ExtractImageBase64(json);
            Assert.Equal("aGVsbG8=", b64);
            Assert.Equal("hello", Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
        }

        [Fact]
        public void ThrowsWhenNoData()
        {
            Assert.Throws<PollinationsException>(() => PollinationsClient.ExtractImageBase64("{\"oops\":1}"));
        }

        [Fact]
        public void ThrowsWhenNoB64()
        {
            Assert.Throws<PollinationsException>(() => PollinationsClient.ExtractImageBase64("{\"data\":[{\"url\":\"https://x/y.png\"}]}"));
        }
    }

    public class PayloadBuilderTests
    {
        private readonly PollinationsClient _c = new PollinationsClient
        {
            TextModel = "text-default",
            ImageModel = "image-default",
            SpeechModel = "speech-default",
            SpeechVoice = "alloy"
        };

        [Fact]
        public void ChatPayloadUsesModelOrDefault()
        {
            var p = _c.BuildChatPayload(new List<ChatMessage> { new ChatMessage("user", "hi") }, null, null, null);
            Assert.Equal("text-default", p["model"]);
            var p2 = _c.BuildChatPayload(new List<ChatMessage> { new ChatMessage("user", "hi") }, "other/model", null, null);
            Assert.Equal("other/model", p2["model"]);
        }

        [Fact]
        public void ChatPayloadSerializesToValidJson()
        {
            var p = _c.BuildChatPayload(new List<ChatMessage>
            {
                new ChatMessage("system", "be nice"),
                new ChatMessage("user", "say \"hi\"\nwith newline")
            }, null, 0.7, 500);
            var json = MiniJson.ToJson(p);
            var back = (IDictionary<string, object>)MiniJson.Parse(json);
            Assert.Equal("text-default", back["model"]);
            Assert.Equal(0.7, back["temperature"]);
            Assert.Equal(500.0, back["max_tokens"]);
            var msgs = (IEnumerable<object>)back["messages"];
            int count = 0;
            foreach (var m in msgs) count++;
            Assert.Equal(2, count);
        }

        [Fact]
        public void ChatPayloadRejectsEmptyMessages()
        {
            Assert.Throws<PollinationsException>(() => _c.BuildChatPayload(new List<ChatMessage>(), null, null, null));
            Assert.Throws<PollinationsException>(() => _c.BuildChatPayload(null, null, null, null));
            Assert.Throws<PollinationsException>(() => _c.BuildChatPayload(new List<ChatMessage> { new ChatMessage("user", "") }, null, null, null));
        }

        [Fact]
        public void ImagePayloadMatchesApiSchema()
        {
            var p = _c.BuildImagePayload("a castle at dusk", null, 512, 768);
            Assert.Equal("image-default", p["model"]);
            Assert.Equal("a castle at dusk", p["prompt"]);
            Assert.Equal(1, p["n"]);
            Assert.Equal("512x768", p["size"]);
            Assert.Equal("b64_json", p["response_format"]);
        }

        [Fact]
        public void SpeechPayloadDefaultsToWav()
        {
            var p = _c.BuildSpeechPayload("hello world", null, null, "audio/wav");
            Assert.Equal("speech-default", p["model"]);
            Assert.Equal("hello world", p["input"]);
            Assert.Equal("alloy", p["voice"]);
            Assert.Equal("wav", p["response_format"]);
        }

        [Fact]
        public void SpeechPayloadMapsMimeToFormat()
        {
            var p = _c.BuildSpeechPayload("hello", null, null, "audio/mpeg");
            Assert.Equal("mpeg", p["response_format"]);
        }

        [Fact]
        public void EffectiveTokenPrefersApiKey()
        {
            var c = new PollinationsClient { ApiKey = "sk_dev", DeviceToken = "sk_device" };
            Assert.Equal("sk_dev", c.EffectiveToken);
            var c2 = new PollinationsClient { ApiKey = "", DeviceToken = "sk_device" };
            Assert.Equal("sk_device", c2.EffectiveToken);
            Assert.Null(new PollinationsClient().EffectiveToken);
        }
    }

    public class HeaderTests
    {
        [Fact]
        public void AddsBearerWhenTokenPresent()
        {
            var h = PollinationsTransport.JsonHeaders("sk_secret", "TestAgent/1.0", false);
            Assert.Equal("Bearer sk_secret", h["Authorization"]);
            Assert.Equal("TestAgent/1.0", h["User-Agent"]);
            Assert.False(h.ContainsKey("Accept"));
        }

        [Fact]
        public void OmitsAuthorizationWhenNoToken()
        {
            var h = PollinationsTransport.JsonHeaders(null, null, true);
            Assert.False(h.ContainsKey("Authorization"));
            Assert.Equal("audio/*, image/*", h["Accept"]);
            Assert.Equal("Pollinations-Unity/1.0", h["User-Agent"]); // default filled in
        }
    }

    public class WavDecoderTests
    {
        /// <summary>Builds a minimal valid 16-bit PCM WAV: 4 samples, mono, 8kHz.</summary>
        private static byte[] MakeWav(short[] samples, int channels, int frequency)
        {
            using (var ms = new System.IO.MemoryStream())
            using (var w = new System.IO.BinaryWriter(ms))
            {
                int dataSize = samples.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + dataSize);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);
                w.Write((short)1);          // PCM
                w.Write((short)channels);
                w.Write(frequency);
                w.Write(frequency * channels * 2); // byte rate
                w.Write((short)(channels * 2));    // block align
                w.Write((short)16);         // bits per sample
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(dataSize);
                foreach (var s in samples) w.Write(s);
                w.Flush();
                return ms.ToArray();
            }
        }

        [Fact]
        public void DecodesValidWav()
        {
            var wav = MakeWav(new short[] { 0, 16384, -16384, 32767 }, 1, 8000);
            var (samples, channels, freq) = WavDecoder.Decode(wav);
            Assert.Equal(4, samples.Length);
            Assert.Equal(1, channels);
            Assert.Equal(8000, freq);
            Assert.Equal(0f, samples[0], 2);
            Assert.Equal(16384f / 32768f, samples[1], 3);
            Assert.Equal(-16384f / 32768f, samples[2], 3);
            Assert.Equal(32767f / 32768f, samples[3], 3);
        }

        [Fact]
        public void RejectsGarbage()
        {
            Assert.Throws<PollinationsException>(() => WavDecoder.Decode(new byte[10]));
            Assert.Throws<PollinationsException>(() => WavDecoder.Decode(Encoding.ASCII.GetBytes("NOTAWAVFILE AT ALL!!!!!!!!")));
        }

        [Fact]
        public void RejectsNonPcm()
        {
            // WAV with audio format 3 (IEEE float) must be rejected clearly.
            var wav = MakeWav(new short[] { 0, 0 }, 1, 8000);
            wav[20] = 3; // fmt chunk, audioFormat offset
            Assert.Throws<PollinationsException>(() => WavDecoder.Decode(wav));
        }
    }

    public class ModelCatalogTests
    {
        // Captured from GET https://gen.pollinations.ai/image/models (trimmed, 2026-09).
        private const string RealImageModels =
            "[{\"name\":\"tongyi-mai/z-image-turbo\",\"aliases\":[\"z-image\"],\"category\":\"image\"," +
            "\"publisher\":\"Alibaba\",\"community\":false,\"title\":\"Z-Image Turbo\"," +
            "\"description\":\"Instant, budget-friendly images\",\"pricing\":{\"currency\":\"pollen\",\"completionImageTokens\":\"0.004\"}," +
            "\"health\":{\"status\":\"healthy\",\"success_rate\":99.958}}," +
            "{\"name\":\"black-forest-labs/flux.1.1-pro\",\"category\":\"image\",\"publisher\":\"Black Forest Labs\",\"community\":false}]";

        [Fact]
        public void ParsesRealImageModelList()
        {
            var models = PollinationsModel.ListFromJson(RealImageModels);
            Assert.Equal(2, models.Count);
            Assert.Equal("tongyi-mai/z-image-turbo", models[0].Name);
            Assert.Equal("Z-Image Turbo", models[0].Title);
            Assert.Equal("Alibaba", models[0].Publisher);
            Assert.Equal(99.958, models[0].SuccessRate);
            Assert.False(models[0].Community);
            Assert.Equal("black-forest-labs/flux.1.1-pro", models[1].Name);
            Assert.Null(models[1].SuccessRate);
        }

        [Fact]
        public void RejectsNonArray()
        {
            Assert.Throws<PollinationsException>(() => PollinationsModel.ListFromJson("{\"name\":\"x\"}"));
        }

        [Fact]
        public void SkipsNamelessEntries()
        {
            var models = PollinationsModel.ListFromJson("[{\"title\":\"no name\"},{\"name\":\"ok/model\"}]");
            Assert.Single(models);
            Assert.Equal("ok/model", models[0].Name);
        }
    }

    public class DeviceFlowTests
    {
        /// <summary>Runs the device flow against a scripted fake transport and reports every progress update.</summary>
        private static async Task<(DeviceFlowProgress result, List<DeviceFlowProgress> progress)> RunFlowAsync(
            Func<PollinationsRequest, int, string> responder, string clientId = "pk_test_app")
        {
            var auth = new PollinationsAuth(clientId) { PollIntervalSeconds = 1 };
            PollinationsTransport.Sender = (req, binary) =>
            {
                var body = responder(req, (int)0);
                return new PollinationsResponse { Status = 200, Body = Encoding.UTF8.GetBytes(body), ContentType = "application/json" };
            };
            var progress = new List<DeviceFlowProgress>();
            var result = await auth.AuthorizeAsync(progress.Add, System.Threading.CancellationToken.None);
            return (result, progress);
        }

        private static void ResetTransport() => PollinationsTransport.Sender = null;

        [Fact]
        public async Task GrantsWhenTokenArrives()
        {
            try
            {
                string seenUserCode = null;
                var (result, progress) = await RunFlowAsync((req, _) =>
                {
                    if (req.Url.EndsWith("/api/device/code"))
                        return "{\"device_code\":\"DC123\",\"user_code\":\"ABCD-1234\",\"verification_uri\":\"/device\",\"expires_in\":600,\"interval\":0}";
                    seenUserCode = "polled";
                    return "{\"access_token\":\"sk_GRANTED\",\"token_type\":\"bearer\"}";
                });

                Assert.Equal(DeviceFlowState.Granted, result.State);
                Assert.Equal("sk_GRANTED", result.AccessToken);
                Assert.Equal("ABCD-1234", result.UserCode);
                Assert.Equal("https://enter.pollinations.ai/device", result.VerificationUri);
                Assert.Contains(progress, p => p.State == DeviceFlowState.WaitingForUser);
                Assert.Equal("polled", seenUserCode);
            }
            finally { ResetTransport(); }
        }

        [Fact]
        public async Task SendsClientIdAndDeviceCode()
        {
            try
            {
                string codeBody = null, tokenBody = null;
                await RunFlowAsync((req, _) =>
                {
                    if (req.Url.EndsWith("/api/device/code")) { codeBody = req.JsonBody; return "{\"device_code\":\"DC\",\"user_code\":\"U\",\"verification_uri\":\"/device\",\"expires_in\":60}"; }
                    tokenBody = req.JsonBody;
                    return "{\"access_token\":\"sk_t\",\"token_type\":\"bearer\"}";
                });
                Assert.Contains("pk_test_app", codeBody);
                Assert.Contains("DC", tokenBody);
            }
            finally { ResetTransport(); }
        }

        [Fact]
        public async Task RejectsSkClientId()
        {
            var auth = new PollinationsAuth("sk_not_publishable");
            await Assert.ThrowsAsync<PollinationsException>(() =>
                auth.AuthorizeAsync(null, System.Threading.CancellationToken.None));
        }

        [Fact]
        public async Task DeniedEndsFlow()
        {
            try
            {
                var (result, progress) = await RunFlowAsync((req, _) =>
                {
                    if (req.Url.EndsWith("/api/device/code"))
                        return "{\"device_code\":\"DC\",\"user_code\":\"U\",\"verification_uri\":\"/device\",\"expires_in\":60}";
                    return "{\"error\":\"access_denied\"}";
                });
                Assert.Equal(DeviceFlowState.Denied, result.State);
            }
            finally { ResetTransport(); }
        }
    }
}
