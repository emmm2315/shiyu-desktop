param([switch]$Test, [switch]$Preview, [switch]$Diagnostics, [string]$OutputPath)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw '找不到系统 C# 编译器，需要 Windows .NET Framework 4.x。' }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$output = if ($OutputPath) { [System.IO.Path]::GetFullPath($OutputPath) } else { Join-Path $taskRoot '时屿.exe' }
New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($output)) -Force | Out-Null
$testDirectory = Join-Path $taskRoot 'artifacts'
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
$iconBuilder = Join-Path $testDirectory 'IconBuilder.exe'
$appIcon = Join-Path $testDirectory 'app.ico'
& $compiler /nologo /target:exe /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$iconBuilder" (Join-Path $taskRoot 'src\Drawing.cs') (Join-Path $taskRoot 'tools\IconBuilder.cs')
if ($LASTEXITCODE -ne 0) { throw '图标生成器编译失败。' }
& $iconBuilder $appIcon
if ($LASTEXITCODE -ne 0) { throw '图标生成失败。' }
$arguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/debug-', '/utf8output',
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
    '/r:System.Runtime.Serialization.dll', ('/win32manifest:' + (Join-Path $taskRoot 'app.manifest')),
    ('/win32icon:' + $appIcon),
    ('/out:' + $output)) + $sources
if ($Diagnostics) { $arguments += '/define:DIAGNOSTICS' }
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw '组件编译失败。' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'app.config') -Destination ($output + '.config') -Force
Write-Output ('已生成：' + $output)
if ($Test) {
    $coreSources = @((Join-Path $taskRoot 'src\Core.cs'), (Join-Path $taskRoot 'src\ExtrasCore.cs'), (Join-Path $taskRoot 'src\LearningPack.cs'), (Join-Path $taskRoot 'src\UniversityPacks.cs'))
    $testBinary = Join-Path $testDirectory 'CoreTests.exe'
    & $compiler /nologo /target:exe /r:System.dll /r:System.Core.dll /r:System.Runtime.Serialization.dll "/out:$testBinary" @coreSources (Join-Path $taskRoot 'tests\CoreTests.cs')
    if ($LASTEXITCODE -ne 0) { throw '测试编译失败。' }
    & $testBinary
    if ($LASTEXITCODE -ne 0) { throw '核心测试失败。' }
    $calendarTestBinary = Join-Path $testDirectory 'CalendarTests.exe'
    & $compiler /nologo /target:exe /r:System.dll /r:System.Core.dll /r:System.Runtime.Serialization.dll "/out:$calendarTestBinary" @coreSources (Join-Path $taskRoot 'tests\CalendarTests.cs')
    if ($LASTEXITCODE -ne 0) { throw '日历测试编译失败。' }
    & $calendarTestBinary
    if ($LASTEXITCODE -ne 0) { throw '日历测试失败。' }
    $extrasTestBinary = Join-Path $testDirectory 'ExtrasTests.exe'
    & $compiler /nologo /target:exe /r:System.dll /r:System.Core.dll /r:System.Runtime.Serialization.dll "/out:$extrasTestBinary" @coreSources (Join-Path $taskRoot 'tests\ExtrasTests.cs')
    if ($LASTEXITCODE -ne 0) { throw '小功能测试编译失败。' }
    & $extrasTestBinary
    if ($LASTEXITCODE -ne 0) { throw '小功能与学习包测试失败。' }
    $uiTestBinary = Join-Path $testDirectory 'UiFlowCheck.exe'
    & $compiler /nologo /utf8output /target:exe /main:DeskSprout.UiFlowCheck /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll "/out:$uiTestBinary" @sources (Join-Path $taskRoot 'tools\UiFlowCheck.cs')
    if ($LASTEXITCODE -ne 0) { throw '界面流程检查编译失败。' }
    & $uiTestBinary
    if ($LASTEXITCODE -ne 0) { throw '界面流程检查失败。' }
    $dialogTestBinary = Join-Path $testDirectory 'DialogLayerCheck.exe'
    & $compiler /nologo /utf8output /target:exe /main:DeskSprout.DialogLayerCheck /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll "/out:$dialogTestBinary" @sources (Join-Path $taskRoot 'tools\DialogLayerCheck.cs')
    if ($LASTEXITCODE -ne 0) { throw '弹窗层级检查编译失败。' }
    & $dialogTestBinary
    if ($LASTEXITCODE -ne 0) { throw '真实模态弹窗检查失败。' }
}
if ($Preview) {
    $previewDirectory = Join-Path $taskRoot 'artifacts\previews'
    $process = Start-Process -FilePath $output -ArgumentList @('--render', ('"' + $previewDirectory + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw '预览生成失败。' }
    Write-Output ('预览已生成：' + $previewDirectory)
}
