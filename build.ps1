$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path ([System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
$src = Join-Path $root 'src'
& $csc /nologo /target:winexe /optimize+ /codepage:65001 /out:"$root\VicAdmin.exe" `
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
  "/resource:$src\Installer.cs,Installer.cs" "/resource:$src\Shared.cs,Shared.cs" "/resource:$src\app.manifest,app.manifest" `
  "$src\Admin.cs" "$src\Shared.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Host 'OK: VicAdmin.exe'

