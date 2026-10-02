@echo off
setlocal

set "PROTOCOL_PROJECT=.\Protocol\Protocol.csproj"
set "PROTOCOL_DLL=.\Protocol\bin\Debug\netstandard2.1\Protocol.dll"
set "UNITY_DLL=.\Assets\Plugins\Protocol.dll"

set "SERVER_PROJECT=.\Server\Server.csproj"
set "PUBLISH=.\publish"

set "SSH_HOST=wcc-server"
set "SERVER_PATH=/opt/witch-cant-cook"
set "TEMP_SERVER_PATH=/tmp/wcc-deploy"
set "SERVICE=witch-cant-cook"

echo.
echo ========================================
echo Witch-Cant-Cook Deployment
echo ========================================
echo.

echo [1/7] Building Protocol...
dotnet build "%PROTOCOL_PROJECT%" -c Debug

if errorlevel 1 (
    echo.
    echo [FAIL] Protocol build failed.
    pause
    exit /b 1
)

echo.
echo [2/7] Updating Unity Protocol.dll...
copy /Y "%PROTOCOL_DLL%" "%UNITY_DLL%"

if errorlevel 1 (
    echo.
    echo [FAIL] Failed to update Unity Protocol.dll.
    pause
    exit /b 1
)

echo.
echo [3/7] Publishing server...

if exist "%PUBLISH%" (
    rmdir /s /q "%PUBLISH%"
)

dotnet publish "%SERVER_PROJECT%" -c Release -r linux-x64 --self-contained false -o "%PUBLISH%"

if errorlevel 1 (
    echo.
    echo [FAIL] Server publish failed.
    pause
    exit /b 1
)

echo.
echo [4/7] Verifying local publish...

if not exist "%PUBLISH%\Server.dll" (
    echo.
    echo [FAIL] Server.dll not found in publish directory.
    pause
    exit /b 1
)

if not exist "%PUBLISH%\Protocol.dll" (
    echo.
    echo [FAIL] Protocol.dll not found in publish directory.
    pause
    exit /b 1
)

for /f "tokens=*" %%H in ('certutil -hashfile "%PUBLISH%\Server.dll" SHA256 ^| findstr /r /v "^SHA256 CertUtil"') do set "LOCAL_SERVER_HASH=%%H"
for /f "tokens=*" %%H in ('certutil -hashfile "%PUBLISH%\Protocol.dll" SHA256 ^| findstr /r /v "^SHA256 CertUtil"') do set "LOCAL_PROTOCOL_HASH=%%H"

echo Local Server.dll:
echo %LOCAL_SERVER_HASH%

echo.
echo Local Protocol.dll:
echo %LOCAL_PROTOCOL_HASH%

echo.
echo [5/7] Uploading server to VPS...

ssh %SSH_HOST% "rm -rf %TEMP_SERVER_PATH% && mkdir -p %TEMP_SERVER_PATH%"

if errorlevel 1 (
    echo.
    echo [FAIL] Failed to prepare server temporary directory.
    pause
    exit /b 1
)

scp -r "%PUBLISH%\*" %SSH_HOST%:%TEMP_SERVER_PATH%/

if errorlevel 1 (
    echo.
    echo [FAIL] Upload failed.
    ssh %SSH_HOST% "rm -rf %TEMP_SERVER_PATH%"
    pause
    exit /b 1
)

echo.
echo [6/7] Applying files and verifying...

ssh %SSH_HOST% "cp -f %TEMP_SERVER_PATH%/* %SERVER_PATH%/"

if errorlevel 1 (
    echo.
    echo [FAIL] Failed to apply files to server.
    ssh %SSH_HOST% "rm -rf %TEMP_SERVER_PATH%"
    pause
    exit /b 1
)

for /f "tokens=*" %%H in ('ssh %SSH_HOST% "sha256sum %SERVER_PATH%/Server.dll"') do set "REMOTE_SERVER_RESULT=%%H"
for /f "tokens=*" %%H in ('ssh %SSH_HOST% "sha256sum %SERVER_PATH%/Protocol.dll"') do set "REMOTE_PROTOCOL_RESULT=%%H"

for /f "tokens=1" %%H in ("%REMOTE_SERVER_RESULT%") do set "REMOTE_SERVER_HASH=%%H"
for /f "tokens=1" %%H in ("%REMOTE_PROTOCOL_RESULT%") do set "REMOTE_PROTOCOL_HASH=%%H"

echo.
echo Remote Server.dll:
echo %REMOTE_SERVER_HASH%

echo.
echo Remote Protocol.dll:
echo %REMOTE_PROTOCOL_HASH%

echo.

if /I not "%LOCAL_SERVER_HASH%"=="%REMOTE_SERVER_HASH%" (
    echo [FAIL] Server.dll hash mismatch.
    ssh %SSH_HOST% "rm -rf %TEMP_SERVER_PATH%"
    pause
    exit /b 1
)

if /I not "%LOCAL_PROTOCOL_HASH%"=="%REMOTE_PROTOCOL_HASH%" (
    echo [FAIL] Protocol.dll hash mismatch.
    ssh %SSH_HOST% "rm -rf %TEMP_SERVER_PATH%"
    pause
    exit /b 1
)

echo [OK] Server.dll hash matches.
echo [OK] Protocol.dll hash matches.

ssh %SSH_HOST% "rm -rf %TEMP_SERVER_PATH%"

echo.
echo [7/7] Restarting server...

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

echo.
echo ========================================
echo Deployment successful!
echo ========================================
echo.
echo Server.dll   : Updated and verified
echo Protocol.dll : Updated and verified
echo Service      : RUNNING
echo VPS          : %SSH_HOST%
echo.
echo ========================================
echo.

rmdir /s /q "%PUBLISH%"

pause
exit /b 0
