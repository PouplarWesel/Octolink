# Octolink

Turn your phone into a wireless game controller. Supports up to 8 players over local WiFi.

## Features

- Up to 8 simultaneous controllers
- QR code pairing
- Real-time input display on PC
- Low latency WebSocket communication
- Browser-based (no app install required)
- Auto-reconnect support
- Ngrok tunnel support for restricted networks

## Requirements

### ViGEmBus Driver

This application uses ViGEmBus to create virtual controllers. Install the driver before running:

1. Download from https://github.com/nefarius/ViGEmBus/releases
2. Run the installer
3. Restart your computer

### .NET 8 Runtime

Download the .NET 8 Desktop Runtime from https://dotnet.microsoft.com/download/dotnet/8.0

## Installation

### Pre-built Release

1. Download the latest release
2. Extract and run `Octolink.exe`

### Build from Source

```
cd Octolink
dotnet restore
dotnet build --configuration Release
dotnet run
```

Requires .NET 8 SDK and Visual Studio 2022 or VS Code with C# extension.

### Ngrok Support

If your network blocks local-device access, you can tunnel Octolink through ngrok:

1. Start Octolink on the PC
2. Run `ngrok http 5000` and `ngrok http 5001` in separate terminals
3. Set `OCTOLINK_PUBLIC_HTTP_URL` and `OCTOLINK_PUBLIC_WS_URL` if needed
4. Open the public URL on your phone

Octolink will also try to detect a local ngrok agent automatically.

## Usage

### PC

1. Start the server
2. A QR code will appear with your local network URL
3. Connected controllers are displayed in the grid

### Phone

1. Connect to the same WiFi network as your PC
2. Scan the QR code
3. Enter your name and connect
4. A controller slot (1-8) is assigned automatically

To reconnect after disconnection, tap the reconnect button and select your previous controller number.

## Troubleshooting

**Server fails to start**
- Verify ViGEmBus is installed
- Run as Administrator
- Check if port 5000 is in use

**Phone can't connect**
- Confirm both devices are on the same network
- Allow the app through Windows Firewall
- Try entering the URL manually
- If local WiFi is blocked, use ngrok tunnels

**High latency**
- Use 5GHz WiFi
- Move closer to the router
- Prefer a stable direct link or wired PC connection

**Stuck movement / input lag**
- Reconnect the phone tab
- Close other tabs to reduce browser throttling
- Check that the connection status stays green

**Controller not recognized in game**
- Press a button to activate the controller
- Check game input settings
- Restart the game after connecting

## Firewall

Windows will prompt to allow network access on first run. If blocked, add the app manually through Windows Defender Firewall settings.

## Technical Details

| Setting | Value |
|---------|-------|
| HTTP Port | 5000 |
| WebSocket Port | 5001 |
| Slots 1-4 | Xbox 360 |
| Slots 5-8 | DualShock 4 |
| Latency | 5-20ms (local network) |

