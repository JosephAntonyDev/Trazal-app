param([ValidateSet('Debug','Release')][string]$Configuration = 'Release', [switch]$Test, [switch]$Package)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (!(Test-Path $vswhere)) { throw 'Instala Visual Studio 2022 o Build Tools con MSBuild y herramientas de escritorio .NET.' }
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (!$msbuild) { throw 'No se encontró MSBuild de Visual Studio.' }
$packages = Join-Path $root 'build\packages'
New-Item -ItemType Directory -Force $packages | Out-Null
# Pinned upstream NuGet packages; cached locally, never installed globally.
$dependencies = @(
    @{Name='net48'; Id='microsoft.netframework.referenceassemblies.net48'; Version='1.0.3'; Sha='8A7E348538E7EB91351696911689F49E3D4F63F8BAB517432BBE159B8B1104A2'},
    @{Name='sqlite'; Id='stub.system.data.sqlite.core.netframework'; Version='1.0.119'; Sha='F5F86B80729323890DA590A8C7BA7957F04E2735E583D075220D1521745E9F4C'},
    @{Name='excelreader'; Id='exceldatareader'; Version='3.7.0'; Sha='7222A0A533A495F3F596334A8A98F38F974394E1F75CC2922AF11E4E15C22834'},
    @{Name='pdfsharp'; Id='pdfsharp-gdi'; Version='6.2.4'; Sha='E7F5B705598F6F23FCEA34789EEF495F4333AAD1C37A96EDD0D369E09EB213DF'},
    @{Name='logging'; Id='microsoft.extensions.logging.abstractions'; Version='8.0.3'; Sha='E4C498D5A13051B4577A148F1D8C3470167215C507E2392069B75DC61322BB74'},
    @{Name='pkcs'; Id='system.security.cryptography.pkcs'; Version='8.0.1'; Sha='28C348909DF243FE4EE962213E3C8021AAECBC8321929DBA03A69BCB92A49DE5'},
    @{Name='di'; Id='microsoft.extensions.dependencyinjection.abstractions'; Version='8.0.2'; Sha='51F2DF1100245F10DA54F0BB7E813F277155117777D4FBBAB902214E27372606'},
    @{Name='buffers'; Id='system.buffers'; Version='4.5.1'; Sha='C30B3DD2C7E2F4CEE4B823D692FD42118309B42AB1F5007F923D329A5B0D6B12'},
    @{Name='memory'; Id='system.memory'; Version='4.5.5'; Sha='10F43DA352A29FB2B3188E4EDD4DCF5100194C8B526E4F61FE2E2B5623775A22'},
    @{Name='unsafe'; Id='system.runtime.compilerservices.unsafe'; Version='4.5.3'; Sha='96764C52A44EE1161151E48EF07489F72047A851CB55B99E9F01D6908536D1A9'},
    @{Name='vectors'; Id='system.numerics.vectors'; Version='4.5.0'; Sha='A9D49320581FDA1B4F4BE6212C68C01A22CDF228026099C20A8EABEFCF90F9CF'},
    @{Name='asyncinterfaces'; Id='microsoft.bcl.asyncinterfaces'; Version='8.0.0'; Sha='F5A5A68B03092AB2ABF68843D4A4AEA25DFBCBE8DD0F13C625CB779B6FC1927C'},
    @{Name='tasks'; Id='system.threading.tasks.extensions'; Version='4.5.4'; Sha='A304A963CC0796C5179F9C6B7D8022BBCE3B2FA7C029EB6196F631F7B462D678'}
)
foreach ($dep in $dependencies) {
    $zip = Join-Path $packages ($dep.Name + '.zip')
    if (!(Test-Path $zip)) { Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/$($dep.Id)/$($dep.Version)/$($dep.Id).$($dep.Version).nupkg" -OutFile $zip }
    if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne $dep.Sha) { throw "Integridad incorrecta: $($dep.Name). No se compilará." }
    $expanded = Join-Path $packages $dep.Name
    $stamp = Join-Path $expanded '.verified-sha256'
    if (!(Test-Path $stamp) -or (Get-Content $stamp -Raw).Trim() -ne $dep.Sha) {
        Expand-Archive $zip $expanded -Force
        Set-Content -LiteralPath $stamp -Value $dep.Sha
    }
}
$appOutput = Join-Path $root "build\output\$Configuration\app"
$testOutput = Join-Path $root "build\output\$Configuration\tests"
& $msbuild (Join-Path $root 'src\Trazal.Desktop\Trazal.Desktop.csproj') /nologo /verbosity:minimal "/p:Configuration=$Configuration" "/p:OutDir=$appOutput\"
if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación de Trazal.' }
if ($Test) {
    & $msbuild (Join-Path $root 'tests\Trazal.Tests\Trazal.Tests.csproj') /nologo /verbosity:minimal "/p:Configuration=$Configuration" "/p:OutDir=$testOutput\"
    if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación de pruebas.' }
    & (Join-Path $testOutput 'Trazal.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas.' }
    & (Join-Path $root 'tests\ConfigureAuth.Tests.ps1')
}
$authConfig = Join-Path $root 'authsettings.json'
if (Test-Path -LiteralPath $authConfig) { Copy-Item -LiteralPath $authConfig -Destination (Join-Path $appOutput 'authsettings.json') -Force }
if ($Package) {
    $output = Join-Path $root 'artifacts\Trazal-0.6.2-preview'
    New-Item -ItemType Directory -Force $output | Out-Null
    Copy-Item (Join-Path $appOutput '*') $output -Recurse -Force
    Copy-Item (Join-Path $root 'LEEME.txt') $output -Force
    Copy-Item (Join-Path $root 'authsettings.example.json') $output -Force
    Copy-Item (Join-Path $root 'docs\FIREBASE-Y-CUENTAS.md') $output -Force
    Copy-Item (Join-Path $root 'docs\CONEXION-Y-CONTINUIDAD.md') $output -Force
    Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $output -Force
    Copy-Item (Join-Path $root 'ExcelDataReader-LICENSE.txt') $output -Force
    Copy-Item (Join-Path $root 'REPORTING-LICENSES.txt') $output -Force
    Compress-Archive (Join-Path $output '*') (Join-Path $root 'artifacts\Trazal-0.6.2-preview.zip') -Force
    Write-Host "Paquete portable: $output"
}
