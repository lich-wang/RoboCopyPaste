$ErrorActionPreference = 'Stop'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $root 'src'
$output = Join-Path $root 'RoboCopyPaste.exe'
$appIcon = Join-Path $root 'assets\app-icon.ico'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /out:$output `
  "/win32icon:$appIcon" `
  /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
  /reference:System.Windows.Forms.dll /reference:Microsoft.CSharp.dll `
  (Join-Path $source '*.cs')
if ($LASTEXITCODE -ne 0) { throw "C# compiler failed with exit code $LASTEXITCODE" }
Write-Host $output
