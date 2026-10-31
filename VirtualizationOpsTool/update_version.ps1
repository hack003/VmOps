param(
    [string]$ProjectRoot,
    [string]$NewVersion
)

$ErrorActionPreference = "Stop"

$OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$csprojFile = Join-Path $ProjectRoot "VirtualizationOpsTool.csproj"

Write-Host "æ›´æ–° csproj æ–‡ä»¶..."
$content = Get-Content $csprojFile -Raw -Encoding UTF8
$content = $content -replace '<Version>[^<]*</Version>', ('<Version	È
È	™]Õ™\œÚ[Ûˆ
È	ÏÕ™\œÚ[Û‰ÊCB–ÔÞ\Ý[K’SË‘š[WNŽ•Üš]P[^
	ÜÜ›Ú‘š[K	ÛÛ[ÔÞ\Ý[K•^‘[˜ÛÙ[™×NŽ•UŽ
CB•Üš]KRÜÝˆÜÜ›Úˆ9âb9§+9cí[{.i»NikK‹¢DæWufW'6–öâ Ð Ð¥w&—FRÔ†÷7B$fW'6–öâæ72KÛþyJŽzˆ¾[¨þ™¸nXøÞ[NŠû¾XùnûÈÎiz™Èi»Nik