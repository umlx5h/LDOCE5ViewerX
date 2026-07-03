param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,

    [Parameter(Mandatory = $true)]
    [string]$PackageDirectory,

    [Parameter(Mandatory = $true)]
    [string]$RuntimeIdentifier,

    [Parameter(Mandatory = $true)]
    [string]$AppImageName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

if (-not $IsLinux) {
    throw "Linux AppImage packaging must run on Linux."
}

function Get-AppImageArchitecture([string]$RuntimeIdentifier) {
    switch ($RuntimeIdentifier) {
        "linux-x64" { return "x86_64" }
        "linux-arm64" { return "aarch64" }
        default { throw "Unsupported Linux AppImage runtime identifier: $RuntimeIdentifier" }
    }
}

function Get-AppImageToolArchitecture {
    switch ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture) {
        "X64" { return "x86_64" }
        "Arm64" { return "aarch64" }
        default { throw "Unsupported AppImage build host architecture: $([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture)" }
    }
}

$PublishDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PublishDirectory)
$PackageDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PackageDirectory)
$appImageArchitecture = Get-AppImageArchitecture $RuntimeIdentifier
$appImageToolArchitecture = Get-AppImageToolArchitecture

$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectFile = [xml](Get-Content -Path (Join-Path $workspaceRoot "LDOCE5ViewerX/LDOCE5ViewerX.csproj") -Raw)
$versionNode = $projectFile.SelectSingleNode("/Project/PropertyGroup/Version")
$appVersion = if ($null -ne $versionNode) { $versionNode.InnerText } else { "1.0.0" }
$releaseDate = Get-Date -Format "yyyy-MM-dd"
$appId = "com.ldoce5viewer.x"
$iconSource = Join-Path $workspaceRoot "LDOCE5ViewerX/Assets/LDOCE5ViewerX.png"
$installDirectoryName = "ldoce5viewerx"
$workRoot = Split-Path -Parent $PackageDirectory
$appDir = Join-Path $workRoot "LDOCE5ViewerX.AppDir"
$appInstallDirectory = Join-Path $appDir "opt/$installDirectoryName"
$applicationsDirectory = Join-Path $appDir "usr/share/applications"
$iconDirectory = Join-Path $appDir "usr/share/icons/hicolor/256x256/apps"
$metainfoDirectory = Join-Path $appDir "usr/share/metainfo"
$rootDesktop = Join-Path $appDir "$appId.desktop"
$rootIcon = Join-Path $appDir "$appId.png"
$sharedIcon = Join-Path $iconDirectory "$appId.png"
$appstreamMetadata = Join-Path $metainfoDirectory "$appId.appdata.xml"

if (-not (Test-Path $iconSource)) {
    throw "Pre-generated Linux icon was not found: $iconSource. Run 'just generate-icons' and commit the generated icon."
}

if (Test-Path $PackageDirectory) {
    Remove-Item -Path $PackageDirectory -Recurse -Force
}

if (Test-Path $appDir) {
    Remove-Item -Path $appDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $PackageDirectory, $appInstallDirectory, $applicationsDirectory, $iconDirectory, $metainfoDirectory | Out-Null

Copy-Item -Path (Join-Path $PublishDirectory "*") -Destination $appInstallDirectory -Recurse -Force
& chmod +x (Join-Path $appInstallDirectory "LDOCE5ViewerX")

New-Item -ItemType SymbolicLink `
    -Path (Join-Path $appDir "AppRun") `
    -Target "opt/$installDirectoryName/LDOCE5ViewerX" |
    Out-Null

$desktopEntry = @"
[Desktop Entry]
Name=LDOCE5 Viewer X
Comment=A dictionary viewer for LDOCE5
Exec=AppRun
Icon=$appId
Terminal=false
Type=Application
Categories=Office;Dictionary;
StartupWMClass=LDOCE5ViewerX
"@

Set-Content -Path $rootDesktop -Value $desktopEntry -Encoding utf8NoBOM
Copy-Item -Path $rootDesktop -Destination (Join-Path $applicationsDirectory "$appId.desktop") -Force

$appstreamEntry = @"
<?xml version="1.0" encoding="UTF-8"?>
<component type="desktop-application">
    <id>$appId</id>
    <metadata_license>CC0-1.0</metadata_license>
    <project_license>GPL-3.0-or-later</project_license>
    <name>LDOCE5 Viewer X</name>
    <summary>A dictionary viewer for LDOCE5</summary>
    <description>
        <p>LDOCE5 Viewer X is a desktop dictionary viewer for the Longman Dictionary of Contemporary English 5th Edition.</p>
    </description>
    <url type="homepage">https://github.com/umlx5h/LDOCE5ViewerX</url>
    <launchable type="desktop-id">$appId.desktop</launchable>
    <developer id="io.github">
        <name>umlx5h</name>
    </developer>
    <releases>
        <release version="$appVersion" date="$releaseDate" />
    </releases>
    <content_rating type="oars-1.1" />
</component>
"@

Set-Content -Path $appstreamMetadata -Value $appstreamEntry -Encoding utf8NoBOM

Copy-Item -Path $iconSource -Destination $sharedIcon -Force
Copy-Item -Path $iconSource -Destination $rootIcon -Force
New-Item -ItemType SymbolicLink -Path (Join-Path $appDir ".DirIcon") -Target "$appId.png" | Out-Null

$appImageTool = Join-Path $workRoot "appimagetool-$appImageToolArchitecture.AppImage"
if (-not (Test-Path $appImageTool)) {
    & curl -L `
        -o $appImageTool `
        "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-$appImageToolArchitecture.AppImage"
}

& chmod +x $appImageTool

$previousArch = $env:ARCH
$previousExtractAndRun = $env:APPIMAGE_EXTRACT_AND_RUN
$previousAsValidateNonet = $env:AS_VALIDATE_NONET
try {
    $env:ARCH = $appImageArchitecture
    $env:APPIMAGE_EXTRACT_AND_RUN = "1"
    $env:AS_VALIDATE_NONET = "1"
    & $appImageTool $appDir (Join-Path $PackageDirectory $AppImageName)
}
finally {
    $env:ARCH = $previousArch
    $env:APPIMAGE_EXTRACT_AND_RUN = $previousExtractAndRun
    $env:AS_VALIDATE_NONET = $previousAsValidateNonet
}

& chmod +x (Join-Path $PackageDirectory $AppImageName)
Write-Host "Created AppImage at $(Join-Path $PackageDirectory $AppImageName)"
