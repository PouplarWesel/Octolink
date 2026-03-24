@echo off
setlocal enabledelayedexpansion

echo ============================================
echo   Octolink Installer Builder
echo ============================================
echo.

:: Change to script directory
cd /d "%~dp0"

:: Create directories
if not exist "dependencies" mkdir dependencies
if not exist "..\installer_output" mkdir "..\installer_output"

:: Check for Inno Setup
set "INNO_PATH="
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (
    set "INNO_PATH=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
)
if exist "C:\Program Files\Inno Setup 6\ISCC.exe" (
    set "INNO_PATH=C:\Program Files\Inno Setup 6\ISCC.exe"
)

if "%INNO_PATH%"=="" (
    echo ERROR: Inno Setup 6 not found!
    echo.
    echo Please install Inno Setup 6 from:
    echo https://jrsoftware.org/isdl.php
    echo.
    pause
    exit /b 1
)

echo [1/4] Building application...
echo.
cd /d "%~dp0..\Octolink"
dotnet restore
if %errorLevel% neq 0 (
    echo ERROR: dotnet restore failed!
    pause
    exit /b 1
)

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "..\publish"
if %errorLevel% neq 0 (
    echo ERROR: dotnet publish failed!
    pause
    exit /b 1
)
echo [OK] Application built successfully
echo.

:: Download ViGEmBus if not present
cd /d "%~dp0"
echo [2/4] Checking ViGEmBus driver...
if not exist "dependencies\ViGEmBus_1.22.0_x64_x86_arm64.exe" (
    echo Downloading ViGEmBus driver...
    powershell -Command "& {[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe' -OutFile 'dependencies\ViGEmBus_1.22.0_x64_x86_arm64.exe'}"
    if %errorLevel% neq 0 (
        echo ERROR: Failed to download ViGEmBus!
        echo Please download manually from:
        echo https://github.com/nefarius/ViGEmBus/releases
        echo.
        echo Save as: dependencies\ViGEmBus_1.22.0_x64_x86_arm64.exe
        pause
        exit /b 1
    )
    echo [OK] ViGEmBus downloaded
) else (
    echo [OK] ViGEmBus already present
)
echo.

:: Check for icon file
echo [3/4] Checking icon file...
if not exist "..\Octolink\icon.ico" (
    echo WARNING: icon.ico not found in Octolink folder
    echo The installer will use a default icon.
    echo To add a custom icon, place icon.ico in the Octolink folder.
    echo.
    :: Create a placeholder entry in the ISS file or skip
)
echo.

:: Build installer
echo [4/4] Building installer with Inno Setup...
"%INNO_PATH%" "OctolinkSetup.iss"
if %errorLevel% neq 0 (
    echo ERROR: Inno Setup compilation failed!
    pause
    exit /b 1
)

echo.
echo ============================================
echo   BUILD SUCCESSFUL!
echo ============================================
echo.
echo Installer created at:
echo   installer_output\OctolinkSetup.exe
echo.
echo The installer includes:
echo   - Octolink application (self-contained)
echo   - ViGEmBus driver installer
echo   - Automatic firewall configuration
echo   - Start menu and desktop shortcuts
echo.
pause
