#requires -Version 5.1
<#
  显示器切换器 · DisplayDock v1.0 构建脚本
  用法：
    .\build.ps1                 # 只编译主程序 → dist\DisplayDock.exe
    .\build.ps1 -Test           # 编译主程序 + 编译并运行回归测试（含实机 DDC 探测）
    .\build.ps1 -Package        # 编译主程序并生成 dist\DisplayDock-v1.0.zip 发布包
  依赖：Windows 自带的 .NET Framework 4.x C# 编译器（csc.exe），不需要安装任何 SDK。
#>
[CmdletBinding()]
param(
    [switch]$Test,
    [switch]$Package
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src = Join-Path $root 'src'
$dist = Join-Path $root 'dist'
$buildDir = Join-Path $root 'build'

function Find-Csc {
    $candidates = @(
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    throw '找不到 .NET Framework 4.x 的 csc.exe，请确认系统已安装 .NET Framework 4.6 或更高版本。'
}

$csc = Find-Csc
$references = @(
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Runtime.Serialization.dll'
)

function Invoke-Csc {
    param([string]$Output, [string[]]$Sources, [string]$Target = 'winexe', [string]$WorkingDirectory = $buildDir)

    if (Test-Path -LiteralPath $Output) { Remove-Item -LiteralPath $Output -Force }
    $arguments = @('-nologo', '-utf8output', "-target:$Target", "-out:$Output", '-optimize+')
    foreach ($reference in $references) { $arguments += "-reference:$reference" }
    $arguments += $Sources

    $log = Join-Path $buildDir 'csc.log'
    # csc 直接吃绝对路径在部分编码环境下会被 MSYS/中文路径破坏，这里统一用工作目录 + 文件名。
    Push-Location -LiteralPath $WorkingDirectory
    try {
        $arguments = $arguments | ForEach-Object {
            if ($_ -match '\.cs$') { Split-Path -Leaf $_ } else { $_ }
        }
        & $csc @arguments 2>&1 | Tee-Object -FilePath $log
        $code = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    if ($code -ne 0) {
        throw "编译失败（退出码 $code），详见 $log"
    }
}

function Get-AppSources {
    Get-ChildItem -LiteralPath $src -Filter '*.cs' |
        Where-Object { $_.Name -ne 'Tests.cs' } |
        ForEach-Object { $_.FullName }
}

function Get-TestSources {
    @('Models.cs', 'PortCatalog.cs', 'MonitorSession.cs', 'HotkeyManager.cs', 'AppService.cs', 'Tests.cs') |
        ForEach-Object { Join-Path $src $_ }
}

New-Item -ItemType Directory -Path $dist -Force | Out-Null
New-Item -ItemType Directory -Path $buildDir -Force | Out-Null

# 主程序
$mainOutput = Join-Path $dist 'DisplayDock.exe'
Write-Host "== 编译主程序 → $mainOutput" -ForegroundColor Cyan
# 先把源码复制到 build 目录：csc 只用文件名（中文长绝对路径在某些编码环境会被破坏）。
Get-AppSources | ForEach-Object { Copy-Item -LiteralPath $_ -Destination $buildDir -Force }
Invoke-Csc -Output $mainOutput -Sources (Get-AppSources)
Write-Host "   完成：$((Get-Item -LiteralPath $mainOutput).Length) 字节" -ForegroundColor Green

# 回归测试
if ($Test) {
    $testDir = Join-Path $buildDir 'test'
    New-Item -ItemType Directory -Path $testDir -Force | Out-Null
    $testOutput = Join-Path $testDir 'DisplayDockTests.exe'
    Write-Host "== 编译回归测试 → $testOutput" -ForegroundColor Cyan
    Get-TestSources | ForEach-Object { Copy-Item -LiteralPath $_ -Destination $testDir -Force }
    Invoke-Csc -Output $testOutput -Sources (Get-TestSources) -Target 'exe' -WorkingDirectory $testDir
    Write-Host '== 运行回归测试（含实机 DDC 探测）' -ForegroundColor Cyan
    Push-Location -LiteralPath $testDir
    try {
        & $testOutput '--live-ddc'
        $testCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    if ($testCode -ne 0) { throw "回归测试失败（退出码 $testCode）" }
    Write-Host '   回归测试全部通过。' -ForegroundColor Green
}

# 发布包
if ($Package) {
    $stage = Join-Path $buildDir 'package'
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    Copy-Item -LiteralPath $mainOutput -Destination $stage
    foreach ($extra in @('README.md', 'src', 'build.ps1', '使用说明.md', '安装说明.txt', '主界面.png')) {
        $path = Join-Path $root $extra
        if (Test-Path -LiteralPath $path) {
            Copy-Item -LiteralPath $path -Destination $stage -Recurse -Force
        }
    }
    $zip = Join-Path $dist 'DisplayDock-v1.0.zip'
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
    Write-Host "== 发布包 → $zip（$([math]::Round((Get-Item -LiteralPath $zip).Length / 1MB, 2)) MB）" -ForegroundColor Green
}
