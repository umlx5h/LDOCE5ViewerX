Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Utf8Bom = [System.Text.Encoding]::UTF8.GetPreamble()
$StrictUtf8 = [System.Text.UTF8Encoding]::new($false, $true)

$CharsetUtf8BomExtensions = @(
    '.axaml',
    '.cs',
    '.csproj',
    '.fsproj',
    '.proj',
    '.slnx',
    '.vbproj',
    '.xaml'
)

$FinalNewlineExtensions = @(
    '.axaml',
    '.cs',
    '.xaml'
)

function Get-RepositoryFile {
    $files = & git -c core.quotePath=false -C $Root ls-files --cached --others --exclude-standard

    foreach ($file in $files) {
        if ([string]::IsNullOrWhiteSpace($file)) {
            continue
        }

        $path = Join-Path $Root $file

        if ([System.IO.File]::Exists($path)) {
            Get-Item -LiteralPath $path -Force
        }
    }
}

function Test-StartsWithUtf8Bom {
    param(
        [byte[]] $Bytes
    )

    return $Bytes.Length -ge $Utf8Bom.Length `
        -and $Bytes[0] -eq $Utf8Bom[0] `
        -and $Bytes[1] -eq $Utf8Bom[1] `
        -and $Bytes[2] -eq $Utf8Bom[2]
}

function Add-Utf8Bom {
    param(
        [System.IO.FileInfo] $File
    )

    [byte[]] $bytes = [System.IO.File]::ReadAllBytes($File.FullName)

    if (Test-StartsWithUtf8Bom -Bytes $bytes) {
        return $false
    }

    [void] $StrictUtf8.GetString($bytes)

    [byte[]] $updatedBytes = [byte[]]::new($bytes.Length + $Utf8Bom.Length)
    [System.Buffer]::BlockCopy($Utf8Bom, 0, $updatedBytes, 0, $Utf8Bom.Length)
    [System.Buffer]::BlockCopy($bytes, 0, $updatedBytes, $Utf8Bom.Length, $bytes.Length)
    [System.IO.File]::WriteAllBytes($File.FullName, $updatedBytes)

    return $true
}

function Add-FinalNewline {
    param(
        [System.IO.FileInfo] $File
    )

    [byte[]] $bytes = [System.IO.File]::ReadAllBytes($File.FullName)

    if ($bytes.Length -eq 0 -or $bytes[-1] -eq 0x0A) {
        return $false
    }

    [byte[]] $updatedBytes = [byte[]]::new($bytes.Length + 1)
    [System.Buffer]::BlockCopy($bytes, 0, $updatedBytes, 0, $bytes.Length)
    $updatedBytes[-1] = 0x0A
    [System.IO.File]::WriteAllBytes($File.FullName, $updatedBytes)

    return $true
}

$bomCount = 0
$newlineCount = 0

foreach ($file in Get-RepositoryFile) {
    $extension = $file.Extension.ToLowerInvariant()

    if ($CharsetUtf8BomExtensions.Contains($extension) -and (Add-Utf8Bom -File $file)) {
        $bomCount += 1
    }

    if ($FinalNewlineExtensions.Contains($extension) -and (Add-FinalNewline -File $file)) {
        $newlineCount += 1
    }
}

Write-Host "Applied UTF-8 BOM fixes: $bomCount"
Write-Host "Applied final newline fixes: $newlineCount"
