# -----------------------------------------------------------------------------
#  MD - 跨平台 Markdown 阅读器
#  文件：build/publish-windows.ps1
#  说明：把 Windows 目标发布为「单文件自包含 exe」（双击即用，无需装运行时）。
#
#  用法：
#    powershell -ExecutionPolicy Bypass -File build/publish-windows.ps1
#    powershell -ExecutionPolicy Bypass -File build/publish-windows.ps1 -Mode framework
#    powershell -ExecutionPolicy Bypass -File build/publish-windows.ps1 -Runtime win-arm64
#
#  ⚠ 注意不要改成命令行 -r / --self-contained：
#    这两个是全局 MSBuild 属性，会一并作用到 Android 目标上，使 Android 去还原
#    Microsoft.NETCore.App.Runtime.Mono.win-x64 并报 NU1102。
#    Windows 的 RID / 自包含 / 单文件等属性都写在 MD.csproj 的 Release 条件块里，
#    这里只通过自定义属性 MdWindowsRuntime 覆盖 RID。
#
#  作者：MD
#  创建：2026-09-21
#  修改：2026-09-21  初版
#  修改：2026-09-21  注释改中文；$args 改名为 $publishArgs（避免与 PowerShell 自动变量冲突）
#  修改：2026-09-21  不再向命令行传 -r / --self-contained（会污染 Android 目标导致 NU1102）
# -----------------------------------------------------------------------------
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    # single    -> 自包含单文件（用户无需装任何运行时，约 150~250 MB）
    # framework -> 依赖框架（用户需预装 .NET 10 + Windows App SDK 运行时，约 20 MB）
    [ValidateSet('single', 'framework')]
    [string]$Mode = 'single',

    [string]$Output
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

# Project path works in both layouts (no need to keep two copies of this script):
#   standard layout (local dev)  : src/MD/MD.csproj
#   flat layout     (repo root)  : MD.csproj
$project = Join-Path $root 'src\MD\MD.csproj'
if (-not (Test-Path $project)) { $project = Join-Path $root 'MD.csproj' }

$tf = 'net10.0-windows10.0.19041.0'

if (-not $Output) {
    $Output = Join-Path $root "dist\windows-$Runtime-$Mode"
}

Write-Host "项目    : $project"
Write-Host "运行时  : $Runtime"
Write-Host "模式    : $Mode"
Write-Host "输出    : $Output"
Write-Host ''

$publishArgs = @(
    'publish', $project,
    '-c', 'Release',
    '-f', $tf,
    '-o', $Output,
    "-p:MdWindowsRuntime=$Runtime"
)

if ($Mode -eq 'framework') {
    # 依赖框架模式：不打包 .NET 与 Windows App SDK，由用户机器上的运行时提供
    $publishArgs += '-p:SelfContained=false'
    $publishArgs += '-p:WindowsAppSDKSelfContained=false'
    $publishArgs += '-p:PublishSingleFile=false'
    $publishArgs += '-p:EnableCompressionInSingleFile=false'
}

& dotnet @publishArgs

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败，退出码 $LASTEXITCODE"
}

$exe = Join-Path $Output 'MD.exe'
if (Test-Path $exe) {
    $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host ''
    Write-Host "完成。单文件可执行程序：$exe（$size MB）"
}
else {
    Write-Host ''
    Write-Host "发布完成。输出目录：$Output"
}
