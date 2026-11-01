param(
    [string]$Token = '',
    [string]$Tag = '',
    [string]$ExePath = ''
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# ================================================================
#  Gitee 私人令牌（别传 Git！.gitignore 已忽略此文件）
# ================================================================
if ($Token -eq '') { $Token = '278ccea6c1b3be77d12cb63743a2c1cb' }

$RepoOwner = 'hack003'
$RepoName  = 'VmOps-Tool'
$RepoPath  = "$RepoOwner/$RepoName"

$RootDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$Csproj    = Join-Path $RootDir 'VirtualizationOpsTool\VirtualizationOpsTool.csproj'
$exeName   = '虚拟化运维工具.NET.exe'
$UpdateMd  = Join-Path $RootDir 'update.md'

if ($Tag -eq '') {
    $c = Get-Content $Csproj -Raw -Encoding UTF8
    if ($c -match '<Version>([^<]+)</Version>') {
        $Tag = "v$($Matches[1])"
    } else {
        Write-Host '[ERROR] 无法从 csproj 读取版本号' -ForegroundColor Red
        return
    }
}

if ($ExePath -eq '') {
    $ExePath = Join-Path $RootDir $exeName
}

if (-not (Test-Path $ExePath)) {
    Write-Host "[ERROR] EXE not found: $ExePath" -ForegroundColor Red
    Write-Host '       请先双击 build_publish.bat 打包' -ForegroundColor Yellow
    return
}

$sizeMB = [math]::Round((Get-Item $ExePath).Length / 1MB, 1)

$changelog = ''
if (Test-Path $UpdateMd) {
    $raw = Get-Content $UpdateMd -Raw -Encoding UTF8
    if ($null -ne $raw -and $raw.Trim().Length -gt 0) {
        $changelog = $raw.Trim()
        Write-Host "  Changelog: update.md ($($changelog.Length) 字符)" -ForegroundColor Gray
    } else {
        $changelog = '1. 修复了一些已知问题。'
        Write-Host '  Changelog: update.md 为空，使用默认内容' -ForegroundColor Yellow
    }
} else {
    $changelog = '1. 修复了一些已知问题。'
    Write-Host '  Changelog: update.md 不存在，使用默认内容' -ForegroundColor Yellow
}

Write-Host ''
Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  上传到 Gitee Release' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host "  Repo:   $RepoPath" -ForegroundColor Gray
Write-Host "  Tag:    $Tag" -ForegroundColor Gray
Write-Host "  EXE:    $exeName ($sizeMB MB)" -ForegroundColor Gray
Write-Host ''

Write-Host '[1/2] Find or create Release...' -NoNewline
try {
    $all = Invoke-RestMethod -Uri "https://gitee.com/api/v5/repos/$RepoPath/releases?access_token=$Token&per_page=100" -TimeoutSec 15
    $found = $all | Where-Object { $_.tag_name -eq $Tag }
    if ($found) {
        $releaseId = $found.id
        Write-Host " OK (exists id=$releaseId)" -ForegroundColor Green
        if ($changelog -ne '' -and $found.body -match '^Release v' -and $found.body -notmatch '更新日志' -and $found.body -notmatch 'Bug') {
            $escapedBody = [Uri]::EscapeDataString($changelog)
            $escapedName = [Uri]::EscapeDataString($found.name)
            $escapedTag  = [Uri]::EscapeDataString($found.tag_name)
            $patchData   = "access_token=$Token&tag_name=$escapedTag&name=$escapedName&body=$escapedBody"
            & curl.exe -s -X PATCH -H "Content-Type: application/x-www-form-urlencoded" -d $patchData "https://gitee.com/api/v5/repos/$RepoPath/releases/$releaseId" 2>$null | Out-Null
            Write-Host '       已更新 Release body 为 update.md 内容' -ForegroundColor Green
        }
    } else {
        $newBody = if ($changelog -ne '') { $changelog } else { "Release $Tag" }
        $body = @{
            access_token     = $Token
            tag_name         = $Tag
            name             = $Tag
            body             = $newBody
            target_commitish = 'master'
            draft            = $false
            prerelease       = $false
        }
        $rel = Invoke-RestMethod -Uri "https://gitee.com/api/v5/repos/$RepoPath/releases" -Method Post -Body $body -TimeoutSec 30
        $releaseId = $rel.id
        Write-Host " OK (created id=$releaseId)" -ForegroundColor Green
    }
} catch {
    Write-Host " [FAIL] $($_.Exception.Message)" -ForegroundColor Red
    return
}

Write-Host '[2/2] Upload EXE...' -NoNewline
try {
    $url = "https://gitee.com/api/v5/repos/$RepoPath/releases/$releaseId/attach_files"
    & curl.exe -s -S -X POST -H "Authorization: token $Token" -F "file=@$ExePath" -F "name=$exeName" "$url" 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Host ' OK' -ForegroundColor Green
    } else {
        Write-Host " [WARN] curl exit=$LASTEXITCODE" -ForegroundColor Yellow
    }
} catch {
    Write-Host " [FAIL] $($_.Exception.Message)" -ForegroundColor Red
    return
}

Write-Host ''
Write-Host '========================================' -ForegroundColor Green
Write-Host '  DONE!' -ForegroundColor Green
Write-Host '========================================' -ForegroundColor Green
Write-Host "  Release: https://gitee.com/$RepoPath/releases/tag/$Tag"
Write-Host ''

if ($changelog -ne '1. 修复了一些已知问题。' -and (Test-Path $UpdateMd)) {
    [IO.File]::WriteAllText($UpdateMd, '', [Text.Encoding]::UTF8)
    Write-Host '  update.md 已清空' -ForegroundColor Cyan
}