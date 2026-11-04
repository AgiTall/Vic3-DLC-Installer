$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fwDir = [System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
$src = Join-Path $root 'src'

if (Test-Path (Join-Path $root 'res_cache')) {
    Remove-Item (Join-Path $root 'res_cache') -Recurse -Force
}

$roslyn = Get-ChildItem "C:\Program Files\dotnet\sdk" -Recurse -Filter "csc.dll" -ErrorAction SilentlyContinue | Select-Object -Last 1

# 1. Сборка общей библиотеки Vic3Shared.dll (разделение на несколько библиотек без упаковки ресурсов в один bundle)
$dllArgs = @(
    "/nologo",
    "/target:library",
    "/optimize+",
    "/platform:anycpu",
    "/out:$root\Vic3Shared.dll",
    "/r:$fwDir\mscorlib.dll",
    "/r:$fwDir\System.dll",
    "/r:$fwDir\System.Drawing.dll",
    "$src\Shared.cs"
)

if ($roslyn) {
    & dotnet $roslyn.FullName /nostdlib+ /deterministic+ /highentropyva+ @dllArgs
} else {
    $csc = Join-Path $fwDir 'csc.exe'
    & $csc /codepage:65001 @dllArgs
}
if ($LASTEXITCODE -ne 0) { throw 'DLL build failed' }

# 2. Сборка основного приложения VicAdmin.exe (без вшитых .dat архивов и обфускаторов)
$exeArgs = @(
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
    "/r:$root\Vic3Shared.dll",
    "$src\Admin.cs",
    "$src\AssemblyInfo.cs"
)

if ($roslyn) {
    & dotnet $roslyn.FullName /nostdlib+ /deterministic+ /highentropyva+ @exeArgs
} else {
    $csc = Join-Path $fwDir 'csc.exe'
    & $csc /codepage:65001 @exeArgs
}
if ($LASTEXITCODE -ne 0) { throw 'EXE build failed' }

# 3. Цифровая подпись сборки сертификатом разработчика (Authenticode)
try {
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -match 'AgiTall' } | Select-Object -First 1
    if (-not $cert) {
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=AgiTall, O=AgiTall & Rotgott" -CertStoreLocation "Cert:\CurrentUser\My" -NotAfter (Get-Date).AddYears(5)
    }
    Set-AuthenticodeSignature -FilePath "$root\Vic3Shared.dll" -Certificate $cert -HashAlgorithm SHA256 | Out-Null
    Set-AuthenticodeSignature -FilePath "$root\VicAdmin.exe" -Certificate $cert -HashAlgorithm SHA256 | Out-Null
} catch {
    Write-Warning "Signing skipped: $_"
}

Write-Host 'OK: VicAdmin.exe + Vic3Shared.dll (Signed)'
