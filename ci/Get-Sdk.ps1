param(
    [ValidateSet('all', 'dh4', 'dh5')][string]$Target = 'all',
    [string]$Destination = ''
)
$ErrorActionPreference = 'Stop'
$lock = Get-Content "$PSScriptRoot/sdk-lock.json" -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $tempBase = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { Join-Path (Split-Path $PSScriptRoot -Parent) '.sdk-cache' }
    $root = if ($env:RUNNER_TEMP) { Join-Path $tempBase 'healers-helper-sdk' } else { $tempBase }
} else {
    $root = $Destination
}
New-Item -ItemType Directory -Force $root | Out-Null

function Get-Dh4Sdk([string]$DestDir) {
    $existing = Get-ChildItem $DestDir -Recurse -Filter VoK.Sdk.dll -ErrorAction SilentlyContinue | Where-Object {
        (Get-FileHash $_.FullName -Algorithm SHA256).Hash -eq $lock.dh4.sdkSha256
    } | Select-Object -First 1
    if ($existing -and [Reflection.AssemblyName]::GetAssemblyName($existing.FullName).Version.ToString() -eq $lock.dh4.sdkVersion) {
        return $existing.DirectoryName
    }

    $work = Join-Path $DestDir '_work_dh4'
    if (Test-Path $work) { Remove-Item $work -Recurse -Force }
    New-Item -ItemType Directory -Force $work | Out-Null
    try {
        $msi = Join-Path $work 'DungeonHelper.msi'
        Invoke-WebRequest $lock.dh4.url -OutFile $msi
        if ((Get-FileHash $msi -Algorithm SHA256).Hash -ne $lock.dh4.installerSha256) {
            throw 'Official DH 4 installer changed. Verify the new installer and update ci/sdk-lock.json deliberately.'
        }
        $extract = Join-Path $work 'extracted'
        $process = Start-Process msiexec.exe -ArgumentList "/a `"$msi`" /qn TARGETDIR=`"$extract`"" -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -notin @(0, 3010)) { throw "MSI extraction failed: $($process.ExitCode)" }
        $sdk = Get-ChildItem $extract -Recurse -Filter VoK.Sdk.dll | Where-Object {
            (Get-FileHash $_.FullName -Algorithm SHA256).Hash -eq $lock.dh4.sdkSha256
        } | Select-Object -First 1
        if (-not $sdk) { throw 'Pinned DH 4 SDK DLL was not found in the installer.' }
        if ([Reflection.AssemblyName]::GetAssemblyName($sdk.FullName).Version.ToString() -ne $lock.dh4.sdkVersion) {
            throw 'Unexpected DH 4 SDK assembly version.'
        }
        New-Item -ItemType Directory -Force $DestDir | Out-Null
        $outDll = Join-Path $DestDir 'VoK.Sdk.dll'
        Copy-Item $sdk.FullName $outDll -Force
        return $DestDir
    } finally {
        if (Test-Path $work) { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

function Get-Dh5Sdk([string]$DestDir) {
    $outDll = Join-Path $DestDir 'VoK.Sdk.dll'
    if ((Test-Path $outDll) -and
        ((Get-FileHash $outDll -Algorithm SHA256).Hash -eq $lock.dh5.sdkSha256) -and
        ([Reflection.AssemblyName]::GetAssemblyName($outDll).Version.ToString() -eq $lock.dh5.sdkVersion)) {
        return $DestDir
    }

    $work = Join-Path $DestDir '_work_dh5'
    if (Test-Path $work) { Remove-Item $work -Recurse -Force }
    New-Item -ItemType Directory -Force $work | Out-Null
    try {
        $nupkg = Join-Path $work 'vok.sdk.nupkg'
        Invoke-WebRequest $lock.dh5.url -OutFile $nupkg
        if ((Get-FileHash $nupkg -Algorithm SHA256).Hash -ne $lock.dh5.packageSha256) {
            throw 'DH 5 NuGet package hash changed. Verify VoK.Sdk 5.3.0 and update ci/sdk-lock.json deliberately.'
        }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [System.IO.Compression.ZipFile]::OpenRead($nupkg)
        try {
            $entry = $archive.GetEntry($lock.dh5.sdkEntry)
            if (-not $entry) { throw "Missing DH 5 SDK entry '$($lock.dh5.sdkEntry)' in nupkg." }
            $tempDll = Join-Path $work 'VoK.Sdk.dll'
            $inStream = $entry.Open()
            try {
                $outStream = [System.IO.File]::Create($tempDll)
                try { $inStream.CopyTo($outStream) } finally { $outStream.Dispose() }
            } finally {
                $inStream.Dispose()
            }
        } finally {
            $archive.Dispose()
        }
        if ((Get-FileHash $tempDll -Algorithm SHA256).Hash -ne $lock.dh5.sdkSha256) {
            throw 'Pinned DH 5 VoK.Sdk.dll hash mismatch.'
        }
        if ([Reflection.AssemblyName]::GetAssemblyName($tempDll).Version.ToString() -ne $lock.dh5.sdkVersion) {
            throw 'Unexpected DH 5 SDK assembly version.'
        }
        New-Item -ItemType Directory -Force $DestDir | Out-Null
        Copy-Item $tempDll $outDll -Force
        return $DestDir
    } finally {
        if (Test-Path $work) { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

if ($Target -eq 'all' -or $Target -eq 'dh4') {
    $dh4Sub = if ($Target -eq 'all') { Join-Path $root 'dh4' } else { $root }
    $dh4Dir = Get-Dh4Sdk $dh4Sub
    $env:DH_SDK_DIR = $dh4Dir
    if ($env:GITHUB_ENV) { "DH_SDK_DIR=$dh4Dir" >> $env:GITHUB_ENV }
    Write-Host "Verified DH 4 SDK $($lock.dh4.sdkVersion): $dh4Dir"
}

if ($Target -eq 'all' -or $Target -eq 'dh5') {
    $dh5Sub = if ($Target -eq 'all') { Join-Path $root 'dh5' } else { $root }
    $dh5Dir = Get-Dh5Sdk $dh5Sub
    $env:DH5_SDK_DIR = $dh5Dir
    if ($env:GITHUB_ENV) { "DH5_SDK_DIR=$dh5Dir" >> $env:GITHUB_ENV }
    Write-Host "Verified DH 5 SDK $($lock.dh5.sdkVersion): $dh5Dir"
}
