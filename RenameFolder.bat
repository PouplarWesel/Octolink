@echo off
echo Renaming VirtualControllerServer folder to Octolink...
cd /d "%~dp0"
rename "VirtualControllerServer" "Octolink"
if %errorLevel% equ 0 (
    echo [OK] Folder renamed successfully!
    del "%~f0"
) else (
    echo [ERROR] Could not rename folder. Make sure VS Code is closed.
)
pause
