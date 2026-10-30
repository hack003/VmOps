param(
    [string]$ProjectRoot,
    [string]$NewVersion
)

$ErrorActionPreference = "Stop"

$OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$csprojFile = Join-Path $ProjectRoot "VirtualizationOpsTool.csproj"

Write-Host "更新 csproj 文件..."
$content = Get-Content $csprojFile -Raw -Encoding UTF8
$content = $content -replace '<Version>[^<]+</Version>', ('<Version>' + $NewVersion + '</Version>')
[System.IO.File]::WriteAllText($csprojFile, $content, [System.Text.Encoding]::UTF8)
Write-Host "  csproj 版本号已更新为 $NewVersion"

Write-Host "AppVersion.cs 使用程序集反射读取，无需更新"