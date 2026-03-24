# Octolink Installer

This folder contains scripts to build a professional Windows installer for Octolink.

## Quick Start

### Option 1: Using Inno Setup (Recommended)

1. **Install Inno Setup 6** from https://jrsoftware.org/isdl.php

2. **Run the build script:**
   ```
   BuildInstaller.bat
   ```

3. The installer will be created at `installer_output\OctolinkSetup.exe`

The installer is updateable: installing a newer build with the same app id will replace the old version and keep the app entry.

### Option 2: Manual Installation Package

If you don't want to use Inno Setup, run:
```
BuildPortable.bat
```

This creates a ZIP file with the application and a setup script.

The helper scripts now build from the root `Octolink.sln`, so they match the current project layout.

## What the Installer Does

The installer performs the following:

1. **Installs Octolink** to Program Files
2. **Installs ViGEmBus Driver** (required for virtual controllers)
3. **Configures Windows Firewall** (allows ports 5000 and 5001)
4. **Sets up URL ACLs** (allows the server to bind to network addresses)
5. **Creates shortcuts** in Start Menu and optionally on Desktop
6. **Checks for ViGEmBus** and installs it automatically if missing

## Prerequisites for Building

- .NET 8 SDK
- Inno Setup 6 (for full installer)
- Internet connection (to download ViGEmBus)

## Files

| File | Description |
|------|-------------|
| `OctolinkSetup.iss` | Inno Setup script |
| `BuildInstaller.bat` | Builds the full installer |
| `BuildPortable.bat` | Creates a portable ZIP package |
| `dependencies/` | Downloaded dependencies (ViGEmBus) |

## Customization

### Adding an Icon

Place an `icon.ico` file in the `Octolink` folder to use a custom icon for the installer and shortcuts.

### Changing Version Number

Edit `OctolinkSetup.iss` and change:
```
#define MyAppVersion "1.0.0"
```

## Uninstallation

The installer creates a proper uninstaller that:
- Removes all installed files
- Removes firewall rules
- Removes URL ACL reservations
- Removes Start Menu and Desktop shortcuts

Note: ViGEmBus is NOT uninstalled automatically as other applications may depend on it.
