# Pollinations Demo Scene

A ready-to-run example showing all three Pollinations generation types in Unity.

## Setup

1. Install the package (see the root [README](../../README.md#install)).
2. In Unity: **Window → Package Manager → + → "Import sample" → Demo Scene**
   (or copy this folder into `Assets/Samples/`).
3. Select the `PollinationsDemo` object and either:
   - paste an `sk_` dev key from [enter.pollinations.ai/keys](https://enter.pollinations.ai/keys)
     into the inspector's `devApiKey` field (dev only — **never ship this in a build**), or
   - leave it empty and click **Connect Pollen** at runtime to run the device flow so
     players pay with their own Pollen.
4. Press Play, type a prompt, and try the three buttons:
   - **Generate Text** — fills the reply label from `POST /v1/chat/completions`
   - **Generate Image** — renders on the RawImage from `POST /v1/images/generations`
   - **Speak** — plays generated speech via `POST /v1/audio/speech`

## What the demo does under the hood

```csharp
var client = new PollinationsClient { ApiKey = devApiKey };

// text
var text = await client.TextAsync("You are a helpful game NPC.", "Tell me a joke");

// image
var img = await client.ImageAsync("a cozy tavern, pixel art", width: 512, height: 512);
rawImage.texture = img.Texture;

// speech (wav decodes in-memory; mp3 via PollinationsAudio.LoadClipAsync)
var speech = await client.SpeechAsync(text.Text);
audioSource.clip = client.GetAudioClip(speech);
audioSource.Play();
```

## Device flow (players pay with their own Pollen)

```csharp
var auth = new PollinationsAuth("pk_your_publishable_key");
var result = await auth.AuthorizeAsync(
    p => statusLabel.text = $"{p.State}: {p.Message}",
    destroyCancellationToken);
if (result.State == DeviceFlowState.Granted)
    client.DeviceToken = result.AccessToken; // SECRET — keep in memory only
```

`result.UserCode` is what the player types at enter.pollinations.ai/device;
`result.VerificationUri` is where they go to approve.
