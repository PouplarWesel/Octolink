@echo off
setlocal enabledelayedexpansion

echo ============================================
echo   Octolink Portable Package Builder
echo ============================================
echo.

:: Change to script directory
cd /d "%~dp0"

:: Create output directory
if not exist "..\installer_output" mkdir "..\installer_output"
if not exist "temp_package" mkdir "temp_package"

echo [1/4] Building application...
cd /d "%~dp0..\Octolink"
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "..\publish"
if %errorLevel% neq 0 (
    echo ERROR: Build failed!
    pause
    exit /b 1
)
echo [OK] Application built
echo.

cd /d "%~dp0"

echo [2/4] Copying files...
:: Copy published files
xcopy /E /Y "..\publish\*" "temp_package\" >nul
echo [OK] Application files copied
echo.

echo [3/4] Creating setup script...
:: Create the install script that will be included
(
echo @echo off
echo setlocal
echo.
echo echo ============================================
echo echo   Octolink Installation
echo echo ============================================
echo echo.
echo.
echo :: Check for admin
echo net session ^>nul 2^>^&1
echo if %%errorLevel%% neq 0 ^(
echo     echo This installer requires Administrator privileges.
echo     echo Please right-click and select "Run as administrator"
echo     pause
echo     exit /b 1
echo ^)
echo.
echo set "INSTALL_DIR=%%ProgramFiles%%\Octolink"
echo.
echo echo Installing to: %%INSTALL_DIR%%
echo echo.
echo.
echo :: Create install directory
echo if not exist "%%INSTALL_DIR%%" mkdir "%%INSTALL_DIR%%"
echo.
echo :: Copy files
echo echo Copying files...
echo xcopy /E /Y "%%~dp0*" "%%INSTALL_DIR%%\" ^>nul
echo del "%%INSTALL_DIR%%\Install.bat" ^>nul 2^>^&1
echo del "%%INSTALL_DIR%%\InstallViGEmBus.bat" ^>nul 2^>^&1
echo echo [OK] Files copied
echo.
echo :: Configure firewall
echo echo Configuring firewall...
echo netsh advfirewall firewall add rule name="Octolink HTTP" dir=in action=allow protocol=TCP localport=5000 ^>nul 2^>^&1
echo netsh advfirewall firewall add rule name="Octolink WebSocket" dir=in action=allow protocol=TCP localport=5001 ^>nul 2^>^&1
echo echo [OK] Firewall configured
echo.
echo :: Configure URL ACLs
echo echo Configuring network permissions...
echo netsh http add urlacl url=http://+:5000/ user=Everyone ^>nul 2^>^&1
echo netsh http add urlacl url=http://+:5001/ user=Everyone ^>nul 2^>^&1
echo echo [OK] Network permissions configured
echo.
echo :: Create Start Menu shortcut
echo echo Creating shortcuts...
echo set "STARTMENU=%%ProgramData%%\Microsoft\Windows\Start Menu\Programs"
echo powershell -Command "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('%%STARTMENU%%\Octolink.lnk'); $s.TargetPath = '%%INSTALL_DIR%%\Octolink.exe'; $s.WorkingDirectory = '%%INSTALL_DIR%%'; $s.Save()"
echo echo [OK] Start Menu shortcut created
echo.
echo echo.
echo echo ============================================
echo echo   Installation Complete!
echo echo ============================================
echo echo.
echo echo IMPORTANT: You must install the ViGEmBus driver!
echo echo.
echo echo Run "InstallViGEmBus.bat" or download from:
echo echo https://github.com/nefarius/ViGEmBus/releases
echo echo.
echo echo After installing ViGEmBus, restart your computer.
echo echo.
echo pause
) > "temp_package\Install.bat"

:: Create ViGEmBus download script
(
echo @echo off
echo echo Downloading ViGEmBus driver...
echo echo.
echo powershell -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe' -OutFile '%%TEMP%%\ViGEmBus_Setup.exe'"
echo if %%errorLevel%% neq 0 ^(
echo     echo Download failed! Please download manually from:
echo     echo https://github.com/nefarius/ViGEmBus/releases
echo     pause
echo     exit /b 1
echo ^)
echo echo.
echo echo Running ViGEmBus installer...
echo "%%TEMP%%\ViGEmBus_Setup.exe"
echo echo.
echo echo After installation, please restart your computer.
echo pause
) > "temp_package\InstallViGEmBus.bat"

echo [OK] Setup scripts created
echo.

echo [4/4] Creating ZIP package...
:: Use PowerShell to create ZIP
powershell -Command "Compress-Archive -Path 'temp_package\*' -DestinationPath '..\installer_output\Octolink_Portable.zip' -Force"
if %errorLevel% neq 0 (
    echo ERROR: Failed to create ZIP!
    pause
    exit /b 1
)

:: Cleanup
rmdir /S /Q "temp_package" 2>nul

echo [OK] Package created
echo.
echo ============================================
echo   BUILD SUCCESSFUL!
echo ============================================
echo.
echo Package created at:
echo   installer_output\Octolink_Portable.zip
echo.
echo Instructions for users:
echo   1. Extract the ZIP
echo   2. Run Install.bat as Administrator
echo   3. Run InstallViGEmBus.bat to install the driver
echo   4. Restart the computer
echo   5. Launch Octolink from Start Menu
echo.
pause
