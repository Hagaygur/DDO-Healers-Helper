param(
    [string]$DungeonHelperDir = (Join-Path $env:APPDATA 'Dungeon Helper'),
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.0.0'
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
    throw 'VoK.Sdk.dll was not found. Pass -DungeonHelperDir with the Dungeon Helper installation folder.'
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
$metadataPath = Join-Path $PSScriptRoot 'src/VoK.HealersHelper.metadata'
$origMeta = [System.IO.File]::ReadAllText($metadataPath)
$metadata = $origMeta | ConvertFrom-Json
if ($metadata.Version -ne "$Version.0") {
    $metadata.Version = "$Version.0"
    $nextMeta = ($metadata | ConvertTo-Json) -replace "`r`n", "`n"
    if (-not $nextMeta.EndsWith("`n")) { $nextMeta += "`n" }
    [System.IO.File]::WriteAllText($metadataPath, $nextMeta, $utf8NoBom)
}
$project = Join-Path $PSScriptRoot 'src\HealersHelper.csproj'
dotnet build $project -c Release "-p:DungeonHelperDir=$DungeonHelperDir" "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
$output = Join-Path $PSScriptRoot 'src/bin/Release/net8.0-windows'
$builtZip = Join-Path $output "HealersHelper-$Version-plugin.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($builtZip)
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
    throw "Installer ZIP entries mismatch: $($actualEntries -join ', ')"
}
dotnet run --project (Join-Path $PSScriptRoot 'tests\HealersHelper.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw "DDO Healer's Helper tests failed." }
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$zip = Join-Path $dist "HealersHelper-$Version-plugin.zip"
# Normal builds already create the exact runtime-only installer.
Copy-Item $builtZip $zip -Force
Write-Host "Built and tested: $zip"
Write-Host 'Install it through Dungeon Helper Settings > Add plugin from zip file.'
