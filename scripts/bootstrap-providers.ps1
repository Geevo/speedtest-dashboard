<#
LibreSpeed and FAST.com are installed by default because verified prebuilt binaries are
available for every supported debug platform. M-Lab is opt-in because it must be built
from pinned source with a local Go toolchain. Ookla is opt-in because its CLI is
proprietary: downloading it requires explicit acknowledgement of both the licence and
GDPR/privacy terms, and runtime acceptance remains a separate explicit configuration.
#>
[CmdletBinding()]
param(
    [string] $Destination = (Join-Path $env:LOCALAPPDATA "SpeedtestDashboard\development\providers"),
    [switch] $IncludeMLab,
    [switch] $IncludeOokla,
    [switch] $AcceptOoklaLicense,
    [switch] $AcceptOoklaGdpr
)

$ErrorActionPreference = "Stop"
$librespeedVersion = "1.0.13"
$fastVersion = "0.3.5"
$mlabVersion = "0.10.1"
$mlabCommit = "4a5f6325d1d586ab38afb84566a5781b5d6c3d9a"
$mlabSourceSha256 = "6aae19abb130d53452ceeef2452ece3a54e653df745e02037726ed9742551ab6"
$ooklaVersion = "1.2.0.84"

if ($IncludeOokla -and (-not $AcceptOoklaLicense -or -not $AcceptOoklaGdpr)) {
    throw "Ookla bootstrap requires -AcceptOoklaLicense and -AcceptOoklaGdpr after reviewing https://www.speedtest.net/about/eula, /terms, and /privacy."
}

if (-not $IncludeOokla -and ($AcceptOoklaLicense -or $AcceptOoklaGdpr)) {
    throw "Ookla acceptance options require -IncludeOokla."
}

switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
    "X64" {
        $goArch = "amd64"
        $librespeedArch = "amd64"
        $librespeedSha256 = "0d10a38858a05998fe1af070ca3c98e6dc038c0883b22c18918b33c855789f2a"
    }
    "Arm64" {
        $goArch = "arm64"
        $librespeedArch = "arm64"
        $librespeedSha256 = "9bd0cb758d46d2f5fb4960949a0db4825a0bbc06cb49b7aab64c122f4720fbe1"
        Write-Warning "FAST.com and Ookla provide only x64 Windows binaries; Windows x64 emulation is required."
    }
    default {
        throw "Unsupported architecture: $([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture)"
    }
}

if ($IncludeMLab -and -not (Get-Command go -ErrorAction SilentlyContinue)) {
    throw "M-Lab bootstrap requires Go on PATH."
}

$scriptDirectory = Split-Path -Parent $PSCommandPath
$repoRoot = Split-Path -Parent $scriptDirectory
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

    $providers = [System.Collections.Generic.List[string]]::new()
    $providers.Add("LibreSpeed $librespeedVersion")
    $providers.Add("FAST.com $fastVersion")

    if ($IncludeMLab) {
        $mlabArchive = Join-Path $workDirectory "mlab-source.tar.gz"
        $mlabSourceDirectory = Join-Path $workDirectory "mlab-source"
        New-Item -ItemType Directory -Path $mlabSourceDirectory | Out-Null
        Invoke-WebRequest -Uri "https://github.com/m-lab/ndt7-client-go/archive/$mlabCommit.tar.gz" -OutFile $mlabArchive
        if ((Get-FileHash -Algorithm SHA256 $mlabArchive).Hash -ne $mlabSourceSha256) {
            throw "M-Lab source archive checksum verification failed."
        }

        & tar -xzf $mlabArchive --strip-components=1 -C $mlabSourceDirectory
        if ($LASTEXITCODE -ne 0) {
            throw "M-Lab source archive extraction failed."
        }

        $previousCgoEnabled = $env:CGO_ENABLED
        $previousGoOs = $env:GOOS
        $previousGoArch = $env:GOARCH
        try {
            $env:CGO_ENABLED = "0"
            $env:GOOS = "windows"
            $env:GOARCH = $goArch
            Push-Location $mlabSourceDirectory
            try {
                & go build -trimpath -buildvcs=false "-ldflags=-s -w -X main.ClientVersion=$mlabVersion" -o (Join-Path $Destination "mlab-ndt7-client.exe") ./cmd/ndt7-client
                if ($LASTEXITCODE -ne 0) {
                    throw "M-Lab client build failed."
                }
            }
            finally {
                Pop-Location
            }
        }
        finally {
            $env:CGO_ENABLED = $previousCgoEnabled
            $env:GOOS = $previousGoOs
            $env:GOARCH = $previousGoArch
        }

        Copy-Item (Join-Path $mlabSourceDirectory "LICENSE") (Join-Path $Destination "LICENSE.mlab-ndt7-client")
        $providers.Add("M-Lab $mlabVersion")
    }

    if ($IncludeOokla) {
        $ooklaArchive = Join-Path $workDirectory "ookla.zip"
        $ooklaDirectory = Join-Path $workDirectory "ookla"
        Invoke-WebRequest -Uri "https://install.speedtest.net/app/cli/ookla-speedtest-1.2.0-win64.zip" -OutFile $ooklaArchive
        if ((Get-FileHash -Algorithm SHA256 $ooklaArchive).Hash -ne "13e3d888b845d301a556419e31f14ab9bff57e3f06089ef2fd3bdc9ba6841efa") {
            throw "Ookla archive checksum verification failed."
        }

        Expand-Archive -Path $ooklaArchive -DestinationPath $ooklaDirectory
        Copy-Item (Join-Path $ooklaDirectory "speedtest.exe") (Join-Path $Destination "speedtest.exe")
        Copy-Item (Join-Path $ooklaDirectory "speedtest.md") (Join-Path $Destination "README.ookla-speedtest.md")
        Copy-Item (Join-Path $repoRoot "packaging\providers\OOKLA_NOTICE.md") (Join-Path $Destination "OOKLA_NOTICE.md")
        $providers.Add("Ookla $ooklaVersion")
    }

    Write-Host "Bootstrapped $($providers -join ', ') providers in $Destination"
    if ($IncludeOokla) {
        Write-Host "Ookla runtime acceptance is not stored by this script; set Providers__Ookla__AcceptLicense=true and Providers__Ookla__AcceptGdpr=true when launching the app."
    }
}
finally {
    if (Test-Path $workDirectory) {
        Remove-Item -Recurse -Force $workDirectory
    }
}
