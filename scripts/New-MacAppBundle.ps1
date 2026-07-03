param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,

    [Parameter(Mandatory = $true)]
    [string]$PackageDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

if (-not $IsMacOS) {
    throw "macOS app bundle packaging must run on macOS."
}

$version = if ([string]::IsNullOrWhiteSpace($env:GITHUB_REF_NAME)) { "0.0.0" } else { $env:GITHUB_REF_NAME.TrimStart("v", "V") }
$buildNumber = if ([string]::IsNullOrWhiteSpace($env:GITHUB_RUN_NUMBER)) { "0" } else { $env:GITHUB_RUN_NUMBER }
$PublishDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PublishDirectory)
$PackageDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PackageDirectory)

$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$app = Join-Path $PackageDirectory "LDOCE5 Viewer X.app"
$contents = Join-Path $app "Contents"
$macos = Join-Path $contents "MacOS"
$resources = Join-Path $contents "Resources"
$iconSource = Join-Path $workspaceRoot "LDOCE5ViewerX/Assets/LDOCE5ViewerX.icns"

if (-not (Test-Path $iconSource)) {
    throw "Pre-generated macOS icon was not found: $iconSource. Run 'just generate-icons' and commit the generated icon."
}

if (Test-Path $PackageDirectory) {
    Remove-Item -Path $PackageDirectory -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $macos, $resources | Out-Null
Copy-Item -Path (Join-Path $PublishDirectory "*") -Destination $macos -Recurse -Force
Copy-Item -Path $iconSource -Destination (Join-Path $resources "LDOCE5ViewerX.icns") -Force

$infoPlist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleIconFile</key>
    <string>LDOCE5ViewerX.icns</string>
    <key>CFBundleIdentifier</key>
    <string>com.ldoce5viewer.x</string>
    <key>CFBundleName</key>
    <string>LDOCE5ViewerX</string>
    <key>CFBundleVersion</key>
    <string>$buildNumber</string>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
    <key>CFBundleExecutable</key>
    <string>LDOCE5ViewerX</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>$version</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
"@

Set-Content -Path (Join-Path $contents "Info.plist") -Value $infoPlist -Encoding utf8NoBOM
& chmod +x (Join-Path $macos "LDOCE5ViewerX")

Write-Host "Created macOS app bundle at $app"
