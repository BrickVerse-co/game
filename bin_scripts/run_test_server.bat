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

REM Verify the server executable exists.
if not exist "server.console.exe" (
    color 0C
    echo ERROR: server.console.exe was not found.
    pause
    exit /b 1
)

REM Verify the host token file exists.
if not exist "..\bin_scripts\host_token.txt" (
    color 0C
    echo ERROR: host_token.txt was not found.
    echo Expected: ..\bin_scripts\host_token.txt
    pause
    exit /b 1
)

REM Read the host token.
set /p host_token=<"..\bin_scripts\host_token.txt"

if "%host_token%"=="" (
    color 0C
    echo ERROR: host_token.txt is empty.
    pause
    exit /b 1
)

REM Verify the world file exists.
if not exist "samples\worlds\baseplate.poly" (
    color 0C
    echo ERROR: World file not found.
    echo Expected: samples\worlds\baseplate.poly
    pause
    exit /b 1
)

echo Starting BrickVerse Test Server...
echo.

server.console.exe ^
    -network=server ^
    -token=%host_token% ^
    -world="res://samples/worlds/baseplate.poly"

pause