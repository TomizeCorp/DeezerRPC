[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$DiscordSdkDirectory,
    [Parameter(Mandatory)]
    [string]$AndroidNdkDirectory,
    [Parameter(Mandatory)]
    [string]$CMakePath
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$nativeRoot = Join-Path $projectRoot 'src\DeezerRpc.Android\native'
$prebuiltRoot = Join-Path $nativeRoot 'prebuilt'
$toolchain = Join-Path $AndroidNdkDirectory 'build\cmake\android.toolchain.cmake'
$abis = @('arm64-v8a', 'armeabi-v7a', 'x86_64', 'x86')

if (-not (Test-Path -LiteralPath $toolchain)) {
    throw "Android NDK invalide : $toolchain est introuvable."
}
if (-not (Test-Path -LiteralPath (Join-Path $DiscordSdkDirectory 'include\discordpp.h'))) {
    throw 'Archive Discord Social SDK invalide : include\discordpp.h est introuvable.'
}

foreach ($abi in $abis) {
    $buildDirectory = Join-Path $nativeRoot "build\$abi"
    $outputDirectory = Join-Path $prebuiltRoot $abi
    New-Item -ItemType Directory -Force -Path $buildDirectory, $outputDirectory | Out-Null

    & $CMakePath `
        -S $nativeRoot `
        -B $buildDirectory `
        -DANDROID_ABI=$abi `
        -DANDROID_PLATFORM=android-24 `
        -DANDROID_NDK=$AndroidNdkDirectory `
        -DCMAKE_TOOLCHAIN_FILE=$toolchain `
        -DDISCORD_SDK_ROOT=$DiscordSdkDirectory `
        -DCMAKE_BUILD_TYPE=Release
    if ($LASTEXITCODE -ne 0) { throw "Configuration CMake échouée pour $abi." }

    & $CMakePath --build $buildDirectory --config Release
    if ($LASTEXITCODE -ne 0) { throw "Compilation CMake échouée pour $abi." }

    Copy-Item -LiteralPath (Join-Path $buildDirectory 'libdeezerrpc_discord_bridge.so') -Destination $outputDirectory -Force
    $discordLibrary = Get-ChildItem -LiteralPath $DiscordSdkDirectory -Recurse -Filter 'libdiscord_partner_sdk.so' |
        Where-Object { $_.FullName -like "*$abi*" } |
        Select-Object -First 1
    if ($null -eq $discordLibrary) { throw "Bibliothèque Discord introuvable pour $abi." }
    Copy-Item -LiteralPath $discordLibrary.FullName -Destination $outputDirectory -Force
}

Write-Host "Pont Discord Android généré dans $prebuiltRoot"
