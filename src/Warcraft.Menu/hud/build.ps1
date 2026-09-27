param(
    [string]$Cs2 = "",
    [string]$Addon = "cs2warcraft_ui",
    [switch]$NoLocalInstall
)

$ErrorActionPreference = "Stop"
$src = Split-Path -Parent $MyInvocation.MyCommand.Path
$resources = @(
    @{ Name = "warcraft_menu"; Type = "layout"; Ext = "xml"; Compiled = "vxml_c" },
    @{ Name = "warcraft_menu"; Type = "styles"; Ext = "css"; Compiled = "vcss_c" }
)

if (-not $Cs2) {
    $roots = @("C:\Program Files (x86)\Steam")
    $vdf = "C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf"
    if (Test-Path $vdf) {
        $found = Select-String -Path $vdf -Pattern '"path"\s+"(.+?)"' -AllMatches
        foreach ($line in $found) {
            foreach ($m in $line.Matches) { $roots += $m.Groups[1].Value.Replace('\\', '\') }
        }
    }
    foreach ($root in $roots) {
        $candidate = Join-Path $root "steamapps\common\Counter-Strike Global Offensive"
        if (Test-Path $candidate) { $Cs2 = $candidate; break }
    }
}

if (-not $Cs2 -or -not (Test-Path $Cs2)) {
    Write-Output "CS2 not found. Run with -Cs2 '<path to Counter-Strike Global Offensive>'."
    exit 1
}

$compiler = Join-Path $Cs2 "game\bin\win64\resourcecompiler.exe"
if (-not (Test-Path $compiler)) {
    Write-Output "Workshop Tools are not installed: resourcecompiler.exe was not found."
    exit 1
}

$contentAddon = Join-Path $Cs2 "content\csgo_addons\$Addon"
$gameAddon = Join-Path $Cs2 "game\csgo_addons\$Addon"

foreach ($resource in $resources) {
    $dir = Join-Path $contentAddon "panorama\$($resource.Type)\custom_game"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $source = Join-Path $src "$($resource.Type)\$($resource.Name).$($resource.Ext)"
    $target = Join-Path $dir "$($resource.Name).$($resource.Ext)"
    Copy-Item $source $target -Force
    & $compiler -i $target -r
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $target" }
}

foreach ($resource in $resources) {
    $output = Join-Path $gameAddon "panorama\$($resource.Type)\custom_game\$($resource.Name).$($resource.Compiled)"
    if (-not (Test-Path $output)) { throw "Compilation finished without expected file: $output" }
}

Write-Output ""
Write-Output "CS2Warcraft UI addon compiled successfully."
Write-Output "Addon source:   $contentAddon"
Write-Output "Addon compiled: $gameAddon"

if (-not $NoLocalInstall) {
    foreach ($resource in $resources) {
        $clientDir = Join-Path $Cs2 "game\csgo\panorama\$($resource.Type)\custom_game"
        New-Item -ItemType Directory -Force -Path $clientDir | Out-Null
        $output = Join-Path $gameAddon "panorama\$($resource.Type)\custom_game\$($resource.Name).$($resource.Compiled)"
        Copy-Item $output (Join-Path $clientDir "$($resource.Name).$($resource.Compiled)") -Force
    }
    Write-Output "Local client UI copies updated. Restart CS2 before testing."
}
