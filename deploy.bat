@echo off
setlocal

set "PROTOCOL_PROJECT=.\Protocol\Protocol.csproj"
set "PROTOCOL_DLL=.\Protocol\bin\Debug\netstandard2.1\Protocol.dll"
set "UNITY_DLL=.\Assets\Plugins\Protocol.dll"

set "SERVER_PROJECT=.\Server\Server.csproj"
set "PUBLISH=.\publish"

set "SSH_HOST=wcc-server"
set "SERVER_PATH=/opt/witch-cant-cook"
set "SERVICE=witch-cant-cook"

echo.
echo ========================================
echo Witch-Cant-Cook Deployment
echo ========================================
echo.

echo [1/5] Building Protocol...
dotnet build "%PROTOCOL_PROJECT%" -c Debug

if errorlevel 1 (
echo.
echo [FAIL] Protocol build failed.
pause
exit /b 1
)

echo.
echo [2/5] Updating Unity Protocol.dll...
copy /Y "%PROTOCOL_DLL%" "%UNITY_DLL%"

if errorlevel 1 (
echo.
echo [FAIL] Failed to update Unity Protocol.dll.
pause
exit /b 1
)

echo.
echo [3/5] Publishing server...
dotnet publish "%SERVER_PROJECT%" -c Release -r linux-x64 --self-contained false -o "%PUBLISH%"

if errorlevel 1 (
echo.
echo [FAIL] Server publish failed.
pause
exit /b 1
)

echo.
echo [4/5] Uploading server to VPS...
scp -r "%PUBLISH%*" %SSH_HOST%:%SERVER_PATH%/

if errorlevel 1 (
echo.
echo [FAIL] Upload failed.
pause
exit /b 1
)

echo.
echo [5/5] Restarting server...
ssh %SSH_HOST% "sudo systemctl restart %SERVICE%"

if errorlevel 1 (
echo.
echo [FAIL] Failed to restart systemd service.
pause
exit /b 1
)

ssh %SSH_HOST% "sudo systemctl is-active --quiet %SERVICE%"

if errorlevel 1 (
echo.
echo [FAIL] Server is not running.
echo.
ssh %SSH_HOST% "sudo systemctl status %SERVICE% --no-pager"
pause
exit /b 1
)

rmdir /s /q "%PUBLISH%"

echo.
echo ========================================
echo Deployment successful!
echo ========================================
echo.
echo Protocol.dll : Updated
echo Server : Updated
echo Service : RUNNING
echo VPS : %SSH_HOST%
echo.
echo ========================================
echo.

pause
exit /b 0