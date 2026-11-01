@echo off
chcp 65001 >nul 2>&1
setlocal enabledelayedexpansion

echo ========================================
echo   虚拟化运维工具.NET - 代码备份
echo ========================================
echo.

set "SCRIPT_DIR=%~dp0"
set "SOURCE_ROOT=%SCRIPT_DIR:~0,-1%"

set "APP_NAME=虚拟化运维工具.NET"
set "BUILD_TYPE=%~1"
set "VERSION_STR=%~2"

set "CSPROJ=%SOURCE_ROOT%\VirtualizationOpsTool\VirtualizationOpsTool.csproj"

if "%VERSION_STR%"=="" (
    for /f "delims=" %%a in ('powershell -NoProfile -Command "(Get-Content '%CSPROJ%' -Raw) -match '<Version>([^<]+)</Version>' | Out-Null; $Matches[1]"') do (
        set "VERSION_STR=%%a"
    )
)
set "VERSION_STR=%VERSION_STR: =%"

if "%BUILD_TYPE%"=="" (
    set "ZIP_NAME=code-%APP_NAME%-v%VERSION_STR%.zip"
) else (
    set "ZIP_NAME=code-%APP_NAME%-v%VERSION_STR%-%BUILD_TYPE%.zip"
)

echo [信息] 程序名称: %APP_NAME%
echo [信息] 版本号:   v%VERSION_STR%
echo [信息] 输出文件: %SOURCE_ROOT%\%ZIP_NAME%
echo.

set "TEMP_DIR=%TEMP%\VmOps_backup_%RANDOM%%RANDOM%"
mkdir "%TEMP_DIR%" 2>nul

echo [1/2] 收集源代码文件...

echo       复制 VirtualizationOpsTool...
robocopy "%SOURCE_ROOT%\VirtualizationOpsTool" "%TEMP_DIR%\VirtualizationOpsTool" /E /XD bin obj publish .vs .idea __pycache__ backups 2>nul

echo       复制 UpdateHelper...
robocopy "%SOURCE_ROOT%\UpdateHelper" "%TEMP_DIR%\UpdateHelper" /E /XD bin obj publish .vs .idea __pycache__ 2>nul

if exist "%SOURCE_ROOT%\*.sln" (
    echo       复制解决方案文件...
    copy /Y "%SOURCE_ROOT%\*.sln" "%TEMP_DIR%\" >nul
) else (
    echo       无 .sln 文件，跳过
)

echo       复制打包脚本...
for %%f in (build_publish.bat backup_code.bat upload_to_gitee.ps1 .gitignore) do (
    if exist "%SOURCE_ROOT%\%%f" copy /Y "%SOURCE_ROOT%\%%f" "%TEMP_DIR%\" >nul
)

echo       复制 md 文档...
copy /Y "%SOURCE_ROOT%\*.md" "%TEMP_DIR%\" >nul 2>nul

if exist "%SOURCE_ROOT%\.trae" (
    echo       复制 .trae 配置...
    robocopy "%SOURCE_ROOT%\.trae" "%TEMP_DIR%\.trae" /E 2>nul
)

set "FILE_COUNT=0"
for /f %%i in ('dir "%TEMP_DIR%" /s /b /a-d 2^>nul ^| find /c /v ""') do set "FILE_COUNT=%%i"
echo       文件数量: %FILE_COUNT%
echo.

echo [2/2] 压缩备份文件...
set "ZIP_FILE=%SOURCE_ROOT%\%ZIP_NAME%"
if exist "%ZIP_FILE%" del /f /q "%ZIP_FILE%"

powershell -NoProfile -Command "Add-Type -AssemblyName 'System.IO.Compression.FileSystem'; [System.IO.Compression.ZipFile]::CreateFromDirectory('%TEMP_DIR%','%ZIP_FILE%')"

if exist "%ZIP_FILE%" (
    for %%i in ("%ZIP_FILE%") do set "ZIP_SIZE=%%~zi"
    echo       压缩完成
    echo.
    echo ========================================
    echo   备份完成！
    echo ========================================
    echo   文件: %ZIP_FILE%
    echo   大小: !ZIP_SIZE! 字节
    echo.
) else (
    echo [错误] 压缩失败
)

if exist "%TEMP_DIR%" rmdir /s /q "%TEMP_DIR%"