@echo off
echo Building Virtual Controller Server...
cd /d "%~dp0VirtualControllerServer"

echo Restoring packages...
dotnet restore

echo Building Release...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "..\publish"

echo.
echo Build complete! Output in 'publish' folder.
echo.
pause
