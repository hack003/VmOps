using System;
using System.Text;

namespace VirtualizationOpsTool.Core
{
    public static class GuestOpsHelper
    {
        public static string BuildCredentialCheckScript(string vmMoRef, string user, string password, bool isLinux)
        {
            string scriptText = isLinux ? "id -un" : "whoami";
            string vmRef = vmMoRef.Replace("'", "''");
            string u = user.Replace("'", "''");
            string p = password.Replace("'", "''");

            return $@"
$ErrorActionPreference = 'Stop'
try {{
    $vm = Get-VM -Id '{vmRef}' -ErrorAction Stop
    $r = Invoke-VMScript -VM $vm -ScriptText '{scriptText}' `
        -GuestUser '{u}' -GuestPassword '{p}' -ErrorAction Stop
    if ($r.ExitCode -eq 0) {{
        Write-Output ('VMT_OK:' + $r.ScriptOutput.Trim())
    }} else {{
        Write-Output ('VMT_FAIL:ExitCode=' + $r.ExitCode)
    }}
}} catch {{
    $m = $_.Exception.Message
    if ($m -match 'authenticate|Authentication|credential|InvalidGuestLogin') {{
        Write-Output 'VMT_BADAUTH'
    }} elseif ($m -match 'tools|Tools') {{
        Write-Output 'VMT_NOTOOLS'
    }} else {{
        Write-Output ('VMT_ERROR:' + $m)
    }}
}}";
        }

        public static string BuildUploadAndRunScript(
            string vmMoRef, string user, string password, bool isLinux,
            string guestScriptContentB64, string guestDestPath,
            string runCmd, bool runAsRootOnLinux = true)
        {
            string vmRef = vmMoRef.Replace("'", "''");
            string u = user.Replace("'", "''");
            string p = password.Replace("'", "''");
            string cleanupCmd = isLinux
                ? $"rm -f '{guestDestPath}'"
                : $"Remove-Item -LiteralPath '{guestDestPath}' -Force -ErrorAction SilentlyContinue";

            return $@"
$ErrorActionPreference = 'Stop'
$vm = Get-VM -Id '{vmRef}' -ErrorAction Stop
$localTmp = Join-Path $env:TEMP ('vmt_ops_' + [IO.Path]::GetRandomFileName())
try {{
    $content = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{guestScriptContentB64}'))
    $utf8NoBom = [Text.UTF8Encoding]::new($false)
    $utf8Bom = [Text.UTF8Encoding]::new($true)
    $enc = {(isLinux ? "$utf8NoBom" : "$utf8Bom")}
    [IO.File]::WriteAllText($localTmp, $content, $enc)
    Copy-VMGuestFile -VM $vm -Source $localTmp -Destination '{guestDestPath}' -LocalToGuest -GuestUser '{u}' -GuestPassword '{p}' -Force -ErrorAction Stop | Out-Null
    Write-Host ('GuestOps ËÑöÊú¨Â∑≤Êé®ÈÄÅ: {guestDestPath}')
    $result = Invoke-VMScript -VM $vm -ScriptText '{runCmd}' -GuestUser '{u}' -GuestPassword '{p}' -ErrorAction SilentlyContinue
    Write-Host ('GuestOps ExitCode=' + $result.ExitCode)
    if ($result.ScriptOutput) {{ Write-Host '--- ScriptOutput ---'; Write-Host $result.ScriptOutput }}
    if ($result.ScriptError) {{ Write-Host '--- ScriptError ---'; Write-Host $result.ScriptError }}
    if ($result.ExitCode -ne 0) {{ throw ('ËÑöÊú¨ÊâßË°åÂ§±Ë¥• ExitCode=' + $result.ExitCode) }}
}} finally {{
    Remove-Item $localTmp -Force -ErrorAction SilentlyContinue
    try {{ Invoke-VMScript -VM $vm -ScriptText '{cleanupCmd}' -GuestUser '{u}' -GuestPassword '{p}' -ErrorAction SilentlyContinue | Out-Null }} catch {{}}
}}";
        }

        public static (bool isLinux, string guestFamily, string guestFullName) DetectGuestOs(
            string vmMoRef, string guestFamilyRaw, string guestFullNameRaw)
        {
            if (!string.IsNullOrEmpty(guestFamilyRaw))
                return (guestFamilyRaw == "linuxGuest", guestFamilyRaw, guestFullNameRaw);

            string osName = guestFullNameRaw ?? "";
            bool isLinux = osName.Length 	âà‹”ò[YKí[ô^Ÿäõ[ù^ﬁ[[ü‹[ë][\üŸ[ù‹ﬂXù[ù_XöX[üôY]›\Ÿ_ôY‹ò_[XòXò_ô[⁄ﬁ[[ü[‹»ã›ö[ô–€€\\ö\€€ãì‹ô[ò[Y€õ‹ôPÿ\ŸJH“∞–¢ñbÇó4∆ñÁWÇbb˜4Ê÷R‰∆VÊwFÇ‚bb˜4Ê÷R‰ñÊFWÑˆbÇ'vñÊF˜w7≈vñÊF˜w2"¬7G&ñÊt6ˆ◊&ó6ˆ‚‰˜&FñÊ¬í¬ê–¢ó4∆ñÁWÇ“G'VS∞–¢&WGW&‚Üó4∆ñÁWÇ¬wVW7Df÷ñ«ï&rÛÚ""¬˜4Ê÷Rì∞–¢––¢––ß