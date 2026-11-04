$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fwDir = [System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
$src = Join-Path $root 'src'
$res = Join-Path $root 'res_cache'

if (!(Test-Path $res)) { New-Item -ItemType Directory -Path $res | Out-Null }

function Compress-Resource($inputPath, $outputPath) {
    $bytes = [System.IO.File]::ReadAllBytes($inputPath)
    $fs = [System.IO.File]::Create($outputPath)
    try {
        $ds = New-Object System.IO.Compression.DeflateStream($fs, [System.IO.Compression.CompressionLevel]::Optimal)
        try {
            $ds.Write($bytes, 0, $bytes.Length)
        } finally {
            $ds.Dispose()
        }
    } finally {
        $fs.Dispose()
    }
}

Compress-Resource (Join-Path $src "Installer.cs") (Join-Path $res "Installer.cs.dat")
Compress-Resource (Join-Path $src "Shared.cs") (Join-Path $res "Shared.cs.dat")
Compress-Resource (Join-Path $src "app.manifest") (Join-Path $res "app.manifest.dat")
Compress-Resource (Join-Path $src "AssemblyInfo.cs") (Join-Path $res "AssemblyInfo.cs.dat")

$roslyn = Get-ChildItem "C:\Program Files\dotnet\sdk" -Recurse -Filter "csc.dll" -ErrorAction SilentlyContinue | Select-Object -Last 1

$commonArgs = @(
    "/nologo",
    "/target:winexe",
    "/optimize+",
    "/platform:anycpu",
    "/out:$root\VicAdmin.exe",
    "/win32manifest:$src\app.manifest",
    "/win32icon:$root\assets\icon.ico",
    "/r:$fwDir\mscorlib.dll",
    "/r:$fwDir\System.dll",
    "/r:$fwDir\System.Core.dll",
    "/r:$fwDir\System.Drawing.dll",
    "/r:$fwDir\System.Windows.Forms.dll",
    "/r:$fwDir\System.IO.Compression.dll",
    "/r:$fwDir\System.IO.Compression.FileSystem.dll",
    "/r:$fwDir\Microsoft.CSharp.dll",
    "/resource:$res\Installer.cs.dat,Installer.cs.dat",
    "/resource:$res\Shared.cs.dat,Shared.cs.dat",
    "/resource:$res\app.manifest.dat,app.manifest.dat",
    "/resource:$res\AssemblyInfo.cs.dat,AssemblyInfo.cs.dat",
    "/resource:$root\assets\icon.ico,icon.ico",
    "$src\Admin.cs",
    "$src\Shared.cs",
    "$src\AssemblyInfo.cs"
)

if ($roslyn) {
    & dotnet $roslyn.FullName /nostdlib+ /deterministic+ /highentropyva+ @commonArgs
} else {
    $csc = Join-Path $fwDir 'csc.exe'
    & $csc /codepage:65001 @commonArgs
}

if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Host 'OK: VicAdmin.exe'
