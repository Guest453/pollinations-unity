// PollinationsTransport — HTTP plumbing shared by the client and the device flow.
// The request/response model and header logic are UnityEngine-free so plain .NET
// test harnesses can exercise them; only the UnityWebRequest sender touches Unity
// (and is compiled out when UNITY_5_3_OR_NEWER is not defined).
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
using UnityEngine.Networking;
#endif

namespace Pollinations.Unity.Internal
{
    /// <summary>A minimal description of an HTTP call. Transport-agnostic (no Unity types).</summary>
    public sealed class PollinationsRequest
    {
        public string Url;
        public string Method;
        public string JsonBody;      // null → GET-style request
        public IDictionary<string, string> Headers;

        public PollinationsRequest(string url, string method, string jsonBody, IDictionary<string, string> headers)
        {
            Url = url; Method = method; JsonBody = jsonBody; Headers = headers;
        }
    }

    /// <summary>Raw HTTP result. Body is the raw bytes; Status is the HTTP status code.</summary>
    public sealed class PollinationsResponse
    {
        public long Status;
        public string ContentType;
        public byte[] Body;
    }

    /// <summary>
    /// Sends <see cref="PollinationsRequest"/>s. Default implementation wraps UnityWebRequest;
    /// tests assign <see cref="Sender"/> to inject a fake (no network, deterministic).
    /// </summary>
    public static class PollinationsTransport
    {
        /// <summary>Injection point for tests. When set, no UnityWebRequest is used at all.</summary>
        public static Func<PollinationsRequest, bool, PollinationsResponse> Sender { get; set; }

        public static IDictionary<string, string> JsonHeaders(string token, string userAgent, bool wantsBinary)
        {
            var h = new Dictionary<string, string>
            {
                ["User-Agent"] = string.IsNullOrEmpty(userAgent) ? "Pollinations-Unity/1.0" : userAgent
            };
            if (wantsBinary) h["Accept"] = "audio/*, image/*";
            if (!string.IsNullOrEmpty(token)) h["Authorization"] = "Bearer " + token;
            return h;
        }

        public static PollinationsResponse Send(PollinationsRequest req, bool binary = false)
        {
            return SendAsync(req, binary).GetAwaiter().GetResult();
        }

        /// <summary>Callback-style send (used by the model-list helper).</summary>
        public static void Send(PollinationsRequest req, Action<PollinationsResponse> onDone, Action<PollinationsException> onError)
        {
            SendAsync(req, false).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    var inner = t.Exception.InnerException ?? t.Exception;
                    onError(inner is PollinationsException pe ? pe : PollinationsException.FromStatus(0));
                }
                else onDone(t.Result);
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        public static async Task<PollinationsResponse> SendAsync(PollinationsRequest req, bool Binary = false)
        {
            if (Sender != null) return Sender(req, Binary);

#if UNITY_5_3_OR_NEWER
            using (var www = BuildUnityWebRequest(req, Binary))
            {
                var op = www.SendWebRequest();
                while (!op.isDone) await Task.Yield();
                var resp = new PollinationsResponse
                {
                    Status = (long)www.responseCode,
                    ContentType = www.GetResponseHeader("Content-Type") ?? ""
                };
                resp.Body = www.downloadHandler?.data ?? new byte[0];
                if (resp.Status >= 400)
                {
                    // Intentionally discard www.error / response body: error payloads can echo
                    // the bearer credential. Surface only the status-derived guidance.
                    throw PollinationsException.FromStatus((int)resp.Status);
                }
                return resp;
            }
#else
            // Non-Unity build (test harness): no real transport available.
            throw new PollinationsException("No transport configured. Assign PollinationsTransport.Sender in tests.");
#endif
        }

#if UNITY_5_3_OR_NEWER
        internal static UnityWebRequest BuildUnityWebRequest(PollinationsRequest req, bool binary)
        {
            UnityWebRequest www;
            if (req.JsonBody != null)
            {
                www = new UnityWebRequest(req.Url, req.Method, new DownloadHandlerBuffer(),
                                          new UploadHandlerRaw(Encoding.UTF8.GetBytes(req.JsonBody)));
            }
            else
            {
                www = new UnityWebRequest(req.Url, req.Method, new DownloadHandlerBuffer(), null);
            }
            www.timeout = 120;
            if (req.Headers != null)
            {
                foreach (var kv in req.Headers)
                {
                    if (kv.Key == "User-Agent") continue; // not settable on all Unity versions; harmless to skip
                    www.SetRequestHeader(kv.Key, kv.Value);
                }
            }
            return www;
        }
#endif
    }

    /// <summary>
    /// Minimal 16-bit PCM WAV decoder (mono/stereo) so speech can become an AudioClip
    /// without external dependencies. Handles the standard RIFF header emitted by
    /// /v1/audio/speech with response_format "wav".
    /// </summary>
    public static class WavDecoder
    {
        public static (float[] samples, int channels, int frequency) Decode(byte[] wav)
        {
            if (wav == null || wav.Length < 44)
                throw new PollinationsException("WAV data too small to contain a RIFF header.");
            int i = 0;
            if (wav[i++] != 'R' || wav[i++] != 'I' || wav[i++] != 'F' || wav[i++] != 'F')
                throw new PollinationsException("Not a RIFF file.");
            i += 4; // riff size
            if (wav[i++] != 'W' || wav[i++] != 'A' || wav[i++] != 'V' || wav[i++] != 'E')
                throw new PollinationsException("Not a WAVE file.");

            // Walk chunks until we have fmt + data.
            short audioFormat = -1, channels = -1, bitsPerSample = -1;
            int frequency = -1;
            byte[] dataChunk = null;
            while (i + 8 <= wav.Length)
            {
                string chunkId = Encoding.ASCII.GetString(wav, i, 4);
                int chunkSize = BitConverter.ToInt32(wav, i + 4);
                i += 8;
                if (chunkId == "fmt ")
                {
                    audioFormat = BitConverter.ToInt16(wav, i);
                    channels = BitConverter.ToInt16(wav, i + 2);
                    frequency = BitConverter.ToInt32(wav, i + 4);
                    bitsPerSample = BitConverter.ToInt16(wav, i + 14);
                }
                else if (chunkId == "data")
                {
                    int len = Math.Min(chunkSize, wav.Length - i);
                    dataChunk = new byte[len];
                    Buffer.BlockCopy(wav, i, dataChunk, 0, len);
                }
                i += chunkSize + (chunkSize % 2); // chunks are word-aligned
                if (dataChunk != null && audioFormat != -1) break;
            }

            if (audioFormat != 1) // PCM
                throw new PollinationsException($"Only 16-bit PCM WAV is supported (got format {audioFormat}).");
            if (bitsPerSample != 16)
                throw new PollinationsException($"Only 16-bit PCM WAV is supported (got {bitsPerSample} bits).");
            if (channels < 1 || dataChunk == null)
                throw new PollinationsException("WAV file missing data chunk.");

            int sampleCount = dataChunk.Length / 2 / channels;
            var samples = new float[sampleCount * channels];
            for (int s = 0; s < samples.Length; s++)
            {
                short v = BitConverter.ToInt16(dataChunk, s * 2);
                samples[s] = v / 32768f;
            }
            return (samples, channels, frequency);
        }
    }
}
