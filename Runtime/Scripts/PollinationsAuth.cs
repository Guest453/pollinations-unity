// PollinationsAuth — BYOP device flow so players pay with their own Pollen.
//
// Verified against BRING_YOUR_OWN_POLLEN.md (2026-09):
//   1. POST https://enter.pollinations.ai/api/device/code   {"client_id":"pk_..."}
//        → { device_code, user_code, verification_uri, verification_uri_complete?, expires_in, interval? }
//   2. User opens verification_uri (enter.pollinations.ai/device) and enters user_code.
//   3. POST https://enter.pollinations.ai/api/device/token  {"device_code":"..."}  every ~5s
//        pending → {"error":"authorization_pending"}   (also: slow_down, access_denied, expired_token)
//        done    → {"access_token":"sk_...","token_type":"bearer"}
//
// The resulting sk_ token is a SECRET: keep it out of logs/screenshots and never commit it.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Pollinations.Unity
{
    /// <summary>State of a device-flow authorization attempt.</summary>
    public enum DeviceFlowState
    {
        Idle,
        RequestingCode,
        WaitingForUser,   // user_code ready; player must approve in browser
        Granted,
        Denied,
        Expired,
        Failed
    }

    /// <summary>Progress report for UI binding.</summary>
    public sealed class DeviceFlowProgress
    {
        public DeviceFlowState State;
        public string UserCode;           // e.g. "ABCD-1234" — show this to the player
        public string VerificationUri;    // e.g. "https://enter.pollinations.ai/device"
        public string Message;
        public string AccessToken;        // only set when State == Granted (SECRET — do not log)
    }

    /// <summary>
    /// Runs the Pollinations device flow. Usage:
    /// <code>
    /// var auth = new PollinationsAuth { ClientId = "pk_your_publishable_key" };
    /// var result = await auth.AuthorizeAsync(p => Debug.Log(p.Message), CancellationToken.None);
    /// if (result.State == DeviceFlowState.Granted) client.DeviceToken = result.AccessToken;
    /// </code>
    /// </summary>
    public sealed class PollinationsAuth
    {
        /// <summary>Publishable key (pk_...) identifying this app for Pollen attribution. Optional but recommended.</summary>
        public string ClientId = "";

        /// <summary>Seconds between token polls. The API documents 5s; slow_down responses grow this.</summary>
        public int PollIntervalSeconds = 5;

        public PollinationsAuth() { }
        public PollinationsAuth(string clientId) { ClientId = clientId; }

        /// <summary>
        /// Runs the full device flow. onProgress is invoked from background continuations —
        /// marshal to the main thread (e.g. UnityMainThreadDispatcher) before touching UI.
        /// </summary>
        public async Task<DeviceFlowProgress> AuthorizeAsync(Action<DeviceFlowProgress> onProgress, CancellationToken cancel)
        {
            void Report(DeviceFlowProgress p) => onProgress?.Invoke(p);

            // ---- Step 1: request a device code ----
            Report(new DeviceFlowProgress { State = DeviceFlowState.RequestingCode, Message = "Requesting device code..." });
            var codePayload = string.IsNullOrEmpty(ClientId)
                ? new System.Collections.Generic.Dictionary<string, object>()
                : new System.Collections.Generic.Dictionary<string, object> { ["client_id"] = ClientId };
            if (!string.IsNullOrEmpty(ClientId) && !ClientId.StartsWith("pk_", StringComparison.Ordinal))
                throw new PollinationsException("ClientId must be a publishable pk_ key (not sk_).");

            var codeResp = await Internal.PollinationsTransport.SendAsync(
                new Internal.PollinationsRequest(
                    PollinationsClient.AuthBase + "/api/device/code", "POST",
                    Internal.MiniJson.ToJson(codePayload),
                    Internal.PollinationsTransport.JsonHeaders(null, "Pollinations-Unity/1.0", false)));
            if (codeResp.Status >= 400) throw PollinationsException.FromStatus((int)codeResp.Status);

            var code = ParseDictionary(codeResp, "device code");
            string deviceCode = Internal.MiniJson.GetString(code, "device_code");
            string userCode = Internal.MiniJson.GetString(code, "user_code");
            string verificationUri = Internal.MiniJson.GetString(code, "verification_uri");
            double? expiresIn = Internal.MiniJson.GetNumber(code, "expires_in");
            double? interval = Internal.MiniJson.GetNumber(code, "interval");

            if (string.IsNullOrEmpty(deviceCode) || string.IsNullOrEmpty(userCode) || string.IsNullOrEmpty(verificationUri))
                throw new PollinationsException("Device authorization response was missing required fields.");

            // Handle a relative verification_uri ("/device") per the docs.
            if (verificationUri.StartsWith("/", StringComparison.Ordinal))
                verificationUri = PollinationsClient.AuthBase + verificationUri;

            var started = DateTime.UtcNow;
            var deadline = started.AddSeconds(expiresIn ?? 600);
            var intervalSec = Math.Max(1.0, interval ?? PollIntervalSeconds);

            Report(new DeviceFlowProgress
            {
                State = DeviceFlowState.WaitingForUser,
                UserCode = userCode,
                VerificationUri = verificationUri,
                Message = $"Go to {verificationUri} and enter code {userCode}"
            });

            // ---- Step 3: poll for the token ----
            int slowDownExtra = 0;
            while (DateTime.UtcNow < deadline)
            {
                var waitSeconds = intervalSec + slowDownExtra;
                for (var waited = 0.0; waited < waitSeconds; waited += 0.25)
                {
                    cancel.ThrowIfCancellationRequested();
                    await Task.Delay(250, cancel);
                }

                Internal.PollinationsResponse tokenResp;
                try
                {
                    tokenResp = await Internal.PollinationsTransport.SendAsync(
                        new Internal.PollinationsRequest(
                            PollinationsClient.AuthBase + "/api/device/token", "POST",
                            Internal.MiniJson.ToJson(new System.Collections.Generic.Dictionary<string, object> { ["device_code"] = deviceCode }),
                            Internal.PollinationsTransport.JsonHeaders(null, "Pollinations-Unity/1.0", false)));
                }
                catch (PollinationsException pe) when (pe.StatusCode == 0)
                {
                    // Transient network error while polling — keep trying until the code expires.
                    continue;
                }

                if (tokenResp.Status >= 400)
                {
                    // The token endpoint signals pending/denied via HTTP 400 + JSON error field
                    // (per RFC 8628, which the API follows) — parse the body before failing hard.
                    var errObj = TryParse(tokenResp);
                    var err = Internal.MiniJson.GetString(errObj, "error");
                    if (HandleError(err, ref slowDownExtra)) continue;
                    if (err == "access_denied")
                    {
                        Report(new DeviceFlowProgress { State = DeviceFlowState.Denied, Message = "Authorization declined." });
                        return new DeviceFlowProgress { State = DeviceFlowState.Denied, Message = "Authorization declined." };
                    }
                    if (err == "expired_token") break;
                    throw PollinationsException.FromStatus((int)tokenResp.Status);
                }

                var tok = ParseDictionary(tokenResp, "token response");
                var token = Internal.MiniJson.GetString(tok, "access_token");
                if (!string.IsNullOrEmpty(token))
                {
                    var done = new DeviceFlowProgress
                    {
                        State = DeviceFlowState.Granted,
                        AccessToken = token,
                        UserCode = userCode,
                        VerificationUri = verificationUri,
                        Message = "Authorization complete."
                    };
                    Report(done);
                    return done;
                }

                var softErr = Internal.MiniJson.GetString(tok, "error");
                if (softErr != null)
                {
                    if (HandleError(softErr, ref slowDownExtra)) continue;
                    if (softErr == "access_denied")
                    {
                        Report(new DeviceFlowProgress { State = DeviceFlowState.Denied, Message = "Authorization declined." });
                        return new DeviceFlowProgress { State = DeviceFlowState.Denied, Message = "Authorization declined." };
                    }
                    if (softErr == "expired_token") break;
                }
            }

            Report(new DeviceFlowProgress { State = DeviceFlowState.Expired, Message = "Device code expired — start again." });
            return new DeviceFlowProgress { State = DeviceFlowState.Expired, Message = "Device code expired — start again." };
        }

        /// <summary>
        /// Returns true when the error means "keep polling" (authorization_pending / slow_down);
        /// slow_down also grows the interval by 5s per RFC 8628.
        /// </summary>
        private static bool HandleError(string err, ref int slowDownExtra)
        {
            if (err == "authorization_pending") return true;
            if (err == "slow_down") { slowDownExtra += 5; return true; }
            return false;
        }

        private static System.Collections.Generic.IDictionary<string, object> ParseDictionary(Internal.PollinationsResponse resp, string what)
        {
            try
            {
                return Internal.MiniJson.Parse(System.Text.Encoding.UTF8.GetString(resp.Body)) as System.Collections.Generic.IDictionary<string, object>;
            }
            catch (FormatException e)
            {
                throw new PollinationsException($"Could not parse {what} response: " + e.Message, e);
            }
        }

        private static System.Collections.Generic.IDictionary<string, object> TryParse(Internal.PollinationsResponse resp)
        {
            try { return ParseDictionary(resp, "error"); }
            catch (PollinationsException) { return null; }
        }
    }
}
