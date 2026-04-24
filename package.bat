@echo off
REM TeamTalkStreamer Package Script
REM Zips the published folder (TeamTalkStreamer.exe + TeamTalk5.dll + any
REM other sidecar runtime files) together with guide.html into
REM TeamTalkStreamer.zip at the project root.

echo ============================================================
echo TeamTalk Streamer - Packaging Script
echo ============================================================
echo.

cd /d "%~dp0"

set PUBLISH_DIR=src\TeamTalkStreamer.App\bin\Release\net10.0-windows\win-x64\publish

REM Check required files
echo Checking required files...

if not exist "%PUBLISH_DIR%\TeamTalkStreamer.exe" (
    echo [ERROR] TeamTalkStreamer.exe not found in publish folder!
    echo.
    echo Run publish.bat first to compile the executable.
    pause
    exit /b 1
)

if not exist guide.html (
    echo [ERROR] guide.html not found in project root!
    echo.
    echo The user guide is required for distribution.
    pause
    exit /b 1
)

echo [OK] %PUBLISH_DIR%\TeamTalkStreamer.exe found
echo [OK] guide.html found
echo.

REM Delete existing zip if present
if exist TeamTalkStreamer.zip (
    echo Removing existing TeamTalkStreamer.zip...
    del TeamTalkStreamer.zip
    if exist TeamTalkStreamer.zip (
        echo [ERROR] Failed to delete existing TeamTalkStreamer.zip
        echo.
        echo The file may be open in another program.
        pause
        exit /b 1
    )
    echo [OK] Removed old TeamTalkStreamer.zip
    echo.
)

REM Create staging directory for a flat zip layout
echo Creating TeamTalkStreamer.zip...
if exist _package rmdir /s /q _package
mkdir _package

REM Copy the full publish folder into staging (exe + TeamTalk5.dll
REM + any other runtime sidecar files that the publish step produced).
xcopy /s /y "%PUBLISH_DIR%\*" _package\ >nul

REM Strip debug symbol files. The managed-assembly .pdb files are not
REM needed by end users, nearly double the zip size, and leaking them
REM just exposes internal symbol names. We strip recursively in case
REM a future publish layout introduces nested folders.
del /s /q _package\*.pdb >nul 2>&1

REM Refresh guide.html from the project root so the packaged copy is
REM always current, even if the published folder had a stale one.
copy /y guide.html _package\ >nul

REM Create zip using PowerShell Compress-Archive
powershell -NoProfile -Command "Compress-Archive -Path '_package\*' -DestinationPath 'TeamTalkStreamer.zip' -Force"
set ZIP_ERROR=%errorlevel%

REM Clean up staging directory
rmdir /s /q _package

if %ZIP_ERROR% neq 0 (
    echo.
    echo [ERROR] Failed to create TeamTalkStreamer.zip
    echo.
    echo Ensure PowerShell is available on this system.
    pause
    exit /b 1
)

if not exist TeamTalkStreamer.zip (
    echo.
    echo [ERROR] TeamTalkStreamer.zip was not created
    pause
    exit /b 1
)

echo.
echo ============================================================
echo [SUCCESS] TeamTalkStreamer.zip created!
echo ============================================================
echo.
echo Contents: everything from the publish folder (excluding .pdb symbols) + guide.html
echo.
for %%A in (TeamTalkStreamer.zip) do echo Size: %%~zA bytes
echo.
pause
