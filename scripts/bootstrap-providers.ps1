[CmdletBinding()]
param(
    [string] $Destination = (Join-Path $env:LOCALAPPDATA "SpeedtestDashboard\development\providers")
)

$ErrorActionPreference = "Stop"
$librespeedVersion = "1.0.13"
$fastVersion = "0.3.5"

switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
    "X64" {
        $librespeedArch = "amd64"
        $librespeedSha256 = "0d10a38858a05998fe1af070ca3c98e6dc038c0883b22c18918b33c855789f2a"
    }
    "Arm64" {
        $librespeedArch = "arm64"
        $librespeedSha256 = "9bd0cb758d46d2f5fb4960949a0db4825a0bbc06cb49b7aab64c122f4720fbe1"
        Write-Warning "FAST.com provides only an x64 Windows binary; Windows x64 emulation is required."
    }
    default {
        throw "Unsupported architecture: $([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture)"
    }
}

$workDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "speedtest-dashboard-providers-$([guid]::NewGuid().ToString('N'))"
$librespeedArchive = Join-Path $workDirectory "librespeed.zip"
$fastArchive = Join-Path $workDirectory "fast.zip"

try {
    New-Item -ItemType Directory -Path $workDirectory | Out-Null
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null

    Invoke-WebRequest -Uri "https://github.com/librespeed/speedtest-cli/releases/download/v$librespeedVersion/librespeed-cli_${librespeedVersion}_windows_$librespeedArch.zip" -OutFile $librespeedArchive
    if ((Get-FileHash -Algorithm SHA256 $librespeedArchive).Hash -ne $librespeedSha256) {
        throw "LibreSpeed archive checksum verification failed."
    }

    Invoke-WebRequest -Uri "https://github.com/mikkelam/fast-cli/releases/download/v$fastVersion/fast-cli-x86_64-windows.zip" -OutFile $fastArchive
    if ((Get-FileHash -Algorithm SHA256 $fastArchive).Hash -ne "e998a71ee21e0a7e1b8c5d9bc203c0ef180b7cc46265e8c663af111edf121137") {
        throw "FAST.com archive checksum verification failed."
    }

    $librespeedDirectory = Join-Path $workDirectory "librespeed"
    $fastDirectory = Join-Path $workDirectory "fast"
    Expand-Archive -Path $librespeedArchive -DestinationPath $librespeedDirectory
    Expand-Archive -Path $fastArchive -DestinationPath $fastDirectory

    Copy-Item (Join-Path $librespeedDirectory "librespeed-cli.exe") (Join-Path $Destination "librespeed-cli.exe")
    Copy-Item (Join-Path $librespeedDirectory "LICENSE") (Join-Path $Destination "LICENSE.librespeed-cli")
    Copy-Item (Join-Path $fastDirectory "fast-cli.exe") (Join-Path $Destination "fast-cli.exe")
    Copy-Item (Join-Path $fastDirectory "LICENSE") (Join-Path $Destination "LICENSE.fast-cli")

    Write-Host "Bootstrapped LibreSpeed $librespeedVersion and FAST.com $fastVersion providers in $Destination"
}
finally {
    if (Test-Path $workDirectory) {
        Remove-Item -Recurse -Force $workDirectory
    }
}
