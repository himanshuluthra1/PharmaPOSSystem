@echo off
setlocal EnableExtensions EnableDelayedExpansion
title PharmaPOS — first-time shop install

REM ============================================================
REM  First-time install on a new shop PC (Windows 10/11 64-bit).
REM  Copy this .bat to the shop machine (USB / WhatsApp / email)
REM  and double-click it. Internet required.
REM
REM  After each new publish, either:
REM    - change SETUP_FILE below, or
REM    - upload installer\latest.txt to the VPS updates folder
REM      (one line: PharmaPOS-Setup-x.y.z.exe)
REM ============================================================

set "BASE_URL=http://bills.cloudpharma.site/bills/updates"
set "SETUP_FILE=PharmaPOS-Setup-1.3.5.exe"
set "DOWNLOAD_DIR=%TEMP%\PharmaPOS-Install"

echo.
echo  PharmaPOS shop install
echo  ----------------------
echo  Needs: Windows 10/11 64-bit, internet, admin password when prompted.
echo  Also needs SQL Server Express LocalDB (free). If PharmaPOS warns
echo  that LocalDB is missing after install, install LocalDB then reopen.
echo.

mkdir "%DOWNLOAD_DIR%" 2>nul

echo Checking for latest installer name...
curl.exe -fsSL "%BASE_URL%/latest.txt" -o "%DOWNLOAD_DIR%\latest.txt" 2>nul
if exist "%DOWNLOAD_DIR%\latest.txt" (
  set /p SETUP_FILE=<"%DOWNLOAD_DIR%\latest.txt"
  REM trim spaces / CR
  for /f "tokens=* delims= " %%A in ("!SETUP_FILE!") do set "SETUP_FILE=%%A"
  echo Using server latest: !SETUP_FILE!
) else (
  echo latest.txt not found — using !SETUP_FILE!
)

set "LOCAL_SETUP=%DOWNLOAD_DIR%\!SETUP_FILE!"

echo.
echo Downloading !SETUP_FILE! ...
echo URL: %BASE_URL%/!SETUP_FILE!
echo.

curl.exe -fL --progress-bar -o "!LOCAL_SETUP!" "%BASE_URL%/!SETUP_FILE!"
if errorlevel 1 (
  echo.
  echo Download failed. Check internet, or open this URL in a browser:
  echo   %BASE_URL%/!SETUP_FILE!
  echo.
  pause
  exit /b 1
)

for %%A in ("!LOCAL_SETUP!") do set "SIZE=%%~zA"
if "!SIZE!"=="" set "SIZE=0"
if !SIZE! LSS 1000000 (
  echo.
  echo Downloaded file is too small ^(!SIZE! bytes^). URL may be wrong.
  echo.
  pause
  exit /b 1
)

echo.
echo Download OK ^(!SIZE! bytes^).
echo Starting installer — accept the Windows admin / UAC prompt...
echo.

start "" /wait "!LOCAL_SETUP!"
set "RC=!ERRORLEVEL!"

echo.
if "!RC!"=="0" (
  echo Installer finished. Launch PharmaPOS from the Start menu.
  echo First login: admin / Admin@123  ^(you will be asked to change it^).
  echo Enter the store code your vendor gave you when prompted.
) else (
  echo Installer exited with code !RC!.
  echo If it did not open, right-click the downloaded file and Run as administrator:
  echo   !LOCAL_SETUP!
)

echo.
pause
exit /b 0
