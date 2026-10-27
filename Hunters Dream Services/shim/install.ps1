<#
  Attaches the "Hunter's Dream Services" mod to the Yharnam Together launcher, or detaches it.

    install.ps1              compiles bin\ythd-probe.exe from YtHdProbe.cs (with the C# compiler that comes with
                             Windows), sets BB_PROBE in the launcher's "Extra variables" and makes the mod show in the
                             launcher's Mods page (where it is switched on and off)
    install.ps1 -Uninstall   undoes that (the mod folder itself stays: delete it when you want it gone)
    install.ps1 -Root <dir>  the Yharnam Together folder (with BLauncher.exe), when this mod is not in <dir>\mods\

  Changes: bin\ythd-probe.exe (new), this mod's dvdroot_ps4 folder (empty, new) and the "extra_env" line of
  %APPDATA%\bbport-launcher-coop\settings.json (a backup is kept beside it). No game or Yharnam Together file is changed.
#>
param([switch]$Uninstall, [string]$Root = '', [string]$SettingsFile = '', [switch]$NoProcessCheck)
$ErrorActionPreference = 'Stop'

function Fail([string]$message) { Write-Host "`n$message" -ForegroundColor Red; exit 1 }

$mod = Split-Path $PSScriptRoot -Parent
if (-not $Root) { $Root = Split-Path (Split-Path $mod -Parent) -Parent }
if (-not (Test-Path -LiteralPath $Root)) { Fail "Folder not found: $Root" }
$Root = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\')
foreach ($need in 'BLauncher.exe', 'run.py', 'bin\bb-probe.exe') {
    if (-not (Test-Path "$Root\$need")) {
        Fail "This does not look like a Yharnam Together folder: $Root (no $need)`nPut the 'Hunters Dream Services' folder into <Yharnam Together>\mods\ , or run:  install.ps1 -Root <folder with BLauncher.exe>"
    }
}
$shim = "$Root\bin\ythd-probe.exe"
$settingsFile = if ($SettingsFile) { $SettingsFile } else { Join-Path $env:APPDATA 'bbport-launcher-coop\settings.json' }

if (-not $NoProcessCheck) {
    # only programs of THIS install count (another port's launcher may be open)
    $busy = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($Root + '\', [StringComparison]::OrdinalIgnoreCase) -and $_.Name -ne 'shadnet.exe' } | Select-Object -First 1
    if ($busy) { Fail "Close the launcher and the game first ($($busy.Name) is running); the launcher would overwrite its settings when it closes." }
}
if (-not (Test-Path $settingsFile)) { Fail "Open the launcher once first, so it creates $settingsFile" }

# The launcher stores "Extra variables" as one string of space-separated NAME=value pairs.
# Our own entry ends in ythd-probe.exe, or in that file's 8.3 short name (YTHD-P~1.EXE) when the folder path has spaces.
$OursPattern = '(?i)(ythd-probe\.exe|ythd-p~\d\.exe)$'
function Set-Probe([string]$value) {
    $utf8 = New-Object Text.UTF8Encoding($false)
    $text = [IO.File]::ReadAllText($settingsFile, $utf8)
    $match = [regex]::Match($text, '("extra_env"\s*:\s*)"((?:[^"\\]|\\.)*)"')
    if (-not $match.Success) { Fail "No extra_env setting found in $settingsFile (a newer launcher?). Set BB_PROBE=$value in the launcher's Advanced > Extra variables by hand." }
    $pairs = @([regex]::Unescape($match.Groups[2].Value) -split '\s+' | Where-Object { $_ })
    $kept = @($pairs | Where-Object { $_ -notmatch '^BB_PROBE=' })
    $old = @($pairs | Where-Object { $_ -match '^BB_PROBE=' })
    if ($value) {
        if ($old -and ($old | Where-Object { $_ -notmatch $OursPattern })) {
            Fail "BB_PROBE in the launcher already points somewhere else ($($old -join ' ')). Another mod uses it; remove that first (a launcher can have only one)."
        }
        $kept += "BB_PROBE=$value"
    }
    elseif ($old -and ($old | Where-Object { $_ -notmatch $OursPattern })) {
        Write-Host "BB_PROBE in the launcher points somewhere else ($($old -join ' ')); left alone." -ForegroundColor Yellow
        return
    }
    $encoded = ($kept -join ' ').Replace('\', '\\').Replace('"', '\"')
    $new = $text.Substring(0, $match.Index) + $match.Groups[1].Value + '"' + $encoded + '"' + $text.Substring($match.Index + $match.Length)
    $null = $new | ConvertFrom-Json   # still valid JSON, or nothing is written
    if ($new -ne $text) {
        $backup = "$settingsFile.bak-ythd"
        if (-not (Test-Path $backup)) { Copy-Item $settingsFile $backup }
        [IO.File]::WriteAllText($settingsFile, $new, $utf8)
    }
}

if ($Uninstall) {
    Write-Host "Detaching Hunter's Dream Services from the launcher..."
    Set-Probe ''
    if (Test-Path $shim) { Remove-Item $shim -Force }
    Remove-Item "$Root\user\host_overrides.hunters-dream.json" -Force -ErrorAction SilentlyContinue
    Write-Host "Done. The launcher starts the plain game again. The mod folder is still in mods\ (delete it if you do not want it)." -ForegroundColor Green
    exit 0
}

Write-Host "Attaching Hunter's Dream Services to the launcher in $Root"
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc)) { Fail "The .NET Framework 4 C# compiler was not found ($csc)." }
$tmp = Join-Path $env:TEMP "ythd-probe-$PID.exe"
& $csc /nologo /optimize+ /target:exe /platform:anycpu "/out:$tmp" /reference:System.Web.Extensions.dll "$PSScriptRoot\YtHdProbe.cs"
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $tmp)) { Fail "Compiling YtHdProbe.cs failed." }

# NAME=value pairs are separated by spaces, so the path must have none (8.3 short name if it does).
Copy-Item $tmp $shim -Force
Remove-Item $tmp -Force
$probe = $shim
if ($probe -match '\s') {
    $probe = (New-Object -ComObject Scripting.FileSystemObject).GetFile($shim).ShortPath
    if ($probe -match '\s') { Remove-Item $shim -Force; Fail "The path of bin\ythd-probe.exe contains a space and has no short (8.3) name. Install Yharnam Together in a folder without spaces." }
}
Set-Probe $probe

# the launcher lists the folders of mods\ that have a dvdroot_ps4: this one has no game files, only the switch
New-Item -ItemType Directory -Force "$mod\dvdroot_ps4" | Out-Null

Write-Host "`nWhat the mod will do on the next start:" -ForegroundColor Cyan
& $shim --ythd-check
Write-Host "`nDone. Open the launcher: 'Mods' lists '$(Split-Path $mod -Leaf)' with its on/off switch. Start the game with PLAY." -ForegroundColor Green
Write-Host "In the log (user\log\console.txt) look for the line that starts with  YTHD:"
