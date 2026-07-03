param(
    [string]$SourceSvg,

    [string]$OutputDirectory,

    [string]$LinuxIconName = "LDOCE5ViewerX.png",

    [string]$MacIconName = "LDOCE5ViewerX.icns",

    [string]$WindowsIconPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($SourceSvg)) {
    $SourceSvg = Join-Path $workspaceRoot "LDOCE5ViewerX/Assets/LDOCE5ViewerX.svg"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $workspaceRoot "LDOCE5ViewerX/Assets"
}

if ([string]::IsNullOrWhiteSpace($WindowsIconPath)) {
    $WindowsIconPath = Join-Path $workspaceRoot "LDOCE5ViewerX/Assets/LDOCE5ViewerX.ico"
}

function Get-RequiredCommand([string]$Name) {
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw "$Name was not found. Install the required tool or run 'just generate-icons-docker'."
    }

    return $command.Source
}

function Get-ImageMagickCommand {
    $magick = Get-Command magick -ErrorAction SilentlyContinue
    if ($null -ne $magick) {
        return $magick.Source
    }

    $convert = Get-Command convert -ErrorAction SilentlyContinue
    if ($null -ne $convert) {
        return $convert.Source
    }

    throw "ImageMagick was not found. Install ImageMagick or run 'just generate-icons-docker'."
}

function Write-BigEndianUInt32(
    [System.IO.BinaryWriter]$Writer,
    [uint32]$Value
) {
    [byte[]]$bytes = [System.BitConverter]::GetBytes($Value)
    if ([System.BitConverter]::IsLittleEndian) {
        [System.Array]::Reverse($bytes)
    }

    $Writer.Write($bytes)
}

function New-IcnsFile(
    [array]$IconSpecs,
    [string]$OutputPath
) {
    [System.Collections.Generic.List[object]]$chunks = [System.Collections.Generic.List[object]]::new()
    [uint32]$totalLength = 8

    foreach ($spec in $IconSpecs) {
        [byte[]]$data = [System.IO.File]::ReadAllBytes($spec.Path)
        [uint32]$chunkLength = [uint32](8 + $data.Length)
        $totalLength += $chunkLength
        $chunks.Add([pscustomobject]@{
            Type = $spec.Type
            Length = $chunkLength
            Data = $data
        })
    }

    [System.IO.FileStream]$stream = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    [System.IO.BinaryWriter]$writer = [System.IO.BinaryWriter]::new($stream, [System.Text.Encoding]::ASCII, $false)
    try {
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes("icns"))
        Write-BigEndianUInt32 $writer $totalLength

        foreach ($chunk in $chunks) {
            if ($chunk.Type.Length -ne 4) {
                throw "Invalid ICNS chunk type: $($chunk.Type)"
            }

            $writer.Write([System.Text.Encoding]::ASCII.GetBytes($chunk.Type))
            Write-BigEndianUInt32 $writer $chunk.Length
            $writer.Write($chunk.Data)
        }
    }
    finally {
        $writer.Dispose()
    }
}

$SourceSvg = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($SourceSvg)
$OutputDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
$WindowsIconPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($WindowsIconPath)

if (-not (Test-Path $SourceSvg)) {
    throw "Source SVG was not found: $SourceSvg"
}

$rsvgConvert = Get-RequiredCommand "rsvg-convert"
$imageMagick = Get-ImageMagickCommand
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $WindowsIconPath) | Out-Null

$linuxIconPath = Join-Path $OutputDirectory $LinuxIconName
$macIconPath = Join-Path $OutputDirectory $MacIconName
$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "ldoce5viewerx-icons-$([System.Guid]::NewGuid().ToString("N"))"

# ic11 through ic14 are the Retina @2x entries produced by macOS iconutil.
$macIconSpecs = @(
    [pscustomobject]@{ Type = "icp4"; Size = 16 },
    [pscustomobject]@{ Type = "ic11"; Size = 32 },
    [pscustomobject]@{ Type = "icp5"; Size = 32 },
    [pscustomobject]@{ Type = "ic12"; Size = 64 },
    [pscustomobject]@{ Type = "icp6"; Size = 64 },
    [pscustomobject]@{ Type = "ic07"; Size = 128 },
    [pscustomobject]@{ Type = "ic13"; Size = 256 },
    [pscustomobject]@{ Type = "ic08"; Size = 256 },
    [pscustomobject]@{ Type = "ic14"; Size = 512 },
    [pscustomobject]@{ Type = "ic09"; Size = 512 },
    [pscustomobject]@{ Type = "ic10"; Size = 1024 }
)

try {
    New-Item -ItemType Directory -Force -Path $temporaryDirectory | Out-Null

    $renderedPngs = @{}
    foreach ($spec in $macIconSpecs) {
        if ($renderedPngs.ContainsKey($spec.Size)) {
            $spec | Add-Member -NotePropertyName Path -NotePropertyValue $renderedPngs[$spec.Size]
            continue
        }

        $pngPath = Join-Path $temporaryDirectory "icon-$($spec.Size).png"
        & $rsvgConvert -w $spec.Size -h $spec.Size -o $pngPath $SourceSvg
        $renderedPngs[$spec.Size] = $pngPath
        $spec | Add-Member -NotePropertyName Path -NotePropertyValue $pngPath
    }

    & $rsvgConvert -w 256 -h 256 -o $linuxIconPath $SourceSvg
    New-IcnsFile -IconSpecs $macIconSpecs -OutputPath $macIconPath

    & $imageMagick `
        -density 384 `
        -background none `
        $SourceSvg `
        -define "icon:auto-resize=256,128,96,72,64,48,32,24,16" `
        -compress none `
        $WindowsIconPath
}
finally {
    if (Test-Path $temporaryDirectory) {
        Remove-Item -Path $temporaryDirectory -Recurse -Force
    }
}

Write-Host "Created Linux icon at $linuxIconPath"
Write-Host "Created macOS icon at $macIconPath"
Write-Host "Created Windows icon at $WindowsIconPath"
