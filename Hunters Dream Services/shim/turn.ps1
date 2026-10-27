<#
  Switches the mod on or off the way the launcher's Mods page does: it edits <Yharnam Together>\mods.json.

    turn.ps1 -Mode on | off | status        status shows the switch and what the mod would do (changes nothing)
    turn.ps1 -Root <dir>                    the Yharnam Together folder, when this mod is not in <dir>\mods\

  Close the launcher first (it would overwrite mods.json when it closes).
#>
param([ValidateSet('on', 'off', 'status')][string]$Mode = 'status', [string]$Root = '', [switch]$NoProcessCheck)
$ErrorActionPreference = 'Stop'

function Fail([string]$message) { Write-Host "`n$message" -ForegroundColor Red; exit 1 }

$mod = Split-Path $PSScriptRoot -Parent
$name = Split-Path $mod -Leaf
if (-not $Root) { $Root = Split-Path (Split-Path $mod -Parent) -Parent }
$Root = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\')
$config = "$Root\mods.json"
$utf8 = New-Object Text.UTF8Encoding($false)

function Read-Mods {
    $order = @(); $disabled = @()
    if (Test-Path -LiteralPath $config) {
        $json = [IO.File]::ReadAllText($config, $utf8) | ConvertFrom-Json
        foreach ($p in $json.PSObject.Properties) {
            if ($p.Name -notin 'order', 'disabled') { Fail "mods.json has an entry '$($p.Name)' this script does not know; use the launcher's Mods page instead." }
        }
        if ($json.order) { $order = @($json.order) }
        if ($json.disabled) { $disabled = @($json.disabled) }
    }
    return @{ order = $order; disabled = $disabled }
}
function Format-Mods($mods) {
    function List($items) {
        if (-not $items.Count) { return '[]' }
        '[' + "`r`n" + (($items | ForEach-Object { '    "' + ([string]$_).Replace('\', '\\').Replace('"', '\"') + '"' }) -join ",`r`n") + "`r`n  ]"
    }
    "{`r`n  `"order`": " + (List $mods.order) + ",`r`n  `"disabled`": " + (List $mods.disabled) + "`r`n}`r`n"
}

$mods = Read-Mods
$isOff = $mods.disabled -contains $name

if ($Mode -eq 'status') {
    Write-Host "Hunter's Dream Services in $Root" -ForegroundColor Cyan
    Write-Host ("  switch in the Mods page : " + $(if ($isOff) { 'OFF' } else { 'ON' }))
    $shim = "$Root\bin\ythd-probe.exe"
    if (Test-Path $shim) { & $shim --ythd-check | ForEach-Object { Write-Host "  $_" } }
    else { Write-Host "  not installed yet (run Install.cmd)" }
    exit 0
}

if (-not $NoProcessCheck) {
    $busy = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($Root + '\', [StringComparison]::OrdinalIgnoreCase) -and $_.Name -ne 'shadnet.exe' } | Select-Object -First 1
    if ($busy) { Fail "Close the launcher and the game first ($($busy.Name) is running); the launcher would overwrite the switch when it closes." }
}

if ($Mode -eq 'off') {
    if ($isOff) { Write-Host "Already OFF." -ForegroundColor Yellow; exit 0 }
    $mods.disabled = @($mods.disabled) + $name
} else {
    if (-not $isOff) { Write-Host "Already ON." -ForegroundColor Yellow; exit 0 }
    $mods.disabled = @($mods.disabled | Where-Object { $_ -ne $name })
}
if ((Test-Path -LiteralPath $config) -and -not (Test-Path "$config.bak-ythd")) { Copy-Item $config "$config.bak-ythd" }
[IO.File]::WriteAllText($config, (Format-Mods $mods), $utf8)
if ($Mode -eq 'on') {
    Write-Host "`nHunter's Dream Services is ON: the next start takes messages, bloodstains, ghosts and chalice search from The Hunter's Dream." -ForegroundColor Green
} else {
    Write-Host "`nHunter's Dream Services is OFF: the next start uses your host's own services, as before." -ForegroundColor Green
}
