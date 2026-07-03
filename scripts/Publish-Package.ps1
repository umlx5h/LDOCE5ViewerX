param(
    [Parameter(Mandatory = $true)]
    [string]$Runtime,

    [Parameter(Mandatory = $true)]
    [string]$ArchiveName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

function Get-SevenZipCommand {
    $command = Get-Command 7z -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw "7-Zip was not found. Install 7-Zip or p7zip and ensure the 7z command is available on PATH."
    }

    return $command.Source
}

function Get-AppImageName([string]$Name, [string]$RuntimeIdentifier) {
    $leaf = [System.IO.Path]::GetFileName($Name)
    if ($leaf.EndsWith(".7z", [StringComparison]::OrdinalIgnoreCase)) {
        $leaf = $leaf.Substring(0, $leaf.Length - 3)
    }

    if (-not $leaf.EndsWith(".AppImage", [StringComparison]::OrdinalIgnoreCase)) {
        return "LDOCE5ViewerX-$RuntimeIdentifier.AppImage"
    }

    return $leaf
}

$outputDirectory = (Get-Location).Path
$workDirectory = Join-Path (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..")).Path "artifacts") "package"
$publishDirectory = Join-Path $workDirectory "publish-$Runtime"
$packageDirectory = Join-Path $workDirectory "package-$Runtime"

New-Item -ItemType Directory -Force -Path $outputDirectory, $workDirectory | Out-Null

& (Join-Path $PSScriptRoot "Publish-App.ps1") `
    -Runtime $Runtime `
    -OutputDirectory $publishDirectory

$sourceDirectory = $publishDirectory

if ($IsLinux) {
    $appImageName = Get-AppImageName $ArchiveName $Runtime
    & (Join-Path $PSScriptRoot "New-LinuxAppImage.ps1") `
        -PublishDirectory $publishDirectory `
        -PackageDirectory $packageDirectory `
        -RuntimeIdentifier $Runtime `
        -AppImageName $appImageName

    $sourceDirectory = $packageDirectory
}
elseif ($IsMacOS) {
    & (Join-Path $PSScriptRoot "New-MacAppBundle.ps1") `
        -PublishDirectory $publishDirectory `
        -PackageDirectory $packageDirectory

    $sourceDirectory = $packageDirectory
}

$archivePath = Join-Path $outputDirectory $ArchiveName
if (Test-Path $archivePath) {
    Remove-Item -Path $archivePath -Force
}

if ($archivePath.EndsWith(".AppImage", [StringComparison]::OrdinalIgnoreCase)) {
    $appImage = Get-ChildItem -Path $sourceDirectory -Filter "*.AppImage" -File | Select-Object -First 1
    if ($null -eq $appImage) {
        throw "No AppImage was produced in $sourceDirectory."
    }

    Copy-Item -Path $appImage.FullName -Destination $archivePath -Force
    Write-Host "Created package at $archivePath"
    return
}

$sevenZip = Get-SevenZipCommand
Push-Location $sourceDirectory
try {
    & $sevenZip a -t7z -mx=9 -mmt=4 $archivePath "*"
}
finally {
    Pop-Location
}

Write-Host "Created package at $archivePath"
