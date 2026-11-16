# Builds Hospital + HospitalTests, launches RimWorld with -hospitaltest=<save>, waits, prints results.
# Exit codes: 0 = all passed, 1 = failures / missing results / build failure, 2 = timeout.
param(
    [string]$SaveName = "HospitalTest",
    [int]$TimeoutMinutes = 10,
    # Optional separate data folder (own ModsConfig.xml needed there with Harmony + Hospital + Hospital Tests enabled). Off by default.
    [string]$SaveDataFolder = "",
    # RimWorld install folder. Default: $env:RIMWORLD_DIR, else detected from Steam (all libraries).
    [string]$GameDir = "",
    # dotnet executable. Default: dotnet on PATH, else the standard install location.
    [string]$Dotnet = ""
)

$ErrorActionPreference = "Stop"

function Find-Dotnet {
    if ($Dotnet) { return $Dotnet }
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $default = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
    if (Test-Path $default) { return $default }
    throw "dotnet not found - install the .NET SDK or pass -Dotnet <path>"
}

function Find-GameDir {
    if ($GameDir) { return $GameDir }
    if ($env:RIMWORLD_DIR) { return $env:RIMWORLD_DIR }
    $steam = (Get-ItemProperty -Path "HKCU:\Software\Valve\Steam" -Name SteamPath -ErrorAction SilentlyContinue).SteamPath
    if ($steam) {
        # Main Steam library first, then extra libraries listed in libraryfolders.vdf.
        $libs = @($steam)
        $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            $libs += Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' |
                ForEach-Object { $_.Matches[0].Groups[1].Value.Replace('\\', '\') }
        }
        foreach ($lib in $libs) {
            $dir = Join-Path $lib "steamapps\common\RimWorld"
            if (Test-Path (Join-Path $dir "RimWorldWin64.exe")) { return $dir }
        }
    }
    throw "RimWorld not found - pass -GameDir <path> or set RIMWORLD_DIR"
}

$dotnet = Find-Dotnet
$game = Join-Path (Find-GameDir) "RimWorldWin64.exe"
$repo = Split-Path -Parent $PSScriptRoot
$dataDir = if ($SaveDataFolder) { $SaveDataFolder } else { Join-Path $env:USERPROFILE "AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios" }
$results = Join-Path $dataDir "HospitalTestResults.txt"
$playerLog = Join-Path $dataDir "Player.log"

& $dotnet build (Join-Path $repo "Source\Hospital\Hospital.csproj") -c Release -p:RwVersion=1.6 -v q
if ($LASTEXITCODE -ne 0) { Write-Host "Hospital build failed"; exit 1 }
& $dotnet build (Join-Path $repo "Tests\HospitalTests\Source\HospitalTests.csproj") -v q
if ($LASTEXITCODE -ne 0) { Write-Host "HospitalTests build failed"; exit 1 }

if (Test-Path $results) { Remove-Item $results -Force }

$gameArgs = @("-hospitaltest=$SaveName")
if ($SaveDataFolder) { $gameArgs += "-savedatafolder=$SaveDataFolder" }
Write-Host "Starting RimWorld: $($gameArgs -join ' ')"
$proc = Start-Process -FilePath $game -ArgumentList $gameArgs -PassThru

if (-not $proc.WaitForExit($TimeoutMinutes * 60 * 1000)) {
    Write-Host "TIMEOUT after $TimeoutMinutes min - killing game"
    Stop-Process -Id $proc.Id -Force
    $timedOut = $true
}

if (Test-Path $playerLog) {
    $bad = Select-String -Path $playerLog -Pattern "Exception|\[HospitalTests\]|^Hospital.*error" -SimpleMatch:$false | Select-Object -First 40
    if ($bad) { Write-Host "---- Player.log (exceptions / harness lines) ----"; $bad | ForEach-Object { Write-Host $_.Line } }
}

if (-not (Test-Path $results)) {
    Write-Host "No results file at $results"
    exit $(if ($timedOut) { 2 } else { 1 })
}
Write-Host "---- $results ----"
$text = Get-Content $results -Raw
Write-Host $text
if ($timedOut) { exit 2 }
if ($text -match "RESULT: \d+ passed, 0 failed") { exit 0 } else { exit 1 }
