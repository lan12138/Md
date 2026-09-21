# -----------------------------------------------------------------------------
#  MD - 跨平台 Markdown 阅读器
#  文件：build/publish-android.ps1
#  说明：构建 Android 安装包（默认 APK，便于侧载）。
#
#  ⚠ 当前 MD.csproj 是**单目标**（只有 net10.0-windows），Android 目标已停用，
#    直接跑本脚本会报「找不到 net10.0-android 目标框架」。恢复步骤：
#      1) MD.csproj 里改成复数的多目标：
#         <TargetFrameworks>net10.0-windows10.0.19041.0;net10.0-android</TargetFrameworks>
#      2) dotnet workload install maui-android
#      3) 准备 JDK 17~21 与含 platforms/android-36 的 Android SDK
#    详见 docs/PLATFORMS.md 第四节。
#
#  前置：
#    dotnet workload install maui-android
#    Android SDK（设置 ANDROID_HOME，或装在默认位置，或让 MSBuild 自动补装）
#    JDK 17 ~ 21（通过 JAVA_HOME 指定）
#
#  用法：
#    powershell -ExecutionPolicy Bypass -File build/publish-android.ps1
#    powershell -ExecutionPolicy Bypass -File build/publish-android.ps1 -Format aab
#    powershell -ExecutionPolicy Bypass -File build/publish-android.ps1 -Sign `
#        -Keystore C:\keys\md.keystore -KeyAlias md -StorePass *** -KeyPass ***
#
#  作者：MD
#  创建：2026-09-21
#  修改：2026-09-21  初版
#  修改：2026-09-21  注释改中文；探测到的 SDK 路径改为显式传给 MSBuild
#                   （本机 SDK 不在默认位置，只报警告会导致构建仍然失败）
#  修改：2026-09-21  补充「Android 目标已停用，需先恢复」的说明
# -----------------------------------------------------------------------------
param(
    [ValidateSet('apk', 'aab')]
    [string]$Format = 'apk',

    [switch]$Sign,
    [string]$Keystore,
    [string]$KeyAlias,
    [string]$StorePass,
    [string]$KeyPass,
    [string]$Output,
    [string]$AndroidSdk
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

# Project path works in both layouts (no need to keep two copies of this script):
#   standard layout (local dev)  : src/MD/MD.csproj
#   flat layout     (repo root)  : MD.csproj
$project = Join-Path $root 'src\MD\MD.csproj'
if (-not (Test-Path $project)) { $project = Join-Path $root 'MD.csproj' }

$tf = 'net10.0-android'

if (-not $Output) {
    $Output = Join-Path $root "dist\android-$Format"
}

# ---- 定位 Android SDK -------------------------------------------------------
# 依次尝试：显式参数 -> ANDROID_HOME -> ANDROID_SDK_ROOT -> 常见安装位置
if (-not $AndroidSdk) { $AndroidSdk = $env:ANDROID_HOME }
if (-not $AndroidSdk) { $AndroidSdk = $env:ANDROID_SDK_ROOT }
if (-not $AndroidSdk) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Android\Sdk'),
        'C:\Program Files (x86)\Android\android-sdk',
        'C:\Android\Sdk'
    )
    foreach ($c in $candidates) {
        if (Test-Path (Join-Path $c 'platform-tools')) { $AndroidSdk = $c; break }
    }
}

# ---- 校验 JDK ---------------------------------------------------------------
$javaHome = $env:JAVA_HOME
if (-not $javaHome -or -not (Test-Path $javaHome)) {
    Write-Warning '未找到 JAVA_HOME。.NET Android 需要 JDK 17~21，请设置后重试。'
}

Write-Host "项目      : $project"
Write-Host "格式      : $Format"
Write-Host "输出      : $Output"
Write-Host "AndroidSDK: $(if ($AndroidSdk) { $AndroidSdk } else { '(未找到，将由 MSBuild 自行探测)' })"
Write-Host "JAVA_HOME : $(if ($javaHome) { $javaHome } else { '(未设置)' })"
Write-Host ''

$publishArgs = @(
    'publish', $project,
    '-c', 'Release',
    '-f', $tf,
    '-o', $Output,
    "-p:AndroidPackageFormat=$Format"
)

# 显式传 SDK 路径：MSBuild 只认自己探测到的那几个位置，探测不到就会报 XA5207
if ($AndroidSdk) {
    $publishArgs += "-p:AndroidSdkDirectory=$AndroidSdk"
}

if ($Sign) {
    $publishArgs += '-p:AndroidKeyStore=true'
    if ($Keystore)  { $publishArgs += "-p:AndroidSigningKeyStore=$Keystore" }
    if ($KeyAlias)  { $publishArgs += "-p:AndroidSigningKeyAlias=$KeyAlias" }
    if ($StorePass) { $publishArgs += "-p:AndroidSigningStorePass=$StorePass" }
    if ($KeyPass)   { $publishArgs += "-p:AndroidSigningKeyPass=$KeyPass" }
}

& dotnet @publishArgs

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败，退出码 $LASTEXITCODE"
}

$found = Get-ChildItem -Path $Output -Filter "*.$Format" -Recurse -ErrorAction SilentlyContinue
if ($found) {
    foreach ($f in $found) {
        $size = [math]::Round($f.Length / 1MB, 1)
        Write-Host "产物：$($f.FullName)（$size MB）"
    }
}
else {
    Write-Host "发布完成。输出目录：$Output"
}
