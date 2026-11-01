$ErrorActionPreference = 'Continue'

function Convert-PrefixToMask([int]$Prefix) {
    if ($Prefix -lt 0 -or $Prefix -gt 32) { return '255.255.255.0' }
    $mask = 0xFFFFFFFF
    if ($Prefix -gt 0) { $mask = $mask -shl (32 - $Prefix) }
    $bytes = ($mask -band 0xFF), (($mask -shr 8) -band 0xFF), (($mask -shr 16) -band 0xFF), (($mask -shr 24) -band 0xFF)
    return ($bytes -join '.')
}

$NewIP      = '${IP}'
$NewPrefix  = '${MASK}'
$NewGW      = '${GW}'
$NewDNS     = '${DNS}'
$NewHost    = '${COMPUTERNAME}'

Write-Host '========== GuestOps Network Config (Windows) =========='
Write-Host ('IP=' + $NewIP + '  Prefix=' + $NewPrefix + '  GW=' + $NewGW + '  DNS=' + $NewDNS + '  Host=' + $NewHost)

$DnsList = @()
if ($NewDNS) {
    $DnsList = $NewDNS -split '[,; \t]+' | Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+$' }
}

if ($NewHost -and $NewHost -ne $env:COMPUTERNAME) {
    Write-Host ('ComputerName: ' + $env:COMPUTERNAME + ' -> ' + $NewHost)
    try {
        Rename-Computer -NewName $NewHost -Force -ErrorAction Stop
        Write-Host 'ComputerName set (takes effect after reboot)'
    } catch {
        Write-Host ('ComputerName set failed: ' + $_.Exception.Message)
    }
} else {
    Write-Host ('ComputerName: skip (' + $env:COMPUTERNAME + ')')
}

Write-Host '--- Interface Detection ---'
$adapters = @()
try {
    $adapters = Get-NetAdapter -ErrorAction SilentlyContinue | Where-Object { $_.InterfaceDescription -notmatch 'Loopback|Bluetooth' -and $_.Status -ne 'NotPresent' }
    Write-Host ('Get-NetAdapter found ' + @($adapters).Count + ' usable interface(s)')
    foreach ($a in $adapters) { Write-Host ('  ' + $a.Name + ' Status=' + $a.Status + ' LinkSpeed=' + $a.LinkSpeed) }
} catch {
    Write-Host ('Get-NetAdapter unavailable: ' + $_.Exception.Message)
}

if (@($adapters).Count -eq 0) {
    Write-Host 'ERROR: No usable interface found'
    exit 1
}

$target = $adapters | Sort-Object Status | Select-Object -First 1
Write-Host ('Selected interface: ' + $target.Name + ' InterfaceDescription=' + $target.InterfaceDescription + ' Status=' + $target.Status)

$Idx = $target.ifIndex
$ifaceAlias = $target.Name
Write-Host ('InterfaceIndex=' + $Idx)

$ipChanged = $false
$dnsChanged = $false

if ($NewIP -or $NewGW) {
    Write-Host '--- IP/Gateway Config ---'
    $prefixVal = if ($NewPrefix) { [int]$NewPrefix } else { 24 }

    try {
        Remove-NetIPAddress -InterfaceIndex $Idx -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
        Remove-NetRoute -InterfaceIndex $Idx -DestinationPrefix '0.0.0.0/0' -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
    } catch { }

    try {
        Write-Host 'Trying New-NetIPAddress ...'
        $splat = @{ InterfaceIndex = $Idx; IPAddress = $NewIP; PrefixLength = $prefixVal; Confirm = $false; ErrorAction = 'Stop' }
        if ($NewGW) { $splat['DefaultGateway'] = $NewGW }
        New-NetIPAddress @splat | Out-Null
        Write-Host 'New-NetIPAddress succeeded'
        $ipChanged = $true
    } catch {
        Write-Host ('New-NetIPAddress failed: ' + $_.Exception.Message)
    }

    if (-not $ipChanged -and $NewIP) {
        Write-Host 'fallback: netsh ...'
        $prefixVal2 = if ($NewPrefix) { [int]$NewPrefix } else { 24 }
        $mask = Convert-PrefixToMask $prefixVal2
        $escAlias = $ifaceAlias -replace '"', ''
        $gwPart = if ($NewGW) { ' ' + $NewGW } else { '' }
        $cmd = 'netsh interface ip set address name="' + $escAlias + '" static ' + $NewIP + ' ' + $mask + $gwPart + ' 1'
        Write-Host ('Executing: ' + $cmd)
        try {
            Invoke-Expression $cmd 2>$null
            if ($LASTEXITCODE -eq 0) { Write-Host 'netsh set succeeded'; $ipChanged = $true }
            else { Write-Host ('netsh returned exit code ' + $LASTEXITCODE) }
        } catch {
            Write-Host ('netsh exception: ' + $_.Exception.Message)
        }
    }
} else {
    Write-Host '--- IP/Gateway: skip (both empty) ---'
}

if ($DnsList.Count -gt 0) {
    Write-Host '--- DNS Config ---'
    try {
        Set-DnsClientServerAddress -InterfaceIndex $Idx -ServerAddresses $DnsList -ErrorAction Stop | Out-Null
        Write-Host 'Set-DnsClientServerAddress succeeded'
        $dnsChanged = $true
    } catch {
        Write-Host ('Set-DnsClientServerAddress failed: ' + $_.Exception.Message)
        try {
            netsh interface ip set dns name=$ifaceAlias static $DnsList[0] 2>$null
            if ($DnsList.Count -gt 1) { netsh interface ip add dns name=$ifaceAlias $DnsList[1] index=2 2>$null }
            $dnsChanged = $true
        } catch { }
    }
} else {
    Write-Host '--- DNS: skip (empty) ---'
}

Start-Sleep -Seconds 1

$verifyIp = ''
try {
    $verifyIp = (Get-NetIPAddress -InterfaceIndex $Idx -AddressFamily IPv4 -ErrorAction SilentlyContinue).IPAddress
} catch { }
$verifyDns = ''
try {
    $verifyDns = (Get-DnsClientServerAddress -InterfaceIndex $Idx -AddressFamily IPv4 -ErrorAction SilentlyContinue).ServerAddresses -join ', '
} catch { }

Write-Host '========== Config Complete =========='
Write-Host ('IP: ' + $verifyIp)
Write-Host ('DNS: ' + $verifyDns)
Write-Host ('IP changed: ' + $ipChanged + '  DNS changed: ' + $dnsChanged)

exit 0