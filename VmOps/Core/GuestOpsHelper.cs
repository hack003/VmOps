using System;
using System.Text;

namespace VmOps.Core
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
    Write-Host ('GuestOps 脚本已推送: {guestDestPath}')
    $result = Invoke-VMScript -VM $vm -ScriptText '{runCmd}' -GuestUser '{u}' -GuestPassword '{p}' -ErrorAction SilentlyContinue
    Write-Host ('GuestOps ExitCode=' + $result.ExitCode)
    if ($result.ScriptOutput) {{ Write-Host '--- ScriptOutput ---'; Write-Host $result.ScriptOutput }}
    if ($result.ScriptError) {{ Write-Host '--- ScriptError ---'; Write-Host $result.ScriptError }}
    if ($result.ExitCode -ne 0) {{ throw ('脚本执行失败 ExitCode=' + $result.ExitCode) }}
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
            bool isLinux = osName.Length > 0 && osName.IndexOf("linux|kylin|openEuler|centos|ubuntu|debian|redhat|suse|fedora|alibaba|neokylin|uos", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isLinux && osName.Length > 0 && osName.IndexOf("windows|Windows", StringComparison.Ordinal) < 0)
                isLinux = true;
            return (isLinux, guestFamilyRaw ?? "", osName);
        }
    }
}