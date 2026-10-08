@echo off
setlocal

set "SSH_HOST=wcc-deploy"

:MENU
cls

echo.
echo ========================================
echo Witch-Cant-Cook Server
echo ========================================
echo.
echo 1. Restart Server
echo 2. View Server Logs
echo 3. View Live Logs
echo 4. Exit
echo.

set /p "CHOICE=Select: "

if "%CHOICE%"=="1" goto RESTART
if "%CHOICE%"=="2" goto LOGS
if "%CHOICE%"=="3" goto LIVE_LOGS
if "%CHOICE%"=="4" goto EXIT

echo.
echo Invalid selection.
pause
goto MENU

:RESTART
cls

echo.
echo ========================================
echo Restarting Server
echo ========================================
echo.

ssh %SSH_HOST% restart

if errorlevel 1 (
    echo.
    echo [FAIL] Server restart failed.
    echo.
    pause
    goto MENU
)

echo.
echo ========================================
echo Server restarted successfully!
echo ========================================
echo.

pause
goto MENU

:LOGS
cls

echo.
echo ========================================
echo Server Logs
echo ========================================
echo.

ssh %SSH_HOST% logs

echo.
echo ========================================
echo End of Logs
echo ========================================
echo.

pause
goto MENU

:LIVE_LOGS
cls

echo.
echo ========================================
echo Live Server Logs
echo ========================================
echo.
echo Press Ctrl+C to stop.
echo.

ssh %SSH_HOST% logs-follow

echo.
pause
goto MENU

:EXIT
exit /b 0