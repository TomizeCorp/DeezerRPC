[CmdletBinding()]
param(
    [string]$AndroidSdkDirectory = $env:ANDROID_SDK_ROOT,
    [string]$JavaSdkDirectory = $env:JAVA_HOME,
    [switch]$DetectorOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src\DeezerRpc.Android\DeezerRpc.Android.csproj'
$nativeRoot = Join-Path $projectRoot 'src\DeezerRpc.Android\native\prebuilt'
$output = Join-Path $projectRoot 'artifacts\android'

if ([string]::IsNullOrWhiteSpace($AndroidSdkDirectory)) {
    throw 'AndroidSdkDirectory ou ANDROID_SDK_ROOT est requis.'
}
if ([string]::IsNullOrWhiteSpace($JavaSdkDirectory)) {
    throw 'JavaSdkDirectory ou JAVA_HOME est requis.'
}
if (-not $DetectorOnly -and -not (Test-Path -LiteralPath $nativeRoot)) {
    throw 'Le pont Discord est absent. Exécutez build-android-native.ps1 ou utilisez -DetectorOnly.'
}

dotnet build $project `
    --configuration Release `
    -p:AndroidSdkDirectory=$AndroidSdkDirectory `
    -p:JavaSdkDirectory=$JavaSdkDirectory
if ($LASTEXITCODE -ne 0) { throw 'Compilation Android échouée.' }

New-Item -ItemType Directory -Force -Path $output | Out-Null
$sourceApk = Join-Path $projectRoot 'src\DeezerRpc.Android\bin\Release\net8.0-android34.0\com.tomize.deezerrpc-Signed.apk'
$apkName = if ($DetectorOnly) { 'DeezerRPC-detector-only.apk' } else { 'DeezerRPC.apk' }
Copy-Item -LiteralPath $sourceApk -Destination (Join-Path $output $apkName) -Force
Write-Host "APK créé dans $output"
