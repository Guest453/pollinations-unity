// PollinationsAudio — loads encoded audio bytes (mp3/ogg/wav containers Unity can't
// decode in-memory) into an AudioClip via UnityWebRequestMultimedia. For WAV the
// client's own WavDecoder is faster; this is the fallback for everything else.
using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Pollinations.Unity
{
    public static class PollinationsAudio
    {
        /// <summary>
        /// Decode audio bytes (mp3, ogg, wav) into an AudioClip using Unity's native
        /// multimedia loader. Must be called from the main thread.
        /// </summary>
        public static Task<AudioClip> LoadClipAsync(byte[] audioBytes, string mimeType, string clipName = "pollinations-audio")
        {
            if (audioBytes == null || audioBytes.Length == 0)
                return Task.FromException<AudioClip>(new PollinationsException("No audio bytes to decode."));
            // UnityWebRequestMultimedia needs a URI; wrap the bytes in a data URI so we
            // don't touch disk. AudioType is inferred from the mime type.
            var audioType = AudioTypeFromMime(mimeType);
            string base64 = Convert.ToBase64String(audioBytes);
            var uri = new Uri($"data:audio/{audioType.ToString().ToLowerInvariant()};base64,{base64}");

            var www = UnityWebRequestMultimedia.GetAudioClip(uri, audioType);
            var tcs = new TaskCompletionSource<AudioClip>();
            var op = www.SendWebRequest();
            op.completed += _ =>
            {
                try
                {
                    if (www.result != UnityWebRequest.Result.Success)
                    {
                        // Discard www.error detail: can echo URLs with embedded credentials.
                        tcs.SetException(new PollinationsException($"Audio decoding failed (HTTP {(int)www.responseCode})."));
                        return;
                    }
                    tcs.SetResult(DownloadHandlerAudioClip.GetContent(www));
                }
                catch (Exception e) { tcs.SetException(e); }
                finally { www.Dispose(); }
            };
            return tcs.Task;
        }

        private static AudioType AudioTypeFromMime(string mime)
        {
            if (string.IsNullOrEmpty(mime)) return AudioType.UNKNOWN;
            if (mime.Contains("mpeg") || mime.Contains("mp3")) return AudioType.MPEG;
            if (mime.Contains("ogg")) return AudioType.OGGVORBIS;
            if (mime.Contains("wav")) return AudioType.WAV;
            if (mime.Contains("aiff")) return AudioType.AIFF;
            return AudioType.UNKNOWN;
        }
    }
}
