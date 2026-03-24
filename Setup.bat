@echo off
echo ============================================
echo   Octolink - First Time Setup
echo ============================================
echo.
echo This script needs to run as Administrator (one time only)
echo to allow the server to accept connections from your phone.
echo.

:: Check for admin privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo ERROR: Please run this script as Administrator!
    echo.
    echo Right-click on Setup.bat and select "Run as administrator"
    echo.
    pause
    exit /b 1
)

echo Adding URL reservations for ports 5000 and 5001...
echo.

:: Add URL ACL for HTTP server (port 5000)
netsh http add urlacl url=http://+:5000/ user=Everyone >nul 2>&1
if %errorLevel% equ 0 (
    echo [OK] Port 5000 registered successfully
) else (
    echo [INFO] Port 5000 may already be registered
)

:: Add URL ACL for WebSocket server (port 5001)
netsh http add urlacl url=http://+:5001/ user=Everyone >nul 2>&1
if %errorLevel% equ 0 (
    echo [OK] Port 5001 registered successfully
) else (
    echo [INFO] Port 5001 may already be registered
)

echo.
echo Adding firewall rules...

:: Add firewall rules
netsh advfirewall firewall add rule name="Octolink HTTP" dir=in action=allow protocol=tcp localport=5000 >nul 2>&1
netsh advfirewall firewall add rule name="Octolink WebSocket" dir=in action=allow protocol=tcp localport=5001 >nul 2>&1
netsh advfirewall firewall add rule name="Octolink ngrok" dir=in action=allow program="%~dp0Octolink\bin\Release\net8.0-windows\Octolink.exe" enable=yes >nul 2>&1

echo [OK] Firewall rules added
echo.
echo ============================================
echo   Setup Complete!
echo ============================================
echo.
echo You can now run Octolink.exe normally
echo (no admin required after this setup).
echo.
pause
