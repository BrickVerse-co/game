@echo off
setlocal

color 0A

echo (c) 2026 Meta Games LLC. All rights reserved.
echo.

REM Change to the bin directory relative to this script.
cd /d "%~dp0..\bin" || (
    color 0C
    echo ERROR: Failed to locate the bin directory.
    pause
    exit /b 1
)

REM Verify the client executable exists.
if not exist "client/client.console.exe" (
    color 0C
    echo ERROR: client.console.exe was not found.
    pause
    exit /b 1
)

REM Verify the host token file exists.
if not exist "..\bin_scripts\join_token.txt" (
    color 0C
    echo ERROR: join_token.txt was not found.
    echo Expected: ..\bin_scripts\join_token.txt
    pause
    exit /b 1
)

REM Read the join token.
set /p join_token=<"..\bin_scripts\join_token.txt"

if "%join_token%"=="" (
    color 0C
    echo ERROR: join_token.txt is empty.
    pause
    exit /b 1
)

echo Starting BrickVerse Test Client...
echo.

client/client.console.exe ^
    -network=client ^
    -token=%join_token% ^

pause