@echo off
setlocal EnableExtensions DisableDelayedExpansion

color 0A
echo (c) 2026 Meta Games LLC. All rights reserved.
echo.

set "SCRIPT_DIR=%~dp0"
set "BIN_DIR=%SCRIPT_DIR%..\bin"
set "CLIENT_EXE=%BIN_DIR%\client\client.console.exe"
set "TOKEN_FILE=%SCRIPT_DIR%join_token.txt"

if not exist "%BIN_DIR%\" call :fail "Failed to locate the bin directory." 1
if not exist "%CLIENT_EXE%" call :fail "client.console.exe was not found." 1
if not exist "%TOKEN_FILE%" call :fail "join_token.txt was not found. Expected: %TOKEN_FILE%" 1

set "join_token="
for /f "usebackq tokens=* delims=" %%A in ("%TOKEN_FILE%") do (
    if not defined join_token set "join_token=%%A"
)

if not defined join_token call :fail "join_token.txt is empty." 1

echo Starting BrickVerse Test Client...
echo.

pushd "%BIN_DIR%" >nul || call :fail "Failed to enter bin directory." 1

"%CLIENT_EXE%" ^
    -network=client ^
    -token=%join_token% ^
    %*

set "exit_code=%ERRORLEVEL%"
popd >nul

if not "%exit_code%"=="0" (
    color 0C
    echo.
    echo BrickVerse Test Client exited with code %exit_code%.
) else (
    color 0A
    echo.
    echo BrickVerse Test Client exited successfully.
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