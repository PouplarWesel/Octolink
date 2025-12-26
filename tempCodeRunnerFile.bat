@echo off
echo Starting Virtual Controller Server...
cd /d "%~dp0VirtualControllerServer"

echo Building...
dotnet build -c Release

echo.
echo Starting application...
cd /d "%~dp0VirtualControllerServer\bin\Release\net8.0-windows"
VirtualControllerServer.exe
pause
