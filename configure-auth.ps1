param(
    [string]$OAuthJson,
    [string]$FirebaseApiKey,
    [string]$ProjectId = 'trazal-773ba',
    [string]$ApiOrigin = 'https://trazal-api.trazaldev.workers.dev',
    [string]$ConfigPath = (Join-Path $PSScriptRoot 'authsettings.json')
)
$ErrorActionPreference = 'Stop'

# Local configuration only. Never print the API key, OAuth secret or JSON contents.
if ($ProjectId -notmatch '^[a-z][a-z0-9-]{4,62}$') { throw 'ProjectId no válido.' }
$uri = $null
if (![Uri]::TryCreate($ApiOrigin, [UriKind]::Absolute, [ref]$uri) -or
    $uri.Scheme -ne 'https' -or $uri.UserInfo -ne '' -or $uri.AbsolutePath -ne '/' -or
    $uri.Query -ne '' -or $uri.Fragment -ne '') { throw 'ApiOrigin debe ser un origen HTTPS sin ruta ni credenciales.' }

$settings = [ordered]@{ apiKey=''; projectId=$ProjectId; apiOrigin=$uri.GetLeftPart([UriPartial]::Authority); googleDesktopClientId=''; googleDesktopClientSecret='' }
$destination = [IO.Path]::GetFullPath($ConfigPath)
if (Test-Path -LiteralPath $destination) {
    try { $existing = Get-Content -LiteralPath $destination -Raw | ConvertFrom-Json }
    catch { throw 'No se pudo leer la configuración existente. No se modificó el archivo.' }
    if ($existing.projectId -and $existing.projectId -ne $ProjectId) { throw 'La configuración existente pertenece a otro proyecto. Usa un archivo separado para otro entorno.' }
    foreach ($field in @('apiKey','googleDesktopClientId','googleDesktopClientSecret')) {
        if ($existing.$field) { $settings[$field] = [string]$existing.$field }
    }
}
if ($FirebaseApiKey) { $settings.apiKey = $FirebaseApiKey.Trim() }
if ([string]::IsNullOrWhiteSpace($settings.apiKey)) { throw 'Falta la apiKey de la app Web de Firebase en la configuración local.' }

if ($OAuthJson) {
    try { $oauth = Get-Content -LiteralPath $OAuthJson -Raw | ConvertFrom-Json }
    catch { throw 'No se pudo leer el JSON OAuth. No se modificó la configuración.' }
    if (!$oauth.installed) { throw 'Se necesita un cliente OAuth de tipo Aplicación de escritorio (installed), no un cliente Web.' }
    if ($oauth.installed.project_id -ne $ProjectId) { throw 'El cliente OAuth pertenece a otro proyecto. Créalo dentro del mismo proyecto Firebase; no se modificó la configuración.' }
    if ($oauth.installed.client_id -notmatch '^[A-Za-z0-9_-]+\.apps\.googleusercontent\.com$' -or
        [string]::IsNullOrWhiteSpace($oauth.installed.client_secret)) { throw 'El JSON OAuth no contiene un cliente de escritorio completo.' }
    $settings.googleDesktopClientId = [string]$oauth.installed.client_id
    $settings.googleDesktopClientSecret = [string]$oauth.installed.client_secret
}

# Validate everything before touching the existing file; replace atomically on the same volume.
$parent = Split-Path -Parent $destination
if (!(Test-Path -LiteralPath $parent -PathType Container)) { throw 'La carpeta de destino no existe.' }
$temporary = Join-Path $parent ('authsettings.json.' + [Guid]::NewGuid().ToString('N') + '.tmp')
try {
    [IO.File]::WriteAllText($temporary, ($settings | ConvertTo-Json) + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
    if (Test-Path -LiteralPath $destination) { [IO.File]::Replace($temporary, $destination, [NullString]::Value) }
    else { [IO.File]::Move($temporary, $destination) }
}
finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
Write-Host 'Configuración local preparada. Compila el paquete y reinicia Trazal para usarla.'
if (!$settings.googleDesktopClientId) { Write-Host 'Google sigue pendiente del cliente OAuth de escritorio. Correo y contraseña pueden probarse ahora.' }
