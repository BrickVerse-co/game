@echo off
setlocal EnableExtensions DisableDelayedExpansion

color 0A
echo (c) 2026 Meta Games LLC. All rights reserved.
echo.

set "SCRIPT_DIR=%~dp0"
set "BIN_DIR=%SCRIPT_DIR%..\bin"
set "SERVER_EXE=%BIN_DIR%\server\server.console.exe"
set "TOKEN_FILE=%SCRIPT_DIR%host_token.txt"

if not exist "%BIN_DIR%\" call :fail "Failed to locate the bin directory." 1
if not exist "%SERVER_EXE%" call :fail "server.console.exe was not found." 1
if not exist "%TOKEN_FILE%" call :fail "host_token.txt was not found. Expected: %TOKEN_FILE%" 1

set "host_token="
for /f "usebackq tokens=* delims=" %%A in ("%TOKEN_FILE%") do (
    if not defined host_token set "host_token=%%A"
)

if not defined host_token call :fail "host_token.txt is empty." 1

set "SERVER_PORT=%BRICKVERSE_TEST_PORT%"
if not defined SERVER_PORT set "SERVER_PORT=5555"

set "SERVER_WORLD=%BRICKVERSE_TEST_WORLD%"
if not defined SERVER_WORLD set "SERVER_WORLD=baseplate.bvxw"

echo Starting BrickVerse Test Server...
echo Port: %SERVER_PORT%
echo World: %SERVER_WORLD%
echo.

pushd "%BIN_DIR%" >nul || call :fail "Failed to enter bin directory." 1

"%SERVER_EXE%" ^
    -network=server ^
    -token=%host_token% ^
    -port=%SERVER_PORT% ^
    -world="%SERVER_WORLD%" ^
    %*

set "exit_code=%ERRORLEVEL%"
popd >nul

if not "%exit_code%"=="0" (
    color 0C
    echo.
    echo BrickVerse Test Server exited with code %exit_code%.
) else (
    color 0A
    echo.
    echo BrickVerse Test Server exited successfully.
)

pause
color 07
exit /b %exit_code%

:fail
color 0C
echo ERROR: %~1
pause
color 07
exit /b %~2