@echo off
chcp 65001 >nul 2>&1
setlocal enabledelayedexpansion

echo ========================================
echo   VmOps.NET - Release Build Script
echo ========================================
echo.

set "SCRIPT_DIR=%~dp0"
set "PROJECT_DIR=%SCRIPT_DIR%VmOps"
set "OUTPUT_DIR=%SCRIPT_DIR%"

set "APP_NAME=VmOps.NET"
set "EXE_NAME=%APP_NAME%.exe"
set "CSPROJ_FILE=%PROJECT_DIR%\VmOps.csproj"

for /f "delims=" %%a in ('powershell -NoProfile -Command "(Get-Content '%CSPROJ_FILE%' -Raw) -match '<Version>([^<]*)</Version>' | Out-Null; if ($Matches.Count -gt 1) { $Matches[1] } else { 'VERSION_NOT_FOUND' }"') do (
    set "OLD_VERSION=%%a"
)

if "%OLD_VERSION%"=="" (
    echo [ERROR] Cannot read version from csproj!
    echo         Check ^<Version^> tag in %CSPROJ_FILE%
    pause
    exit /b 1
)
if "%OLD_VERSION%"=="VERSION_NOT_FOUND" (
    echo [ERROR] ^<Version^> tag is empty or missing!
    echo         Please fix it manually and retry
    pause
    exit /b 1
)

echo [1/6] Version handling...
echo       Current version: v%OLD_VERSION%
echo.
echo       Choose:
echo         [1] Bump revision (v%OLD_VERSION% ^> v?  default)
echo         [2] Keep version (test build only)
echo.
set /p "VER_CHOICE=Your choice [Enter=1]: "
if "%VER_CHOICE%"=="" set "VER_CHOICE=1"

if "%VER_CHOICE%"=="2" (
    set "NEW_VERSION=%OLD_VERSION%"
    set "SKIP_BACKUP=yes"
    echo.
    echo       Keep version: v%NEW_VERSION% ^(skip version file update^)
) else (
    for /f "tokens=1-4 delims=." %%a in ("%OLD_VERSION%") do (
        set "V1=%%a"
        set "V2=%%b"
        set "V3=%%c"
        set "V4=%%d"
    )
    set /a V4+=1
    if !V4! geq 10 (
        set /a "V3+=V4/10"
        set /a "V4=V4%%10"
    )
    if !V3! geq 10 (
        set /a "V2+=V3/10"
        set /a "V3=V3%%10"
    )
    if !V2! geq 10 (
        set /a "V1+=V2/10"
        set /a "V2=V2%%10"
    )
    set "NEW_VERSION=!V1!.!V2!.!V3!.!V4!"

    powershell -NoProfile -ExecutionPolicy Bypass -Command "$OutputEncoding = [System.Text.Encoding]::UTF8; [Console]::OutputEncoding = [System.Text.Encoding]::UTF8; & '%PROJECT_DIR%\update_version.ps1' -ProjectRoot '%PROJECT_DIR%' -NewVersion '!NEW_VERSION!'"

    echo.
    echo [Version] %OLD_VERSION%  -^>  !NEW_VERSION!  OK
)
echo       Build version: v%NEW_VERSION%
echo.

echo [2/6] Building UpdateHelper...
echo       UpdateHelper\VmOps.UpdateHelper.csproj
call dotnet build "%PROJECT_DIR%\UpdateHelper\VmOps.UpdateHelper.csproj" --configuration Release --runtime win-x64
if %errorlevel% neq 0 (
    echo.
    echo UpdateHelper build FAILED!
    pause
    exit /b 1
)
copy /Y "%PROJECT_DIR%\UpdateHelper\bin\Release\net48\win-x64\update_helper.exe" "%PROJECT_DIR%\Resources\update_helper.exe" >nul
echo       UpdateHelper build done ^(embedded to Resources^)
echo.

echo [3/6] Building Release...
cd /d "%PROJECT_DIR%"

call dotnet publish --configuration Release --framework net48 --runtime win-x64 -p:SelfContained=false -p:DebugType=none

if %errorlevel% neq 0 (
    echo.
    echo Build FAILED!
    pause
    exit /b 1
)

echo       Build OK
echo.

echo [4/6] Locating output...
set "EXE_PATH=%PROJECT_DIR%\bin\Release\net48\win-x64\publish\VmOps.NET.exe"

if not exist "%EXE_PATH%" (
    for /r "%PROJECT_DIR%\bin\Release" %%f in (VmOps.NET.exe) do (
        set "EXE_PATH=%%f"
        goto :found_exe
    )
    echo [ERROR] Output exe not found
    pause
    exit /b 1
)

:found_exe
echo       Found: %EXE_PATH%
echo.

echo [5/6] Copying to output dir...
copy /Y "%EXE_PATH%" "%OUTPUT_DIR%%EXE_NAME%" >nul
echo       Main exe: %EXE_NAME%

set "CONFIG_SRC=%PROJECT_DIR%\bin\Release\net48\win-x64\publish\VmOps.NET.exe.config"
if not exist "%CONFIG_SRC%" set "CONFIG_SRC=%PROJECT_DIR%\App.config"
if exist "%CONFIG_SRC%" (
    copy /Y "%CONFIG_SRC%" "%OUTPUT_DIR%%APP_NAME%.exe.config" >nul
    echo       Config: %APP_NAME%.exe.config
)

set "MANIFEST_SRC=%PROJECT_DIR%\app.manifest"
if not exist "%MANIFEST_SRC%" set "MANIFEST_SRC=%PROJECT_DIR%\bin\Release\net48\win-x64\publish\VmOps.NET.exe.manifest"
if exist "%MANIFEST_SRC%" (
    copy /Y "%MANIFEST_SRC%" "%OUTPUT_DIR%%APP_NAME%.exe.manifest" >nul
    echo       Manifest: %APP_NAME%.exe.manifest
)

for %%i in ("%OUTPUT_DIR%%EXE_NAME%") do set "EXE_SIZE=%%~zi"
set /a "EXE_SIZE_MB=EXE_SIZE/1048576"

echo       Output (single exe + config):
echo       %APP_NAME%.exe          ^(embeds update_helper.exe^)
echo       %APP_NAME%.exe.config
echo       %APP_NAME%.exe.manifest
echo       Size: !EXE_SIZE_MB! MB
echo.

echo [Cleanup] Removing temp files...
if exist "%PROJECT_DIR%\bin\Release" (
    rmdir /s /q "%PROJECT_DIR%\bin\Release" 2>nul
    echo       Cleaned: bin\Release
)
echo.

if "%SKIP_BACKUP%"=="yes" (
    echo [6/6] Skip backup ^(test build mode^)
) else (
    echo [6/6] Backing up code...
    call "%SCRIPT_DIR%backup_code.bat" publish %NEW_VERSION%
)

echo.
echo ========================================
echo   Build done! ^(not uploaded to Gitee^)
echo ========================================
echo.
echo Output:   %OUTPUT_DIR%%EXE_NAME%
echo Size:     !EXE_SIZE_MB! MB
echo Version:  v%NEW_VERSION%
echo.
echo.
pause