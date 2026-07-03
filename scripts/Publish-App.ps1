param(
    [Parameter(Mandatory = $true)]
    [string]$Runtime,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $workspaceRoot "LDOCE5ViewerX/LDOCE5ViewerX.csproj"
$OutputDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)

if (Test-Path $OutputDirectory) {
    Remove-Item -Path $OutputDirectory -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

dotnet publish $projectPath `
    --configuration Release `
    --runtime $Runtime `
    --output $OutputDirectory

Get-ChildItem $OutputDirectory -Filter "*.pdb" -File |
    Where-Object BaseName -ne "LDOCE5ViewerX" |
    Remove-Item -Force

Write-Host "Published $Runtime to $OutputDirectory"
