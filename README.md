# Pollinations for Unity

Async C# helpers that add **Pollinations text, image and speech generation** to Unity
games — as a small [Unity Package Manager](https://docs.unity3d.com/Manual/CustomPackages.html)
(UPM) package. Ships with a bring-your-own-key mode for development **and** the
[Pollinations device flow](https://github.com/pollinations/pollinations/blob/main/BRING_YOUR_OWN_POLLEN.md)
so players can pay with their own Pollen.

| | |
|---|---|
| **Unity** | 2021.3+ (built and tested against the public API surface; no beta APIs used) |
| **Dependencies** | None outside `UnityEngine` / `UnityEngine.UnityWebRequestModule` |
| **Models** | Any model from the live [text](https://gen.pollinations.ai/text/models), [image](https://gen.pollinations.ai/image/models) and [audio](https://gen.pollinations.ai/audio/models) lists |
| **License** | MIT |

---

## Install

### Option A — Git URL (works today)

In Unity: **Window → Package Manager → + → "Add package from git URL…"**

```
https://github.com/Guest453/pollinations-unity.git
```

Or add to your project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "dev.guest453.pollinations-unity": "https://github.com/Guest453/pollinations-unity.git",
    "com.unity.nuget.newtonsoft-json": "3.2.1"
  }
}
```

> The Newtonsoft dependency is optional — the package has its own tiny JSON parser and
> does not require it. Remove that line if you don't want it.

### Option B — local checkout

```bash
git clone https://github.com/Guest453/pollinations-unity.git
```

Copy the folder into your project's `Packages/` directory (or add via
**Package Manager → + → "Add package from disk…"** selecting `package.json`).

### Option C — OpenUPM (planned; see [Publishing to OpenUPM](#publishing-to-openupm))

Once the package is listed, install via the OpenUPM CLI:

```bash
openupm add dev.guest453.pollinations-unity
```

---

## Quick start

```csharp
using Pollinations.Unity;

// 1) create a client (once)
var client = new PollinationsClient { ApiKey = "sk_your_dev_key" };

// 2) text — POST /v1/chat/completions (OpenAI-compatible)
var text = await client.TextAsync("You are a friendly NPC.", "Tell me a short joke.");
Debug.Log(text.Text);           // the assistant reply
Debug.Log(text.Model);          // which model answered

// 3) image — POST /v1/images/generations (b64_json, decoded into a Texture2D)
var img = await client.ImageAsync("a cozy tavern, pixel art", width: 512, height: 512);
rawImage.texture = img.Texture; // ready to slap on a RawImage / material

// 4) speech — POST /v1/audio/speech (wav decodes in-memory, mp3 via the loader)
var speech = await client.SpeechAsync(text.Text, voice: "alloy");
audioSource.clip = client.GetAudioClip(speech);
audioSource.Play();
```

All methods are plain `Task`-based and safe to `await` from Unity code. Continuations
resume on the Unity main thread, so you can touch UI/GameObject state directly.

### Choosing models

Every method takes an optional `model` parameter. Any name from the live model lists works:

```csharp
// fetch the live list (also available: Image, Audio)
var models = await PollinationsModels.FetchAsync(PollinationsModality.Text);
foreach (var m in models) Debug.Log($"{m.Name} — {m.Title} ({m.Publisher})");

var reply = await client.TextAsync(msgs, model: "anthropic/claude-opus-5.5");
var pic   = await client.ImageAsync("dragon", model: "black-forest-labs/flux.1.1-pro");
```

Defaults are set on the client (`TextModel`, `ImageModel`, `SpeechModel`) so you can
configure once and forget.

---

## Dev key vs. players paying with their own Pollen

Two authentication modes, selected automatically:

| Mode | How | Who pays |
|---|---|---|
| **Dev key** | `client.ApiKey = "sk_..."` (get one at [enter.pollinations.ai/keys](https://enter.pollinations.ai/keys)) | Your account |
| **Device flow (BYOP)** | Leave `ApiKey` empty and run `PollinationsAuth` | The player's Pollen |

> **Never ship a dev key.** Set `ApiKey` in code or from an environment variable
> (the bundled demo reads `POLLINATIONS_DEV_KEY`), never in a serialized scene or
> prefab field: Unity stores serialized strings in the scene asset and the player
> build, so a pasted key would ship with the game. Players use the device flow.

### Device flow (BYOP)

```csharp
var auth = new PollinationsAuth("pk_your_publishable_key");
var result = await auth.AuthorizeAsync(
    p => statusLabel.text = $"{p.State}: {p.Message}",
    destroyCancellationToken);

if (result.State == DeviceFlowState.Granted)
{
    client.DeviceToken = result.AccessToken; // SECRET — keep in memory only
    // every request now bills the player's Pollen, not yours
}
```

What the player sees: the game shows a short code (`result.UserCode`, e.g. `ABCD-1234`)
and opens [enter.pollinations.ai/device](https://enter.pollinations.ai/device); they
approve there, and the game picks the token up automatically. This is the exact flow
documented in
[BRING_YOUR_OWN_POLLEN.md](https://github.com/pollinations/pollinations/blob/main/BRING_YOUR_OWN_POLLEN.md).

> 🔐 **Secret hygiene:** `sk_` keys and device-flow tokens are secrets. Never commit them,
> never log them, never put them in builds. This package deliberately never logs tokens
> and never prints response bodies of failed (401/402/403) requests, since those can echo
> credentials back.

---

## Demo scene

A full working demo lives at `Samples~/DemoScene` (import via **Package Manager →
Samples → Demo Scene**, or copy the folder into `Assets/`). It wires up:

- a text prompt field + reply label (`TextAsync`)
- a RawImage that receives the generated texture (`ImageAsync`)
- an AudioSource that plays generated speech (`SpeechAsync` + `GetAudioClip`)
- a **Connect Pollen** button that runs the device flow end to end

See [Samples~/DemoScene/README.md](Samples~/DemoScene/README.md) for a walkthrough and
code excerpts.

---

## API reference

| Method | Endpoint | Notes |
|---|---|---|
| `TextAsync(messages, model?, temperature?, maxTokens?)` | `POST /v1/chat/completions` | OpenAI-compatible chat |
| `TextAsync(system, user, model?)` | `POST /v1/chat/completions` | convenience overload |
| `ImageAsync(prompt, model?, width, height)` | `POST /v1/images/generations` | returns `Texture2D` + PNG bytes |
| `SpeechAsync(text, model?, voice?, mimeType?)` | `POST /v1/audio/speech` | default `audio/wav` |
| `GetAudioClip(SpeechResult)` | — | WAV → `AudioClip` (in-memory) |
| `PollinationsAudio.LoadClipAsync(bytes, mime)` | — | mp3/ogg → `AudioClip` via Unity loader |
| `PollinationsModels.FetchAsync(modality, …)` | `GET /{text\|image\|audio}/models` | live model list |
| `PollinationsAuth.AuthorizeAsync(…)` | `POST /api/device/code` + `/api/device/token` | device flow |

Errors surface as `PollinationsException` with friendly, status-derived messages
(401 → "reconnect", 402 → "out of Pollen", 429 → "rate limited", …) that never leak
the bearer token.

---

## What's verified vs. not (honesty section)

**Verified in this repo (reproducible):**

- ✅ **Live end-to-end generation through the package's own client code** —
  [`Tests/LiveHarness`](Tests/LiveHarness/Program.cs) drives the real `PollinationsClient`
  (payload builders → MiniJson serializer → HTTP → response parsers → WAV decoder) with an
  HttpClient transport standing in for UnityWebRequest. Output from 2026-09-27:
  - **Text** `POST /v1/chat/completions` → `gpt-5.4-nano-2026-03-17`, usage prompt=28 completion=10,
    reply: *"Welcome players to the Unity demo!"*
  - **Speech** `POST /v1/audio/speech` → 183,534 bytes `audio/wav` → decoded by the package's
    `WavDecoder` to 91,728 samples, mono, 44,100 Hz
  - **Image** `POST /v1/images/generations` → 47,327-byte image via the package's payload/parse path
    (note: `z-image-turbo` returns **JPEG** bytes; `Texture2D.LoadImage` decodes both JPEG and PNG,
    and `ImageResult.ImageBytes` reflects whatever the model returned)
- ✅ **36/36 xUnit tests** on .NET 8 (`dotnet test Tests/Pollinations.Tests.csproj`): JSON
  round-trips incl. surrogate pairs, chat/image parsers against real-response fixtures,
  payload builders vs. the live OpenAPI schema, header/token selection, WAV decode +
  rejection paths, device flow vs. scripted fake transport (granted / denied / slow_down /
  pk_ validation).
- ✅ Package layout follows UPM structure; `package.json` + both asmdefs validated as JSON.
- ✅ Endpoint shapes captured from `gen.pollinations.ai/openapi.json` and
  `BRING_YOUR_OWN_POLLEN.md` (2026-09).

**Not verified here (no Unity editor in this environment):**

- ⚠️ Compilation of the three Unity-facing files (`PollinationsClient.cs` Unity half,
  `PollinationsAudio.cs`, `PollinationsDemo.cs`) inside a Unity editor. They only use
  long-stable APIs (`UnityWebRequest`, `UnityWebRequestMultimedia`, `Texture2D.LoadImage`,
  `AudioClip.Create`), but they have not been compiled by Unity's compiler. The demo scene
  YAML is likewise untested in-editor. If anything doesn't compile in your Unity version,
  please open an issue.

---

## Publishing to OpenUPM

The package is OpenUPM-shaped (`package.json` at the repo root, valid name
`dev.guest453.pollinations-unity`, no external deps beyond Unity modules, versioned git
tag). **A registration PR is already open: [openupm/openupm#7005](https://github.com/openupm/openupm/pull/7005)** —
once merged (new hunters need maintainer approval, usually within 24h), the package is
live and installable via the OpenUPM CLI:

```bash
openupm add dev.guest453.pollinations-unity
```

Manual process (if the PR needs redoing): fork `openupm/openupm`, add
`data/packages/dev.guest453.pollinations-unity.yml` (copy the shape of any existing
file there, e.g. `com.cysharp.unitask.yml`), and open a PR titled
`chore(data): new package dev.guest453.pollinations-unity`. The registry's CI builds
the package from the git tag and publishes it.

---

## License

MIT — see [LICENSE](LICENSE).
