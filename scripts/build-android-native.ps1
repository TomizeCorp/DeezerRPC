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
$prebuiltRoot = Join-Path $nativeRoot 'prebuilt-full'
$toolchain = Join-Path $AndroidNdkDirectory 'build\cmake\android.toolchain.cmake'
$abis = @('arm64-v8a', 'armeabi-v7a', 'x86_64', 'x86')

if (-not (Test-Path -LiteralPath $toolchain)) {
    throw "Android NDK invalide : $toolchain est introuvable."
}
if (-not (Test-Path -LiteralPath (Join-Path $DiscordSdkDirectory 'include\discordpp.h'))) {
    throw 'Archive Discord Social SDK invalide : include\discordpp.h est introuvable.'
}

foreach ($abi in $abis) {
    $buildDirectory = Join-Path $nativeRoot "build-ninja\$abi"
    $outputDirectory = Join-Path $prebuiltRoot $abi
    New-Item -ItemType Directory -Force -Path $buildDirectory, $outputDirectory | Out-Null

    & $CMakePath `
        -G Ninja `
        -S $nativeRoot `
        -B $buildDirectory `
        "-DANDROID_ABI=$abi" `
        "-DANDROID_PLATFORM=android-24" `
        "-DANDROID_NDK:PATH=$AndroidNdkDirectory" `
        "-DCMAKE_TOOLCHAIN_FILE:FILEPATH=$toolchain" `
        "-DDISCORD_SDK_ROOT:PATH=$DiscordSdkDirectory" `
        "-DCMAKE_MAKE_PROGRAM:FILEPATH=$(Join-Path (Split-Path -Parent $CMakePath) 'ninja.exe')" `
        "-DCMAKE_BUILD_TYPE=Release"
    if ($LASTEXITCODE -ne 0) { throw "Configuration CMake échouée pour $abi." }

    & $CMakePath --build $buildDirectory --config Release
    if ($LASTEXITCODE -ne 0) { throw "Compilation CMake échouée pour $abi." }

    Copy-Item -LiteralPath (Join-Path $buildDirectory 'libdeezerrpc_discord_bridge.so') -Destination $outputDirectory -Force
}

Write-Host "Pont Discord Android généré dans $prebuiltRoot"
