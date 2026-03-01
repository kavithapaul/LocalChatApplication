# LocalChatApplication (WPF, fully local)

This repository now contains a **C# WPF desktop app** that runs chat, speech-to-text, and image generation on your own machine.

## Implemented UI requirements

- Multiline prompt textbox.
- Microphone icon button next to prompt box.
- Multiline response textbox.
- ChatGPT-like dark style.
- `Ask` button that routes to local model.
- Prompt router: text prompts go to local Mistral, image prompts (e.g., "create an image of a black cat") go to local image model.

## Best local stack (free and offline after setup)

### 1) Text model (Mistral 7B)
**Recommended:** `ollama` + `mistral` model.

Why:
- Easiest local serving API for C# (`http://localhost:11434/api/generate`).
- Free.
- Works fully offline after model is pulled.

Setup (once):

```bash
# Install ollama from official installer.
ollama pull mistral
ollama run mistral "test"
```

### 2) Speech-to-text (STT)
**Recommended:** `Whisper` local model via `Whisper.net` (`ggml-base.bin` or `ggml-small.bin`).

Why:
- High quality offline STT.
- Fully local inference.
- No cloud API cost.

Setup (once):
1. Download a GGML Whisper model file (recommended `ggml-base.bin`).
2. Place it at:

```text
LocalChatApp/models/ggml-base.bin
```

### 3) Image generation model
**Recommended:** `Stable Diffusion 1.5` (or `SDXL-turbo` for faster inference if GPU is strong) served by **AUTOMATIC1111** local API.

Why:
- Fully local + free.
- Mature tooling and API (`/sdapi/v1/txt2img`).
- Huge community support.

Setup (once):
1. Install AUTOMATIC1111 WebUI locally.
2. Download SD 1.5 checkpoint (`.safetensors`) into its `models/Stable-diffusion` folder.
3. Launch with API enabled:

```bash
# Example flag
webui-user.bat --api
```

The app expects image API at `http://127.0.0.1:7860`.

## Run the app

```bash
dotnet build LocalChatApp/LocalChatApp.csproj
# Run on Windows:
dotnet run --project LocalChatApp/LocalChatApp.csproj
```


## Chat responsiveness improvements

- The chat client now uses Ollama streaming (`stream: true`) so text appears token-by-token instead of waiting for a full completion.
- Generation parameters were tuned for lower latency (`num_predict=256`) while keeping quality stable.
- HTTP calls use response-header streaming mode to start reading output as soon as the first token arrives.

## Routing behavior

In `MainViewModel`, prompt routing is keyword-based:
- `create an image...`
- `generate an image...`
- contains `image of`
- contains `draw`

These prompts call Stable Diffusion API; all others call Mistral.

## Notes for true offline use

- Do one-time downloads while online (Ollama model, Whisper model, SD model).
- After that, disconnect internet and run all services locally.
- No paid APIs are used.

## Troubleshooting image generation (CMD)

If image prompts stay on "Generating image" or fail with connection errors, check the local SD API from **Command Prompt**:

```cmd
netstat -ano | findstr :7860
curl http://127.0.0.1:7860/sdapi/v1/sd-models
```

- If `curl` fails with connection refused, launch AUTOMATIC1111 with API enabled:

```cmd
webui-user.bat --api
```

- After it starts, re-run:

```cmd
curl http://127.0.0.1:7860/sdapi/v1/sd-models
```

A JSON response means the image model endpoint is available.

