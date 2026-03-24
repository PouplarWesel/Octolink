@echo off
echo Starting Octolink...
cd /d "%~dp0Octolink"

echo Building...
dotnet build -c Release

echo.
echo Starting application...
cd /d "%~dp0Octolink\bin\Release\net8.0-windows"
Octolink.exe
pause
