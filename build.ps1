$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path ([System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
$src = Join-Path $root 'src'
$res = Join-Path $root 'res_cache'

if (!(Test-Path $res)) { New-Item -ItemType Directory -Path $res | Out-Null }

function Obfuscate-File($inputPath, $outputPath) {
    $bytes = [System.IO.File]::ReadAllBytes($inputPath)
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        $bytes[$i] = $bytes[$i] -bxor 0x5A
    }
    [System.IO.File]::WriteAllBytes($outputPath, $bytes)
}

Obfuscate-File (Join-Path $src "Installer.cs") (Join-Path $res "Installer.cs.dat")
Obfuscate-File (Join-Path $src "Shared.cs") (Join-Path $res "Shared.cs.dat")
Obfuscate-File (Join-Path $src "app.manifest") (Join-Path $res "app.manifest.dat")
Obfuscate-File (Join-Path $src "AssemblyInfo.cs") (Join-Path $res "AssemblyInfo.cs.dat")

& $csc /nologo /target:winexe /optimize+ /codepage:65001 /platform:anycpu /out:"$root\VicAdmin.exe" `
  /win32manifest:"$src\app.manifest" /win32icon:"$root\assets\icon.ico" `
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
  "/resource:$res\Installer.cs.dat,Installer.cs.dat" `
  "/resource:$res\Shared.cs.dat,Shared.cs.dat" `
  "/resource:$res\app.manifest.dat,app.manifest.dat" `
  "/resource:$res\AssemblyInfo.cs.dat,AssemblyInfo.cs.dat" `
  "/resource:$root\assets\icon.ico,icon.ico" `
  "$src\Admin.cs" "$src\Shared.cs" "$src\AssemblyInfo.cs"

if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Host 'OK: VicAdmin.exe'
