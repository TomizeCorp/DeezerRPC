[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src\DeezerRpc.Windows\DeezerRpc.Windows.csproj'
$output = Join-Path $projectRoot "artifacts\windows-$Runtime"
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }

dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained $selfContained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $output

Write-Host "Executable créé dans $output"
