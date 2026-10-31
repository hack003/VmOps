$targetUser = '${TARGET_USER}'
$newPw = '${NEW_PASSWORD}'
if (-not $targetUser) { $targetUser = 'Administrator' }
if (-not $newPw) { Write-Output 'FAIL:empty_password'; Remove-Item $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue; exit 1 }

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = "net.exe"
$psi.Arguments = "user $targetUser `"$newPw`""
$psi.RedirectStandardError = $true
$psi.RedirectStandardOutput = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$proc = [System.Diagnostics.Process]::Start($psi)
$stdout = $proc.StandardOutput.ReadToEnd()
$stderr = $proc.StandardError.ReadToEnd()
$proc.WaitForExit()
$code = $proc.ExitCode

if ($code -eq 0) { Write-Output 'SUCCESS'; Remove-Item $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue; exit 0 }
Write-Output ("FAIL:" + $code + (if ($stderr) { '|' + $stderr.Trim() } else { '' }))
Remove-Item $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
exit 1