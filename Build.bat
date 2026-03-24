@echo off
echo Building Octolink...
echo Restoring packages...
dotnet restore "%~dp0Octolink.sln"

echo Building Release...
dotnet publish "%~dp0Octolink\Octolink.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "%~dp0publish"

echo.
echo Build complete! Output in 'publish' folder.
echo.
pause
