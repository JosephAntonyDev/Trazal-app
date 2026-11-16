$ErrorActionPreference = 'Stop'
$configure = Join-Path $PSScriptRoot '..\configure-auth.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('Trazal-auth-config-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$configFile = Join-Path $testRoot 'authsettings.json'
$oauthFile = Join-Path $testRoot 'client_secret_test.json'
function Assert($condition, $message) { if (!$condition) { throw $message } }
function RejectConfig($arguments) {
    $failed = $false
    try { & $configure @arguments } catch { $failed = $true }
    Assert $failed 'Se esperaba rechazar una configuración inválida.'
}
try {
    & $configure -ConfigPath $configFile -FirebaseApiKey 'test-public-key'
    $first = Get-Content -LiteralPath $configFile -Raw | ConvertFrom-Json
    Assert ($first.apiOrigin -eq 'https://trazal-api.trazaldev.workers.dev') 'Origen incorrecto.'
    Assert ($first.projectId -eq 'trazal-773ba') 'Proyecto incorrecto.'
    $before = (Get-FileHash -LiteralPath $configFile).Hash
    @{installed=@{project_id='wrong-project';client_id='test.apps.googleusercontent.com';client_secret='test-only'}} | ConvertTo-Json | Set-Content -LiteralPath $oauthFile -Encoding UTF8
    RejectConfig @{ConfigPath=$configFile;OAuthJson=$oauthFile}
    Assert ((Get-FileHash -LiteralPath $configFile).Hash -eq $before) 'Proyecto distinto modificó la configuración.'
    @{web=@{project_id='trazal-773ba';client_id='test.apps.googleusercontent.com';client_secret='test-only'}} | ConvertTo-Json | Set-Content -LiteralPath $oauthFile -Encoding UTF8
    RejectConfig @{ConfigPath=$configFile;OAuthJson=$oauthFile}
    Assert ((Get-FileHash -LiteralPath $configFile).Hash -eq $before) 'Cliente Web modificó la configuración.'
    RejectConfig @{ConfigPath=$configFile;ApiOrigin='http://example.com'}
    RejectConfig @{ConfigPath=$configFile;ApiOrigin='https://example.com/v1'}
    Assert ((Get-FileHash -LiteralPath $configFile).Hash -eq $before) 'Origen inválido modificó la configuración.'
    @{installed=@{project_id='trazal-773ba';client_id='test.apps.googleusercontent.com';client_secret='test-only'}} | ConvertTo-Json | Set-Content -LiteralPath $oauthFile -Encoding UTF8
    & $configure -ConfigPath $configFile -OAuthJson $oauthFile
    $updated = Get-Content -LiteralPath $configFile -Raw | ConvertFrom-Json
    Assert ($updated.apiKey -eq 'test-public-key') 'No conservó la clave existente.'
    Assert ($updated.googleDesktopClientId -eq 'test.apps.googleusercontent.com') 'No importó OAuth.'
    & $configure -ConfigPath $configFile
    $repeated = Get-Content -LiteralPath $configFile -Raw | ConvertFrom-Json
    Assert ($repeated.googleDesktopClientSecret -eq 'test-only') 'No conservó OAuth al cambiar el origen.'
    Assert ((Get-ChildItem -LiteralPath $testRoot -Filter '*.tmp').Count -eq 0) 'Quedó un temporal de configuración.'
    Write-Host 'OK: configuración local, proyecto OAuth, tipo de cliente, HTTPS, preservación y reemplazo atómico.'
}
finally {
    foreach ($file in @($configFile, $oauthFile)) { if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force } }
    # No recursive cleanup; only the two files created above and their empty test directory.
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -ErrorAction SilentlyContinue }
}
