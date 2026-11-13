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
    @{Name='sqlite'; Id='stub.system.data.sqlite.core.netframework'; Version='1.0.119'; Sha='F5F86B80729323890DA590A8C7BA7957F04E2735E583D075220D1521745E9F4C'}
)
foreach ($dep in $dependencies) {
    $zip = Join-Path $packages ($dep.Name + '.zip')
    if (!(Test-Path $zip)) { Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/$($dep.Id)/$($dep.Version)/$($dep.Id).$($dep.Version).nupkg" -OutFile $zip }
    if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne $dep.Sha) { throw "Integridad incorrecta: $($dep.Name). No se compilará." }
    Expand-Archive $zip (Join-Path $packages $dep.Name) -Force
}
& $msbuild (Join-Path $root 'src\Trazal.Desktop\Trazal.Desktop.csproj') /nologo /verbosity:minimal "/p:Configuration=$Configuration"
if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación de Trazal.' }
if ($Test) {
    & $msbuild (Join-Path $root 'tests\Trazal.Tests\Trazal.Tests.csproj') /nologo /verbosity:minimal "/p:Configuration=$Configuration"
    if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación de pruebas.' }
    & (Join-Path $root "tests\Trazal.Tests\bin\$Configuration\Trazal.Tests.exe")
    if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas.' }
}
if ($Package) {
    $output = Join-Path $root 'artifacts\Trazal-0.2.0-preview'
    New-Item -ItemType Directory -Force $output | Out-Null
    Copy-Item (Join-Path $root "src\Trazal.Desktop\bin\$Configuration\*") $output -Recurse -Force
    Copy-Item (Join-Path $root 'LEEME.txt') $output -Force
    Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $output -Force
    Compress-Archive (Join-Path $output '*') (Join-Path $root 'artifacts\Trazal-0.2.0-preview.zip') -Force
    Write-Host "Paquete portable: $output"
}
