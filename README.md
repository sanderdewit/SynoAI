# SynoAI

A Synology Surveillance Station notification system that uses an AI object-detection backend to reduce false-positive motion alerts and deliver timely, annotated notifications via your preferred channel.

Inspired by Christopher Adams' [sssAI](https://github.com/Christofo/sssAI) and originally developed by [djdd87](https://github.com/djdd87/SynoAI). This fork ([sanderdewit](https://github.com/sanderdewit)) is actively maintained and updated.

![Build](https://github.com/sanderdewit/SynoAI/actions/workflows/compile_upload.yml/badge.svg)

## How it works

1. Surveillance Station detects motion and calls the SynoAI webhook via an Action Rule.
2. SynoAI fetches a snapshot from the Synology Camera API.
3. The snapshot is sent to your configured AI backend (DeepStack or CodeProject.AI).
4. If the AI finds objects that match your configured types (e.g. Person, Car) above the confidence threshold, SynoAI annotates the image with bounding boxes and sends a notification.

This bypasses Synology's built-in notification system entirely, giving you a fresh, AI-processed image at the moment of detection rather than a delayed or duplicate alert.

## Requirements

- .NET 10 runtime (Docker image based on `mcr.microsoft.com/dotnet/aspnet:10.0`)
- A running AI backend: [DeepStack](https://deepstack.cc/) or [CodeProject.AI Server](https://github.com/codeproject/CodeProject.AI-Server/)
- A Synology NAS with Surveillance Station

## Table of Contents

- [Quick start](#quick-start)
- [Configuration reference](#configuration-reference)
  - [General](#general)
  - [Cameras](#cameras)
  - [Camera API (enable/disable)](#camera-api)
  - [Development](#development)
- [Supported AIs](#supported-ais)
  - [DeepStack](#deepstack)
  - [CodeProject.AI Server](#codeprojectai-server)
- [Notifiers](#notifiers)
  - [Pushbullet](#pushbullet)
  - [Webhook](#webhook)
  - [Telegram](#telegram)
  - [Email](#email)
  - [HomeAssistant](#homeassistant)
  - [Pushover](#pushover)
  - [Discord](#discord)
  - [MQTT](#mqtt)
  - [SynologyChat](#synologychat)
- [Docker](#docker)
  - [Docker run](#docker-run)
  - [Docker Compose](#docker-compose)
- [Example appsettings.json](#example-appsettingsjson)
- [Setting up Surveillance Station Action Rules](#setting-up-surveillance-station-action-rules)
- [Troubleshooting](#troubleshooting)
- [FAQ](#faq)

---

## Quick start

1. Create a folder for SynoAI (e.g. `/docker/synoai/`).
2. Copy `appsettings.json` to that folder and fill in your NAS URL, credentials, cameras, AI, and notifiers.
3. Optionally create a `Captures/` subfolder for saved images.
4. Run with Docker (see [Docker run](#docker-run) below).
5. Add an Action Rule in Surveillance Station pointing at `http://{SynoAI-IP}:{Port}/Camera/{CameraName}`.

---

## Configuration reference

All configuration lives in `appsettings.json`. An [example file](#example-appsettingsjson) is at the bottom of this document.

### General

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `Url` | Yes | — | URL and port of your Synology NAS, e.g. `http://10.0.0.10:5000` |
| `User` | Yes | — | Synology user account used to fetch camera snapshots |
| `Password` | Yes | — | Password for the account above (sent securely via HTTP POST, never in the URL) |
| `AllowInsecureUrl` | No | `false` | Allow self-signed or invalid HTTPS certificates when connecting to the NAS |
| `SynoAIUrl` | No | — | The URL at which SynoAI itself is reachable. Used to embed image links in some notifiers |
| `Quality` | No | `Balanced` | Snapshot profile type: `High`, `Balanced`, or `Low` |
| `MinSizeX` | No | `50` | Global minimum object width in pixels. Overridden per camera |
| `MinSizeY` | No | `50` | Global minimum object height in pixels. Overridden per camera |
| `MaxSizeX` | No | *(none)* | Global maximum object width in pixels. Overridden per camera |
| `MaxSizeY` | No | *(none)* | Global maximum object height in pixels. Overridden per camera |
| `Delay` | No | `5000` | Cooldown in ms between the end of one detection and the next for the same camera |
| `DelayAfterSuccess` | No | *(uses `Delay`)* | Cooldown in ms after a detection that resulted in a notification being sent |
| `MaxSnapshots` | No | `1` | Maximum number of sequential snapshots to attempt before giving up on the current motion event |
| `DrawMode` | No | `Matches` | `Matches` — draw boxes only around matched objects; `All` — draw all AI detections; `Off` — no drawing |
| `DrawExclusions` | No | `false` | Draw exclusion zone outlines on the image (useful when configuring zones) |
| `BoxColor` | No | `#FF0000` | Bounding box border colour |
| `TextBoxColor` | No | `#00FFFFFF` (transparent) | Background colour behind label text |
| `ExclusionBoxColour` | No | `#00FF00` | Exclusion zone box colour |
| `StrokeWidth` | No | `2` | Bounding box border width in pixels |
| `Font` | No | `Tahoma` | Font for bounding box labels |
| `FontSize` | No | `12` | Font size in pixels |
| `FontColor` | No | `#00FF00` | Label text colour |
| `TextOffsetX` | No | `2` | Label horizontal offset in pixels from the left edge of the box |
| `TextOffsetY` | No | `2` | Label vertical offset in pixels from the top edge of the box |
| `AlternativeLabelling` | No | `false` | When `true` and `DrawMode` is `Matches`, draws sequential numbers (1, 2, 3…) instead of class names on the image; the notification message then lists each object and its confidence |
| `LabelBelowBox` | No | `false` | When `true`, places label text below the bounding box rather than inside or above it |
| `SaveOriginalSnapshot` | No | `Off` | `Off` — never save; `Always` — save every snapshot; `WithPredictions` — save if AI found anything; `WithValidPredictions` — save only if valid objects were found |
| `DaysToKeepCaptures` | No | `0` (forever) | Captures older than this many days are deleted automatically. `0` keeps captures forever |

### Cameras

Defined as an array under the `Cameras` key.

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `Name` | Yes | — | Camera name exactly as it appears in Surveillance Station |
| `Types` | Yes | — | Array of object labels to trigger on, e.g. `["Person", "Car"]` |
| `Threshold` | Yes | — | Minimum AI confidence (0–100) required before a detection is valid, e.g. `45` |
| `MinSizeX` | No | *(global)* | Override global `MinSizeX` for this camera |
| `MinSizeY` | No | *(global)* | Override global `MinSizeY` for this camera |
| `MaxSizeX` | No | *(global)* | Override global `MaxSizeX` for this camera |
| `MaxSizeY` | No | *(global)* | Override global `MaxSizeY` for this camera |
| `Wait` | No | `0` | Milliseconds to wait after the motion event fires before fetching the snapshot. The "running" lock is not held during this wait, so new events can arrive. Useful to let the subject move into frame |
| `Delay` | No | *(global)* | Override global `Delay` for this camera |
| `DelayAfterSuccess` | No | *(global)* | Override global `DelayAfterSuccess` for this camera |
| `MaxSnapshots` | No | *(global)* | Override global `MaxSnapshots` for this camera |
| `Rotate` | No | `0` | Degrees to rotate the image before sending it to the AI (applied before annotation too) |
| `Exclusions` | No | — | Array of zones; detections fully inside (or intersecting) a zone are ignored |

#### Exclusion zones

Each exclusion zone has:

| Key | Required | Description |
|-----|----------|-------------|
| `Start.X` / `Start.Y` | Yes | Top-left corner of the exclusion rectangle |
| `End.X` / `End.Y` | Yes | Bottom-right corner |
| `Mode` | No (`Contains`) | `Contains` — ignore the object only if it is entirely inside the zone; `Intersect` — ignore if any part overlaps the zone |

### Camera API

You can enable or disable a camera at runtime by POSTing JSON to `/Camera/{name}`:

```http
POST http://10.0.0.10:8080/Camera/Driveway
Content-Type: application/json

{ "Enabled": false }
```

POST `{ "Enabled": true }` to re-enable. Only the fields you want to change need to be included.

### Development

These should not normally be changed:

| Key | Default | Description |
|-----|---------|-------------|
| `ApiVersionAuth` | `6` | API version for `SYNO.API.Auth`. SynoAI will use the lesser of this value and the maximum version the NAS reports |
| `ApiVersionCamera` | `9` | API version for `SYNO.SurveillanceStation.Camera`. Same auto-clamping applies |

---

## Supported AIs

Set the `AI.Type` in your config. Both AIs use the same interface — the `Url` and optionally `Path` are the only differences.

### DeepStack

```json
"AI": {
  "Type": "DeepStack",
  "Url": "http://10.0.0.10:83"
}
```

[DeepStack documentation — supported object types](https://docs.deepstack.cc/object-detection/#classes)

### CodeProject.AI Server

```json
"AI": {
  "Type": "CodeProjectAIServer",
  "Url": "http://10.0.0.10:32168"
}
```

[CodeProject.AI API reference](https://www.codeproject.com/AI/docs/api/api_reference.html)

---

## Notifiers

Multiple notifiers can be configured. Each notifier fires for every detection unless you restrict it by `Cameras` or `Types`.

```json
"Notifiers": [
  {
    "Type": "Pushover",
    "ApiKey": "...",
    "UserKey": "...",
    "Cameras": ["Driveway"],
    "Types": ["Person"]
  }
]
```

| Common key | Required | Description |
|------------|----------|-------------|
| `Type` | Yes | One of: `Pushbullet`, `Webhook`, `Telegram`, `Email`, `Pushover`, `Discord`, `MQTT`, `SynologyChat` |
| `Cameras` | No | If set, only fire this notifier for cameras in this list. Empty = all cameras |
| `Types` | No | If set, only fire this notifier when one of these object types was detected. Empty = all types |

### Pushbullet

```json
{
  "Type": "Pushbullet",
  "ApiKey": "o.xxxxxxxxxxxxxxxxxxxxxxxxx"
}
```

| Key | Required | Description |
|-----|----------|-------------|
| `ApiKey` | Yes | Pushbullet API key from your account settings |

### Webhook

Calls an HTTP endpoint with the detection data. When `SendImage` is `true`, the request is `multipart/form-data`; otherwise it is `application/json`.

```json
{
  "Type": "Webhook",
  "Url": "https://server/endpoint",
  "Method": "POST",
  "Authentication": "Bearer",
  "Token": "your-token",
  "SendImage": true,
  "ImageField": "image",
  "AllowInsecureUrl": false
}
```

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `Url` | Yes | — | Endpoint URL |
| `Method` | No | `POST` | `GET`, `POST`, `PUT`, `PATCH`, `DELETE` |
| `Authentication` | No | `None` | `None`, `Basic`, `Bearer` |
| `Username` | No | — | For `Basic` auth |
| `Password` | No | — | For `Basic` auth |
| `Token` | No | — | For `Bearer` auth |
| `ImageField` | No | `image` | Multipart field name for the image |
| `SendImage` | No | `true` | Whether to include the annotated image in POST/PUT/PATCH requests |
| `AllowInsecureUrl` | No | `false` | Skip TLS certificate validation |

#### JSON body (when `SendImage` is `false`)

```json
{
  "camera": "Driveway",
  "foundTypes": ["Car"],
  "predictions": [
    {
      "Label": "car",
      "Confidence": 67.89,
      "MinX": 1738, "MinY": 420,
      "MaxX": 2304, "MaxY": 844,
      "SizeX": 566, "SizeY": 424
    }
  ],
  "message": "Motion detected on Driveway\n\nDetected 1 objects:\nCar",
  "imageUrl": "http://192.168.1.2/Driveway/capture.jpeg"
}
```

### Telegram

```json
{
  "Type": "Telegram",
  "ChatID": "000000000",
  "Token": "bot-token-from-BotFather",
  "PhotoBaseURL": ""
}
```

| Key | Required | Description |
|-----|----------|-------------|
| `ChatID` | Yes | Telegram chat ID to send messages to |
| `Token` | Yes | Bot token from [BotFather](https://core.telegram.org/bots#botfather) |
| `PhotoBaseURL` | No | If you self-host captures via Synology Web Station, set this to the URL of your captures folder. Leave blank to have SynoAI upload the file directly to Telegram |

### Email

```json
{
  "Type": "Email",
  "Sender": "synoai@example.com",
  "Destination": "you@example.com",
  "Host": "smtp.example.com",
  "Port": 587,
  "Username": "synoai@example.com",
  "Password": "password",
  "Encryption": "StartTLS"
}
```

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `Sender` | No | *(uses `Destination`)* | From address |
| `Destination` | Yes | — | To address |
| `Host` | Yes | — | SMTP hostname |
| `Port` | No | `25` | SMTP port |
| `Username` | No | — | SMTP username |
| `Password` | No | — | SMTP password |
| `Encryption` | No | `None` | `None`, `Auto`, `SSL`, `StartTLS`, `StartTLSWhenAvailable` |

> **Gmail note:** You may need to enable App Passwords or allow less-secure app access in your Google account settings.

### HomeAssistant

Use the SynoAI Webhook notifier to push to a HomeAssistant [Push camera](https://www.home-assistant.io/integrations/push/):

```yaml
# configuration.yaml
camera:
  - platform: push
    name: Motion Driveway
    webhook_id: motion_driveway
    timeout: 1
    buffer: 1
```

```json
{
  "Type": "Webhook",
  "Url": "http://homeassistant-ip:8123/api/webhook/motion_driveway"
}
```

The Push camera integration expects the image field to be named `image`, which is the SynoAI default.

### Pushover

```json
{
  "Type": "Pushover",
  "ApiKey": "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
  "UserKey": "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
  "Devices": ["iphone"],
  "Sound": "pushover",
  "Priority": 0,
  "Retry": 60,
  "Expire": 3600
}
```

| Key | Required | Description |
|-----|----------|-------------|
| `ApiKey` | Yes | Pushover application API key |
| `UserKey` | Yes | Pushover user key |
| `Devices` | No | Array of device names. Empty = all devices |
| `Sound` | No | Override the user's default [sound](https://pushover.net/api#sounds) |
| `Priority` | No | [Priority level](https://pushover.net/api#priority): -2 (lowest) to 2 (emergency) |
| `Retry` | No | For emergency priority: retry interval in seconds (minimum 30) |
| `Expire` | No | For emergency priority: how long to retry in seconds (maximum 10800) |

### Discord

```json
{
  "Type": "Discord",
  "Url": "https://discord.com/api/webhooks/..."
}
```

| Key | Required | Description |
|-----|----------|-------------|
| `Url` | Yes | Discord webhook URL ([how to get one](https://support.discord.com/hc/en-us/articles/228383668)) |

### MQTT

Messages are published as JSON to `{BaseTopic}/{CameraName}/notification`.

```json
{
  "Type": "MQTT",
  "Host": "mqtt.example.com",
  "Port": 1883,
  "Username": "user",
  "Password": "password",
  "BaseTopic": "synoai",
  "SendImage": false
}
```

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `Host` | Yes | — | MQTT broker hostname or IP |
| `Port` | No | `1883` | MQTT broker port |
| `Username` | No | — | MQTT username |
| `Password` | No | — | MQTT password |
| `BaseTopic` | No | `synoai` | Root topic prefix |
| `SendImage` | No | `false` | Include the annotated image as a base64-encoded string in the `image` field |

#### Example MQTT payload

```json
{
  "camera": "Driveway",
  "foundTypes": ["Car"],
  "predictions": [
    {
      "Label": "car",
      "Confidence": 67.89,
      "MinX": 1738, "MinY": 420,
      "MaxX": 2304, "MaxY": 844,
      "SizeX": 566, "SizeY": 424
    }
  ],
  "message": "Motion detected on Driveway\n\nDetected 1 objects:\nCar",
  "imageUrl": "https://synoai.example.com/Driveway/capture.jpeg"
}
```

### SynologyChat

```json
{
  "Type": "SynologyChat",
  "Url": "https://your-nas/webapi/entry.cgi?api=SYNO.Chat.External&method=incoming&version=2&token=XXXXXXXX"
}
```

| Key | Required | Description |
|-----|----------|-------------|
| `Url` | Yes | Full incoming webhook URL from your Synology Chat integration settings |

---

## Docker

The Docker image is published to Docker Hub as `dewitauto/synoai-fork`.

### Docker run

```sh
docker run \
  -v /path/to/appsettings.json:/app/appsettings.json \
  -v /path/to/captures:/app/Captures \
  -p 8080:80 \
  dewitauto/synoai-fork:latest
```

The default listen port inside the container is `80`. Map it to any available port on your host.

### Docker Compose

```yaml
services:
  synoai:
    image: dewitauto/synoai-fork:latest
    ports:
      - "8080:80"
    volumes:
      - /docker/synoai/appsettings.json:/app/appsettings.json
      - /docker/synoai/captures:/app/Captures
    environment:
      - TZ=Europe/Amsterdam
    restart: unless-stopped
```

---

## Example appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Warning"
    }
  },

  "Url": "http://10.0.0.10:5000",
  "User": "SynologyUser",
  "Password": "SynologyPassword",

  "MinSizeX": 100,
  "MinSizeY": 100,
  "DaysToKeepCaptures": 14,

  "AI": {
    "Type": "CodeProjectAIServer",
    "Url": "http://10.0.0.10:32168"
  },

  "Notifiers": [
    {
      "Type": "Pushover",
      "ApiKey": "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
      "UserKey": "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
    },
    {
      "Type": "Webhook",
      "Url": "http://homeassistant:8123/api/webhook/motion_driveway",
      "Cameras": ["Driveway"]
    }
  ],

  "Cameras": [
    {
      "Name": "Driveway",
      "Types": ["Person", "Car"],
      "Threshold": 45,
      "MinSizeX": 250,
      "MinSizeY": 500
    },
    {
      "Name": "SideGate",
      "Types": ["Person"],
      "Threshold": 40,
      "Wait": 2500
    },
    {
      "Name": "BackDoor",
      "Types": ["Person"],
      "Threshold": 30,
      "Exclusions": [
        {
          "Start": { "X": 1800, "Y": 400 },
          "End":   { "X": 2350, "Y": 900 }
        },
        {
          "Start": { "X": 0, "Y": 0 },
          "End":   { "X": 200, "Y": 500 }
        }
      ]
    }
  ]
}
```

---

## Setting up Surveillance Station Action Rules

1. Open **Surveillance Station → Action Rules → Add**.
2. **Name**: `Trigger SynoAI — Driveway` (or similar)
3. **Rule type**: Triggered; **Action type**: Interruptible
4. **Event** tab: Source = Camera, Device = your camera, Event = Motion Detected
5. **Action** tab:
   - Action device: Webhook
   - URL: `http://{SynoAI-IP}:{Port}/Camera/{CameraName}` (e.g. `http://10.0.0.10:8080/Camera/Driveway`)
   - Method: GET
   - Username / Password: blank
6. Click **Test Send** — if SynoAI is running you should get a green tick.
7. Repeat for each camera.

> **Camera names with spaces:** encode spaces as `%20` in the URL, e.g. `/Camera/Back%20Door`. Surveillance Station does not automatically encode them.

---

## Troubleshooting

### Enable verbose logging

```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft": "Warning",
    "Microsoft.Hosting.Lifetime": "Information"
  }
}
```

### Common issues

#### Not receiving notifications

- Check that there is no **SSS motion debounce delay** set in your camera's motion detection settings.
- Check logs for performance issues (slow snapshots, AI timeouts).
- Verify that your `Types` list matches the labels the AI returns (case-insensitive, but spelling must match).

#### Snapshots all return the same image

Security cameras use I/P/B frame encoding. The Synology API returns the latest I-frame, which may be several seconds old. Set `Wait` on the camera (e.g. `"Wait": 1000`) to delay the snapshot request until the scene has updated. Some cameras have a "Smart Codec" or bandwidth-saving setting that extends the I-frame interval — disable it.

#### Snapshots are slow

- Use the NAS IP address rather than a hostname (avoids DNS lookup latency).
- Try `Quality: Low` to reduce the image size being transferred.
- Check Surveillance Station stream settings — a 4K stream at `Quality: High` is much slower than a 1080p stream.

#### Timeout errors at startup or during AI calls

```
System.Threading.Tasks.TaskCanceledException: The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.
```

This is a networking issue. When running all containers on Synology DSM with the Docker bridge network, use the Docker bridge gateway IP instead of `127.0.0.1` or the NAS IP.

#### Synology API error codes

| Code | Meaning |
|------|---------|
| 100 | Unknown error |
| 101 | Invalid parameters |
| 104 | API version not supported |
| 105 | Insufficient privilege — check user permissions |
| 106 | Session timed out — SynoAI will re-authenticate automatically |
| 107 | Multiple login detected |
| 400 | Invalid password |
| 401 | Guest or disabled account |
| 402 | Permission denied |
| 403 | OTP not specified (2FA not supported — use a dedicated account without 2FA) |
| 407 | Account blocked (too many failed attempts) |
| 411 | Account locked |

#### Image timezone is wrong

Set the `TZ` environment variable in your Docker config, e.g. `TZ=Europe/Amsterdam`.

---

## FAQ

### How do I send notifications only for specific cameras?

Add a `Cameras` array to the notifier config:

```json
"Notifiers": [
  { "Type": "Webhook", "Url": "https://server/cam1", "Cameras": ["Camera1"] },
  { "Type": "Webhook", "Url": "https://server/cam2", "Cameras": ["Camera2"] }
]
```

### How do I send notifications only for specific object types?

Add a `Types` array to the notifier:

```json
{
  "Type": "Pushover",
  "ApiKey": "...",
  "UserKey": "...",
  "Types": ["Person"]
}
```

### How do I disable a camera temporarily without restarting?

POST to the camera endpoint:

```sh
curl -X POST http://10.0.0.10:8080/Camera/Driveway \
  -H "Content-Type: application/json" \
  -d '{"Enabled": false}'
```
