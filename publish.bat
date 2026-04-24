@echo off
setlocal

set PUBLISH_DIR=src\TeamTalkStreamer.App\bin\Release\net10.0-windows\win-x64\publish
set NATIVE_DLL=libs\teamtalk\TeamTalk5.dll

echo Publishing TeamTalkStreamer as single executable...
dotnet publish src\TeamTalkStreamer.App\TeamTalkStreamer.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
if %ERRORLEVEL% NEQ 0 (
    echo Build failed.
    pause
    exit /b 1
)

REM Safety-net copy of the native TeamTalk DLL into the publish folder.
REM The csproj already declares CopyToPublishDirectory for this file,
REM but we do one more explicit copy here so any MSBuild quirk around
REM single-file publishing and transitive None items can't silently
REM leave the exe without its native dependency.
echo.
echo Verifying TeamTalk5.dll in publish folder...
if not exist "%NATIVE_DLL%" (
    echo [WARNING] %NATIVE_DLL% not found in libs folder.
    echo The TeamTalk SDK has not been vendored; see README for setup.
) else (
    copy /y "%NATIVE_DLL%" "%PUBLISH_DIR%\" >nul
    if exist "%PUBLISH_DIR%\TeamTalk5.dll" (
        echo [OK] TeamTalk5.dll placed alongside the exe.
    ) else (
        echo [ERROR] Failed to copy TeamTalk5.dll into %PUBLISH_DIR%.
        pause
        exit /b 1
    )
)

echo.
echo Build complete: %PUBLISH_DIR%\TeamTalkStreamer.exe
echo.
echo Note: TeamTalk5.dll and guide.html are placed in the publish folder
echo alongside the exe — they are not bundled inside the single file
echo because they are native / content assets that must be resolvable
echo on disk at runtime.
pause
