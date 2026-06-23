@echo off
setlocal EnableExtensions

cd /d "%~dp0"

set "ROOT=%~dp0"
set "APP=%ROOT%src\VoiceTranslator.App\bin\Release\net10.0-windows\VoiceTranslator.App.dll"
set "WORKER_PY=%ROOT%worker\.venv\Scripts\python.exe"
set "DOTNET=%ROOT%.dotnet\dotnet.exe"
set "LOG_DIR=%ROOT%artifacts\logs"
set "LOG_FILE=%LOG_DIR%\voice-translator-launch.log"

if not exist "%LOG_DIR%" mkdir "%LOG_DIR%"

if not exist "%APP%" (
    echo Voice Translator executable was not found.
    echo Building local Release executable without NuGet restore...
    if not exist "%DOTNET%" (
        echo Missing workspace .NET SDK: "%DOTNET%"
        pause
        exit /b 1
    )
    set "NUGET_PACKAGES=%USERPROFILE%\.nuget\packages"
    "%DOTNET%" publish "%ROOT%src\VoiceTranslator.App\VoiceTranslator.App.csproj" -c Release -r win-x64 --no-self-contained --no-restore -p:NuGetAudit=false
    if errorlevel 1 (
        echo Build failed. See the output above.
        pause
        exit /b 1
    )
)

if not exist "%WORKER_PY%" (
    echo Python worker environment was not found.
    echo Preparing worker environment. This can take several minutes on first run...
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%ROOT%worker\bootstrap.ps1"
    if errorlevel 1 (
        echo Worker bootstrap failed. See the output above.
        pause
        exit /b 1
    )
)

if not exist "%USERPROFILE%\.voice-translator\models\verified-models.json" (
    echo Verified models were not found.
    echo Run model download first, or ask Codex to download them.
    pause
    exit /b 1
)

echo Starting Voice Translator...
echo If the app closes with an error, this window will show the exit code.
echo Log: "%LOG_FILE%"
echo.

"%DOTNET%" "%APP%" > "%LOG_FILE%" 2>&1
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo.
    echo Voice Translator closed with exit code %EXIT_CODE%.
    echo Log file: "%LOG_FILE%"
    pause
    exit /b %EXIT_CODE%
)

exit /b 0
