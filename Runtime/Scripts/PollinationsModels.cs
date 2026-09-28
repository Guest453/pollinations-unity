// PollinationsModels — fetch the live model lists from gen.pollinations.ai.
// Types live in PollinationsModelCatalog.cs; this file adds the network fetch.
using System;
using System.Collections.Generic;
using System.Threading;

namespace Pollinations.Unity
{
    public static class PollinationsModels
    {
        /// <summary>
        /// Fetch a live model list (GET /text/models | /image/models | /audio/models).
        /// Fire from the Unity main thread; continuations resume on the main thread.
        /// </summary>
        public static void FetchAsync(PollinationsModality modality, Action<List<PollinationsModel>> onDone, Action<PollinationsException> onError)
        {
            var url = PollinationsClient.GenBase + "/" + ModalityPath(modality) + "/models";
            var req = new Internal.PollinationsRequest(url, "GET", null,
                Internal.PollinationsTransport.JsonHeaders(null, "Pollinations-Unity/1.0", false));
            Internal.PollinationsTransport.Send(req,
                body => SafeDeliver(() => onDone(PollinationsModel.ListFromJson(System.Text.Encoding.UTF8.GetString(body.Body))), onError),
                onError);
        }

        private static string ModalityPath(PollinationsModality modality)
        {
            switch (modality)
            {
                case PollinationsModality.Text: return "text";
                case PollinationsModality.Image: return "image";
                case PollinationsModality.Audio: return "audio";
                default: return "text";
            }
        }

        private static void SafeDeliver(Action a, Action<PollinationsException> onError)
        {
            try { a(); }
            catch (PollinationsException pe) { onError?.Invoke(pe); }
            catch (Exception e) { onError?.Invoke(new PollinationsException("Failed to parse model list: " + e.Message)); }
        }

        /// <summary>Synchronous helper for editor tools/tests (blocks; do not call from gameplay).</summary>
        public static List<PollinationsModel> Fetch(PollinationsModality modality)
        {
            List<PollinationsModel> result = null;
            PollinationsException error = null;
            using (var done = new ManualResetEvent(false))
            {
                FetchAsync(modality,
                    models => { result = models; done.Set(); },
                    err => { error = err; done.Set(); });
                done.WaitOne(30000);
            }
            if (error != null) throw error;
            return result;
        }
    }
}
