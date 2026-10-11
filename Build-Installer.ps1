$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskLauncher = Join-Path $PSScriptRoot 'launcher.exe'
& $taskCompiler /nologo /target:winexe /platform:x64 /optimize+ ('/out:'+$taskLauncher) /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'Launcher.cs')
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed' }
& $taskCompiler /nologo /target:winexe /platform:x64 /optimize+ ('/out:'+(Join-Path $PSScriptRoot 'Codex-Fixed-Installer.exe')) /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll ('/resource:'+(Join-Path $PSScriptRoot 'manifest.json')+',manifest.json') ('/resource:'+(Join-Path $PSScriptRoot 'header.gz')+',header.gz') ('/resource:'+$taskLauncher+',launcher.exe') (Join-Path $PSScriptRoot 'Installer.cs')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
