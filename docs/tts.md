# Text-to-speech & lipsync

The mod can speak Jun's replies out loud while the text scrolls, with her Live2D mouth moving in sync. Everything lives in `src/Tts/`. Enable it with `[Tts] Enabled = true` (see the [configuration reference](configuration.md#tts--text-to-speech) for all knobs).

## How it works

### Sentence pipelining (`TtsManager`)

`TtsManager` (MonoBehaviour singleton) receives the same clean text stream the dialog writer gets. It:

1. accumulates streamed text into a sentence buffer, cutting at sentence boundaries;
2. fires a synthesis request per sentence **while the previous sentence is still playing** — bounded by `MaxConcurrentRequests` (default 2) via a `SemaphoreSlim`;
3. keeps playback strictly in text order using a queue of pending tasks, regardless of which request finishes first;
4. `BeginUtterance()` / `CompleteUtterance()` bracket one AI reply — the trailing partial sentence is flushed at the end;
5. plays audio through a Unity `AudioClip`/`AudioSource` created from the decoded PCM; a per-sample gain (`Volume`, default 2.5) boosts quiet TTS output beyond `AudioSource.volume`'s 1.0 cap;
6. cancellation (`StopAll()`, called when the chat ends) tears down pending requests and playback.

The result: no wait for the whole reply — the first sentence starts playing as soon as it is synthesized, and chunk N+1 generates while chunk N plays.

### The client (`TtsClient`)

Two API formats, chosen by `[Tts] ApiFormat`:

- **`Jun`** — `GET`/`POST` against the Jun webapp's `/api/tts.php?action=tts` (Kokoro or pocket-tts behind the PHP proxy + NGINX). Reuses the shared authenticated `JunSession` — same login, logging, and rate limiting as the web UI. Uses `[Jun] ApiUrl`; `Voice`, `Engine` (`kokoro`/`pockettts`), and `Speed` come from `[Tts]`.
- **`OpenAI`** — `POST {ApiUrl}/audio/speech` with an OpenAI-style JSON body (`model`, `voice`, `speed`, `response_format: wav`) and optional Bearer key. Compatible with OpenAI TTS, Kokoro-FastAPI, openedai-speech, AllTalk, and similar servers.

Both must return **WAV** bytes.

### Audio decoding (`WavAudio`)

A self-contained RIFF/WAVE parser that walks chunks until it finds `fmt ` and `data`, supporting PCM 8/16/24/32-bit and IEEE float 32-bit, producing interleaved `float[]` samples (−1…1) plus channel count and sample rate. Throws `InvalidDataException` on unsupported input (streamed files with unreliable size fields are tolerated).

### Lipsync (`TtsLipSync`)

While a clip plays, `TtsManager` inspects a sliding ~50 ms window of samples each frame to derive an amplitude, multiplies it by `LipSyncGain`, smooths it (lerp factor 14/s), and hands the 0–1 value to `TtsLipSync.Apply()`. That resolves the bot model's Cubism mouth parameter — trying `ParamMouthOpenY`, `PARAM_MOUTH_OPEN_Y`, `ParamMouthOpen`, `Mouth` in order — and writes it in `LateUpdate` so the value lands **after** the game's own animation pass (the same technique the community moan mod uses). Scene changes destroying the model are handled by re-resolving the parameter; on chat end `Release()` closes the mouth and stops touching the parameter.

## Recommended setups

**Jun webapp stack** (easiest — CPU TTS bundled behind the same login):

```ini
[Tts]
Enabled = true
ApiFormat = "Jun"
Voice = "af_heart"     # any Kokoro voice
Engine = "kokoro"      # or "pockettts"
```

**Any OpenAI-compatible speech server:**

```ini
[Tts]
Enabled = true
ApiFormat = "OpenAI"
ApiUrl = "http://localhost:8880/v1"
Model = "tts-1"
Voice = "af_heart"
```

## Troubleshooting

- No sound: verify `Enabled = true`, the server URL, and (Jun format) valid `[Jun]` credentials. Check `TtsClient`/`TtsManager` lines in `MelonLoader/Latest.log`.
- Audio too quiet/loud: adjust `Volume` (0–5, sample-level gain).
- Mouth barely moves / flaps too hard: adjust `LipSyncGain` (0.1–20).
- Sluggish delivery on a slow server: raise `MaxConcurrentRequests` (up to 8) so more sentences synthesize in parallel.
