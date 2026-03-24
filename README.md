# Octolink

Turn your phone into a wireless game controller. Supports up to 8 players over local WiFi.

## Features

- Up to 8 simultaneous controllers
- QR code pairing
- Real-time input display on PC
- Low latency WebSocket communication
- Browser-based (no app install required)
- Auto-reconnect support

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
dotnet restore
dotnet build Octolink.sln --configuration Release
dotnet run --project Octolink/Octolink.csproj
```

Requires .NET 8 SDK and Visual Studio 2022 or VS Code with C# extension.

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

**High latency**
- Use 5GHz WiFi
- Move closer to the router

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

