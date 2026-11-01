param(
    [string]$Token = '',
    [string]$Branch = 'master',
    [string]$CommitMsg = ''
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

if ($Token -eq '') { $Token = '278ccea6c1b3be77d12cb63743a2c1cb' }

$RepoPath = 'hack003/VmOps-Tool'
$Prefix = 'VirtualizationOpsTool'
$RootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SrcDir = Join-Path $RootDir 'VirtualizationOpsTool'

if (-not (Test-Path $SrcDir)) { Write-Host '[ERROR] Source dir not found:' $SrcDir -ForegroundColor Red; return }
if ($CommitMsg -eq '') { $CommitMsg = 'sync source: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') }

$extsKeep = @('.cs', '.csproj', '.config', '.resx', '.sln', '.xaml', '.json', '.xml', '.png', '.ico', '.html', '.js', '.css', '.txt', '.md', '.sql', '.ps1', '.bat', '.gitignore', '.gitattributes', '.sh')
$files = Get-ChildItem $SrcDir -Recurse -File | Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\|\\publish\\|\.vs\\' -and ($extsKeep -contains $_.Extension.ToLower()) }

Write-Host ''
Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  同步源代码到 Gitee master 分支' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ('  Repo: ' + $RepoPath + '/' + $Prefix) -ForegroundColor Gray
Write-Host ('  Files: ' + $($files.Count)) -ForegroundColor Gray
Write-Host ''

$ok = 0; $fail = 0
$tmpDir = [IO.Path]::GetTempPath() + 'gitee_sync_' + [guid]::NewGuid().ToString('N')
New-Item -ItemType Directory -Path $tmpDir | Out-Null

try {
    foreach ($f in $files) {
        $rel = $f.FullName.Substring($SrcDir.Length + 1).Replace('\', '/')
        $giteePath = $Prefix + '/' + $rel
        $apiUrl = 'https://gitee.com/api/v5/repos/' + $RepoPath + '/contents/' + $giteePath

        # GET sha 判断文件是否已存在
        $sha = ''
        try {
            $wc = New-Object Net.WebClient
            $wc.Encoding = [Text.Encoding]::UTF8
            $getUrl = $apiUrl + '?access_token=' + $Token + '&ref=' + $Branch
            $json = $wc.DownloadString($getUrl)
            $j = $json | ConvertFrom-Json
            if ($j.sha) { $sha = $j.sha }
        } catch { }

        $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($f.FullName))
        $bodyStr = 'access_token=' + $Token +
                    '&content=' + $b64 +
                    '&message=' + [Uri]::EscapeDataString($CommitMsg) +
                    '&branch=' + $Branch

        # PUT 更新 (带 sha) / POST 创建 (不带 sha)
        $method = 'POST'
        if ($sha) { $method = 'PUT'; $bodyStr += '&sha=' + $sha }

        $bodyFile = Join-Path $tmpDir 'body.txt'
        [IO.File]::WriteAllText($bodyFile, $bodyStr, [Text.Encoding]::ASCII)

        $result = curl.exe -s -w "
HTTPCODE:%{http_code}" -X $method -H "Content-Type: application/x-www-form-urlencoded" -d "@$bodyFile" $apiUrl
        $code = ''
        foreach ($line in $result) { if ($line -match '^HTTPCODE:(\d+)') { $code = $Matches[1] } }
        $okCodes = if ($method -eq 'POST') { '201' } else { '200' }
        if ($okCodes -contains $code) {
            Write-Host ('  OK  [' + $method + '] ' + $rel) -ForegroundColor Green; $ok++
        } else {
            $err = ($result | Where-Object { $_ -notmatch '^HTTPCODE:' } | Out-String).Trim()
            Write-Host ('  FAIL ' + $rel + '  HTTP=' + $code + '  ' + $err) -ForegroundColor Red; $fail++
        }
    }
} finally { Remove-Item $tmpDir -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host ''
Write-Host ('  DONE!  OK=' + $ok + '  FAIL=' + $fail) -ForegroundColor $(if ($fail -eq 0) { 'Green' } else { 'Yellow' })
Write-Host ('  https://gitee.com/' + $RepoPath + '/tree/' + $Branch + '/' + $Prefix)
Write-Host ''