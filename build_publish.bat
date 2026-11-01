@echo off
chcp 65001 >nul 2>&1
setlocal enabledelayedexpansion

echo ========================================
echo   虚拟化运维工具.NET - 正式发布版本打包
echo ========================================
echo.

set "SCRIPT_DIR=%~dp0"
set "PROJECT_DIR=%SCRIPT_DIR%VirtualizationOpsTool"
set "OUTPUT_DIR=%SCRIPT_DIR%"

set "APP_NAME=虚拟化运维工具.NET"
set "EXE_NAME=%APP_NAME%.exe"
set "CSPROJ_FILE=%PROJECT_DIR%\VirtualizationOpsTool.csproj"

for /f "delims=" %%a in ('powershell -NoProfile -Command "(Get-Content '%CSPROJ_FILE%' -Raw) -match '<Version>([^<]*)</Version>' | Out-Null; if ($Matches.Count -gt 1) { $Matches[1] } else { 'VERSION_NOT_FOUND' }"') do (
    set "OLD_VERSION=%%a"
)

if "%OLD_VERSION%"=="" (
    echo [错误] 无法从 csproj 读取版本号！
    echo        请检查 %CSPROJ_FILE% 中的 ^<Version^> 标签是否存在且非空
    pause
    exit /b 1
)
if "%OLD_VERSION%"=="VERSION_NOT_FOUND" (
    echo [错误] csproj 中的 ^<Version^> 标签为空或不存在！
    echo        请手动修复后重试
    pause
    exit /b 1
)

echo [1/5] 处理版本号...
echo       当前版本: v%OLD_VERSION%
echo.
echo       请选择:
echo         [1] 递增修订号 (v%OLD_VERSION% ^> v?  默认)
echo         [2] 保持版本不变 (仅测试编译)
echo.
set /p "VER_CHOICE=请输入选择 [回车=1]: "
if "%VER_CHOICE%"=="" set "VER_CHOICE=1"

if "%VER_CHOICE%"=="2" (
    set "NEW_VERSION=%OLD_VERSION%"
    set "SKIP_BACKUP=yes"
    echo.
    echo       版本号保持不变: v%NEW_VERSION% ^(跳过版本文件更新^)
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
    echo [版本] %OLD_VERSION%  -^>  !NEW_VERSION!  OK
)
echo       本次打包版本: v%NEW_VERSION%
echo.

echo [2/5] 编译 Release 版本...
cd /d "%PROJECT_DIR%"

call dotnet publish --configuration Release --framework net48 --runtime win-x64 -p:SelfContained=false -p:DebugType=none

if %errorlevel% neq 0 (
    echo.
    echo 编译失败！
    pause
    exit /b 1
)

echo       编译成功
echo.

echo [3/5] 查找编译输出...
set "EXE_PATH=%PROJECT_DIR%\bin\Release\net48\win-x64\publish\VirtualizationOpsTool.exe"

if not exist "%EXE_PATH%" (
    for /r "%PROJECT_DIR%\bin\Release" %%f in (VirtualizationOpsTool.exe) do (
        set "EXE_PATH=%%f"
        goto :found_exe
    )
    echo [错误] 未找到编译输出的 exe 文件
    pause
    exit /b 1
)

:found_exe
echo       找到: %EXE_PATH%
echo.

echo [4/5] 复制到输出目录...
copy /Y "%EXE_PATH%" "%OUTPUT_DIR%%EXE_NAME%" >nul
echo       主程序: %EXE_NAME%

set "CONFIG_SRC=%PROJECT_DIR%\bin\Release\net48\win-x64\publish\VirtualizationOpsTool.exe.config"
if not exist "%CONFIG_SRC%" set "CONFIG_SRC=%PROJECT_DIR%\App.config"
if exist "%CONFIG_SRC%" (
    copy /Y "%CONFIG_SRC%" "%OUTPUT_DIR%%APP_NAME%.exe.config" >nul
    echo       配置文件: %APP_NAME%.exe.config
)

set "MANIFEST_SRC=%PROJECT_DIR%\app.manifest"
if not exist "%MANIFEST_SRC%" set "MANIFEST_SRC=%PROJECT_DIR%\bin\Release\net48\win-x64\publish\VirtualizationOpsTool.exe.manifest"
if exist "%MANIFEST_SRC%" (
    copy /Y "%MANIFEST_SRC%" "%OUTPUT_DIR%%APP_NAME%.exe.manifest" >nul
    echo       清单文件: %APP_NAME%.exe.manifest
)

for %%i in ("%OUTPUT_DIR%%EXE_NAME%") do set "EXE_SIZE=%%~zi"
set /a "EXE_SIZE_MB=EXE_SIZE/1048576"

echo       输出文件（单 exe + 配置）:
echo       %APP_NAME%.exe          ^(内嵌 update_helper.exe^)
echo       %APP_NAME%.exe.config
echo       %APP_NAME%.exe.manifest
echo       主程序大小: !EXE_SIZE_MB! MB
echo.

echo [清理] 清理临时文件...
if exist "%PROJECT_DIR%\bin\Release" (
    rmdir /s /q "%PROJECT_DIR%\bin\Release" 2>nul
    echo       已清理: bin\Release
)
echo.

if "%SKIP_BACKUP%"=="yes" (
    echo [5/5] 跳过备份 ^(测试编译模式^)
) else (
    echo [5/5] 备份代码...
    call "%SCRIPT_DIR%backup_code.bat" publish %NEW_VERSION%
)

echo.
echo ========================================
echo   打包完成！（未上传 Gitee）
echo ========================================
echo.
echo 可执行文件: %OUTPUT_DIR%%EXE_NAME%
echo 文件大小:   !EXE_SIZE_MB! MB
echo 版本号:     v%NEW_VERSION%
echo.
echo 如需上传到 Gitee，请双击 upload_to_gitee.ps1
echo.
pause