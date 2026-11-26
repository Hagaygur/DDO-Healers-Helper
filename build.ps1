param(
    [string]$DungeonHelperDir = (Join-Path $env:APPDATA 'Dungeon Helper'),
    [string]$DungeonHelper5Dir = $env:DH5_SDK_DIR,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.4.0',
    [string]$Dh5KeyFile = $env:DH_AUTHOR_KEY_FILE,
    [switch]$AllowUnsignedDh5,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install the .NET 10 SDK to rebuild (and .NET 8 runtime for tests). The prebuilt plugin does not need the SDK.'
}
$sdkVersion = dotnet --version
if ($LASTEXITCODE -ne 0 -or $sdkVersion -notmatch '^10\.') {
    throw 'This source tree requires a stable .NET 10 SDK selected by global.json.'
}
if (-not (Test-Path (Join-Path $DungeonHelperDir 'VoK.Sdk.dll'))) {
    throw 'DH 4 VoK.Sdk.dll was not found. Pass -DungeonHelperDir with the Dungeon Helper 4 installation folder.'
}
if ([string]::IsNullOrWhiteSpace($DungeonHelper5Dir)) {
    $DungeonHelper5Dir = Join-Path $PSScriptRoot '.sdk-cache\dh5'
}
if (-not (Test-Path (Join-Path $DungeonHelper5Dir 'VoK.Sdk.dll'))) {
    & (Join-Path $PSScriptRoot 'ci\Get-Sdk.ps1') -Target dh5 -Destination $DungeonHelper5Dir
    if ($LASTEXITCODE -ne 0) { throw 'DH 5 SDK acquisition failed.' }
}
if (-not (Test-Path (Join-Path $DungeonHelper5Dir 'VoK.Sdk.dll'))) {
    throw 'DH 5 VoK.Sdk.dll (5.3.0.0) was not found. Set DH5_SDK_DIR or pass -DungeonHelper5Dir.'
}

# Stamp every version-bearing file together; preserve LF and UTF-8 without BOM and avoid dirtying unchanged files.
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$assemblyInfo = Join-Path $PSScriptRoot 'src/AssemblyInfo.cs'
$origAsm = [System.IO.File]::ReadAllText($assemblyInfo)
$nextAsm = $origAsm -replace 'AssemblyVersion\("[^"]+"\)', "AssemblyVersion(`"$Version.0`")"
$nextAsm = $nextAsm -replace 'AssemblyFileVersion\("[^"]+"\)', "AssemblyFileVersion(`"$Version.0`")"
if ($nextAsm -ne $origAsm) {
    [System.IO.File]::WriteAllText($assemblyInfo, $nextAsm, $utf8NoBom)
}
$project = Join-Path $PSScriptRoot 'src\HealersHelper.csproj'
$origCsproj = [System.IO.File]::ReadAllText($project)
$nextCsproj = $origCsproj -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
if ($nextCsproj -ne $origCsproj) {
    [System.IO.File]::WriteAllText($project, $nextCsproj, $utf8NoBom)
}
$dh5Project = Join-Path $PSScriptRoot 'src-dh5\HealersHelper.Dh5.csproj'
$origDh5Csproj = [System.IO.File]::ReadAllText($dh5Project)
$nextDh5Csproj = $origDh5Csproj -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
if ($nextDh5Csproj -ne $origDh5Csproj) {
    [System.IO.File]::WriteAllText($dh5Project, $nextDh5Csproj, $utf8NoBom)
}
$metadataPath = Join-Path $PSScriptRoot 'src/VoK.HealersHelper.metadata'
$origMeta = [System.IO.File]::ReadAllText($metadataPath)
$metadata = $origMeta | ConvertFrom-Json
if ($metadata.Version -ne "$Version.0") {
    $metadata.Version = "$Version.0"
    $nextMeta = ($metadata | ConvertTo-Json) -replace "`r`n", "`n"
    if (-not $nextMeta.EndsWith("`n")) { $nextMeta += "`n" }
    [System.IO.File]::WriteAllText($metadataPath, $nextMeta, $utf8NoBom)
}

dotnet build $project -c Release "-p:DungeonHelperDir=$DungeonHelperDir" "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw 'DH 4 plugin build failed.' }

$output = Join-Path $PSScriptRoot 'src/bin/Release/net8.0-windows'
$builtDh4Zip = Join-Path $output "HealersHelper-$Version-dh4-plugin.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($builtDh4Zip)
try {
    $actualEntries = @($archive.Entries | ForEach-Object { $_.FullName })
} finally {
    $archive.Dispose()
}
$expectedEntries = @(
    'plugins/HealersHelper/VoK.HealersHelper.dll',
    'plugins/HealersHelper/VoK.HealersHelper.deps.json',
    'plugins/HealersHelper/VoK.HealersHelper.metadata'
)
if (($actualEntries.Count -ne $expectedEntries.Count) -or ($actualEntries[0] -ne $expectedEntries[0]) -or ($actualEntries[1] -ne $expectedEntries[1]) -or ($actualEntries[2] -ne $expectedEntries[2])) {
    throw "DH 4 installer ZIP entries mismatch: $($actualEntries -join ', ')"
}

dotnet build $dh5Project -c Release -p:Platform=x64 "-p:DungeonHelper5Dir=$DungeonHelper5Dir" "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw 'DH 5 plugin build failed.' }

$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$dh4DistZip = Join-Path $dist "HealersHelper-$Version-dh4-plugin.zip"
Copy-Item $builtDh4Zip $dh4DistZip -Force

$dh5OutDir = Join-Path $PSScriptRoot 'src-dh5/bin/x64/Release/net10.0'
$dh5DistZip = Join-Path $dist "HealersHelper-$Version-dh5-plugin.zip"
$signArgs = @(
    (Join-Path $PSScriptRoot 'ci/sign_dh5.py'),
    '--dll', (Join-Path $dh5OutDir 'VoK.HealersHelper.dll'),
    '--deps', (Join-Path $dh5OutDir 'VoK.HealersHelper.deps.json'),
    '--version', $Version,
    '--output', $dh5DistZip
)
if (-not [string]::IsNullOrWhiteSpace($Dh5KeyFile)) {
    $signArgs += @('--key-file', $Dh5KeyFile)
}
if ($AllowUnsignedDh5) {
    $signArgs += '--allow-unsigned'
}
$pythonCmd = if (Get-Command py -ErrorAction SilentlyContinue) { 'py' } else { 'python' }
if ($pythonCmd -eq 'py') {
    & py -3 @signArgs
} else {
    & python @signArgs
}
if ($LASTEXITCODE -ne 0) { throw 'DH 5 signing/packaging failed.' }

if (-not $SkipTests) {
    dotnet run --project (Join-Path $PSScriptRoot 'tests\HealersHelper.Tests.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw "DDO Healer's Helper tests failed." }
}

Write-Host "Built DH 4: $dh4DistZip (Install via DH 4 Settings > Add plugin from zip file)"
Write-Host "Built DH 5: $dh5DistZip (Install via DH 5 Beta Plugins > Install from zip)"
