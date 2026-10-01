@echo off
setlocal

set "PROJECT=.\Server\Server.csproj"
set "PUBLISH=.\publish"
set "SSH_HOST=wcc-server"
set "SERVER_PATH=/opt/witch-cant-cook"
set "SERVICE=witch-cant-cook"

echo.
echo ========================================
echo   Witch-Cant-Cook Server Deployment
echo ========================================
echo.

echo [1/4] Publishing server...
dotnet publish "%PROJECT%" -c Release -r linux-x64 --self-contained false -o "%PUBLISH%"

if errorlevel 1 (
    echo.
    echo [FAIL] Publish failed.
    pause
    exit /b 1
)

echo.
echo [2/4] Uploading server to VPS...
scp -r "%PUBLISH%\*" %SSH_HOST%:%SERVER_PATH%/

if errorlevel 1 (
    echo.
    echo [FAIL] Upload failed.
    pause
    exit /b 1
)

echo.
echo [3/4] Restarting systemd service...
ssh %SSH_HOST% "sudo systemctl restart %SERVICE%"

if errorlevel 1 (
    echo.
    echo [FAIL] Failed to restart systemd service.
    pause
    exit /b 1
)

echo.
echo [4/4] Checking server status...
ssh %SSH_HOST% "sudo systemctl is-active --quiet %SERVICE%"

if errorlevel 1 (
    echo.
    echo [FAIL] Server is not running.
    echo.
    ssh %SSH_HOST% "sudo systemctl status %SERVICE% --no-pager"
    pause
    exit /b 1
)

echo.
echo ========================================
echo   Deployment successful!
echo ========================================
echo.
echo Server: %SSH_HOST%:4040
echo Service: %SERVICE%
echo Status: RUNNING
echo.

rmdir /s /q "%PUBLISH%"

pause
exit /b 0
