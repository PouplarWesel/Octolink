@echo off
echo Building Octolink...
cd /d "%~dp0Octolink"

echo Restoring packages...
dotnet restore

echo Building Release...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "..\publish"

echo.
echo Build complete! Output in 'publish' folder.
echo.
pause
