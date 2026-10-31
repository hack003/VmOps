$ErrorActionPreference = 'Continue'

$NewIP      = '${IP}'
$NewPrefix  = '${MASK}'
$NewGW      = '${GW}'
$NewDNS     = '${DNS}'
$NewHost    = '${COMPUTERNAME}'

Write-Host '========== Guest Operations Network Config =========='
Write-Host ('IP=' + $NewIP + '  Prefix=' + $NewPrefix + '  GW=' + $NewGW + '  DNS=' + $NewDNS + '  Host=' + $NewHost)

$DnsList = $NewDNS -split '[,; \t]+' | Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+$' }

function Convert-PrefixToMask([int]$Prefix) {
    if ($Prefix -lt 0 -or $Prefix -gt 32) { return '255.255.255.0' }
    $mask = 0xFFFFFFFF
    if ($Prefix -gt 0) { $mask = $mask -shl (32 - $Prefix) }
    $mask = $mask -band 0xFFFFFFFF
    return '{0}.{1}.{2}.{3}' -f (($mask -shr 24) -band 0xFF), (($mask -shr 16) -band 0xFF), (($mask -shr 8) -band 0xFF), ($mask -band 0xFF)
}

if ($NewHost -and $NewHost -ne $env:COMPUTERNAME) {
    Write-Host ('ComputerName: ' + $env:COMPUTERNAME + ' -> ' + $NewHost)
    try {
        Rename-Computer -NewName $NewHost -Force -ErrorAction Stop
        Write-Host 'ComputerName set (takes effect after reboot)'
    } catch {
        Write-Host ('ComputerName set failed: ' + $_.Exception.Message)
    }
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
    try {
        Write-Host 'Trying netsh fallback ...'
        $netshOut = netsh interface show interface
        Write-Host ('netsh output: ' + $netshOut)
    } catch {
        Write-Host 'netsh also failed'
    }
    Write-Host 'ERROR: No usable interface found'
    exit 1
}

$target = $adapters | Sort-Object Status | Select-Object -First 1
Write-Host ('Selected interface: ' + $target.Name + ' InterfaceDescription=' + $target.InterfaceDescription + ' Status=' + $target.Status)

$Idx = $target.ifIndex
$ifaceAlias = $target.Name
Write-Host ('InterfaceIndex=' + $Idx)

$applied = $false

try {
    Remove-NetIPAddress -InterfaceIndex $Idx -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
    Remove-NetRoute -InterfaceIndex $Idx -DestinationPrefix '0.0.0.0/0' -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
} catch { }

try {
    Write-Host 'Trying New-NetIPAddress ...'
    New-NetIPAddress -InterfaceIndex $Idx `
        -IPAddress $NewIP `
        -PrefixLength ([int]$NewPrefix) `
        -DefaultGateway $NewGW `
        -Confirm:$false -ErrorAction Stop | Out-Null
    Write-Host 'New-NetIPAddress succeeded'
    $applied = $true
} catch {
    Write-Host ('New-NetIPAddress failed: ' + $_.Exception.Message)
}

if (-not $applied) {
    Write-Host 'fallback: netsh ...'
    $Mask = Convert-PrefixToMask ([int]$NewPrefix)
    $escAlias = $ifaceAlias -replace '"', ''
    $cmd = 'netsh interface ip set address name="' + $escAlias + '" static ' + $NewIP + ' ' + $Mask + ' ' + $NewGW + ' 1'
    Write-Host ('Executing: ' + $cmd)
    try {
        Invoke-Expression $cmd 2>$null
        if ($LASTEXITCODE -eq 0) {
            Write-Host 'netsh set succeeded'
            $applied = $true
        } else {
            Write-Host ('netsh returned exit code ' + $LASTEXITCODE)
        }
    } catch {
        Write-Host ('netsh exception: ' + $_.Exception.Message)
    }
}

Start-Sleep -Seconds 1

if ($DnsList.Count -gt 0) {
    try {
        Set-DnsClientServerAddress -InterfaceIndex $Idx -ServerAddresses $DnsList -ErrorAction Stop | Out-Null
        Write-Host 'DNS set succeeded'
    } catch {
        Write-Host ('Set-DnsClientServerAddress failed: ' + $_.Exception.Message)
        try {
            netsh interface ip set dns name=$ifaceAlias static $DnsList[0] 2>$null
            if ($DnsList.Count -gt 1) { netsh interface ip add dns name=$ifaceAlias $DnsList[1] index=2 2>$null }
        } catch { }
    }
}

Write-Host '--- Verification ---'
$verifyIp = ''
try {
    $verifyIp = (Get-NetIPAddress -InterfaceIndex $Idx -AddressFamily IPv4 -ErrorAction SilentlyContinue).IPAddress
    Write-Host ('Get-NetIPAddress returned: ' + $verifyIp)
} catch { Write-Host ('Verify IP failed: ' + $_.Exception.Message) }

if ([string]::IsNullOrEmpty($verifyIp)) {
    try {
        netsh interface ip show config name=$ifaceAlias 2	ќ[›Ь‘XXЪSШљ™XЭИЬљ]KRЬЭ	ИCB€	\ЫЫ™љYИH\ЫЫ™љYИЏ‰ќ[B€Ьљ]KRЬЭ	Ъ\ЫЫ™љYИЭ]]‰ГB€Ьљ]KRЬЭ	\ЫЫ™љYГB€HШ]ЪИCBџCBѓB‰™\љYћQњИH	ЙГBќћHГB€	™\љYћQњИH
Щ]QњРЫY[ќЩ\ќ™\ђY™\ЬИR[ќ\™XЩR[™^	YPY™\ЬС[Z[HTЌQ\њ›ЬђXЭ[Ы€Ъ[[ќPЫЫќ[ќYJK”Щ\ќ™\ђY™\ЬЩ\ИZ›Ъ[€	Л	ГBџHШ]ЪИCBѓB•Ьљ]KRЬЭ	ПOOOOOOOOOHЫЫ™љYИЫЫ\]HOOOOOOOOOIГB•Ьљ]KRЬЭ
	ТT€	И
И	™\љYћR\
CB•Ьљ]KRЬЭ
	С”О€	И
И	™\љYћQњКCBѓBљY€
	\YYX[™[›ЭЬЭљ[™ЧNЋ’\Уќ[Ь‘[\J	™\љYћR\
JHГB€Ьљ]KRЬЭ	У™]ЫЬљИЫЫ™љYИЭXШЩYYY	ГB€^]BџH[ЩZY€
	\YY
HГB€Ьљ]KRЬЭ	ХРT“’S‘О€ЫЫ™љYИЫЫ[X[™ИЭXШЩYYYќ]™\љYљYYT\И[\KX^H™YY[Ь™H[YIГB€^]BџH[ЩHГB€Ьљ]KRЬЭ	СT”“ФЋ€™]ЫЬљИЫЫ™љYИY›ЭZЩHY™™XЭ	ГB€^]CBџ