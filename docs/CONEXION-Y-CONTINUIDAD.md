# Conectar el escritorio y continuar el proyecto

Actualizado el 15/11/2026. Escritorio 0.6.3, API publicada 0.6.0. La [guía central del equipo](https://github.com/JosephAntonyDev/Trazal-API-TS/blob/main/docs/ESTADO-Y-CONTINUIDAD.md) documenta Neon, Hyperdrive, Firebase, GitHub Builds, identificadores, incidencias y evidencia del despliegue.

## Preparar el equipo de desarrollo

1. Clonar [Trazal-app](https://github.com/JosephAntonyDev/Trazal-app). La carpeta local de Joseph se llama `Trazal_Desktop`; no es necesario conservar ese nombre en otros equipos.
2. Instalar las herramientas WPF de Visual Studio/Build Tools y referencias de .NET Framework 4.8 según el [README](../README.md). El producto es C# / WPF; React/Vite solo corresponde al mockup. No se está desarrollando el cliente actual con Flutter.
3. Desde la raíz del repositorio, ejecutar en PowerShell:

   ```powershell
   .\build.ps1 -Test -Package
   ```

4. Abrir `artifacts/Trazal-0.6.3-preview/Trazal.exe`. Compartir el ZIP completo, no únicamente el EXE. Este paquete es portable, no un instalador ni una distribución de .NET Framework.

## Configurar una instalación

Copiar `authsettings.example.json` como `authsettings.json` junto al EXE y completar:

```json
{
  "apiKey": "COPIAR_DE_LA_APP_WEB_EN_FIREBASE",
  "projectId": "trazal-773ba",
  "apiOrigin": "https://trazal-api.trazaldev.workers.dev",
  "googleDesktopClientId": "",
  "googleDesktopClientSecret": ""
}
```

El valor de `apiKey` se obtiene de Firebase Console → proyecto `trazal-773ba` → Configuración del proyecto → General → app Web → configuración SDK. No usar claves de administrador. No dejar el texto de ejemplo como valor real.

También se puede guardar ese archivo en la raíz del repositorio antes de compilar: `build.ps1` lo copia a la salida y al paquete. Está ignorado por Git. Los paquetes preparados localmente pueden contener esa configuración; revisarla al distribuir. Alternativamente, configurar `TRAZAL_FIREBASE_API_KEY`, `TRAZAL_FIREBASE_PROJECT_ID` y `TRAZAL_API_ORIGIN`; prevalecen sobre el archivo. Reiniciar Trazal después de cambiarla.

El paquete 0.6.3 preparado en el equipo de Joseph incluye el origen publicado. Los paquetes antiguos no cambian. En un clon nuevo, completar `authsettings.json` localmente: `configure-auth.ps1` conserva la configuración existente y permite importar Google con `-OAuthJson`, verificando que el proyecto coincida. El archivo real sigue fuera de Git.

La app no recibe el ID de Hyperdrive, la contraseña PostgreSQL ni una conexión directa a Neon. Se comunica con Firebase para identidad y con el Worker mediante HTTPS para sincronización.

## Comprobación de conexión

Desde PowerShell, estas comprobaciones no necesitan credenciales ni modifican datos:

```powershell
curl.exe -i https://trazal-api.trazaldev.workers.dev/health
curl.exe -i https://trazal-api.trazaldev.workers.dev/v1/me
```

Resultados comprobados el 15/11/2026: HTTP 200 con servicio/versión en `/health`, HTTP 401 `authentication_required` en `/v1/me` sin sesión. La primera ruta no consulta PostgreSQL; la segunda solo comprueba el rechazo sin token. Falta probar lectura/escritura autenticada real.

Para esa prueba:

1. Abrir **Mi cuenta**, crear/iniciar una cuenta de prueba y confirmar correo. Pulsar la comprobación de sesión al volver del enlace de verificación.
2. Crear una obra ficticia dentro de ese espacio, con concepto, programa, avance y gasto. No usar información del ingeniero para esta primera comprobación.
3. Abrir **Sincronización** y pulsar **Comprobar conexión**: consulta `/v1/me`, valida que el UID coincida y comprueba acceso a la base; puede crear la cuenta técnica en el servidor, pero no transfiere obras ni vincula/modifica el espacio local. Después pulsar **Sincronizar mis obras**, registrar el resultado sin exponer tokens y comprobar datos en Neon con acceso administrativo autorizado.
4. Repetir inicio de sesión con la misma cuenta desde otro equipo/usuario de Windows y descargar la obra. Comparar campos e importes.
5. Probar cambios concurrentes para comprobar el conflicto de revisión. La resolución visual aún está pendiente; no forzar sobrescrituras para hacer pasar la prueba.

Iniciar sesión abre un SQLite propio de proyecto/UID. Las obras del espacio local original no aparecen automáticamente en la cuenta. Cerrar sesión vuelve al espacio original, sin borrarlo. No mover archivos entre espacios para saltarse esa separación. El traslado guiado de obras a una cuenta sigue pendiente.

## Google: pendiente concreto

Google y Correo/contraseña ya están habilitados en Firebase. El flujo Google está implementado en el escritorio con navegador del sistema, PKCE y retorno temporal a `127.0.0.1`; falta validación real.

El JSON entregado anteriormente pertenecía a `trazal-773ba-514820`, diferente del proyecto de autenticación `trazal-773ba`, y no se aplicó. Crear un cliente OAuth de tipo **Desktop app** en el proyecto correcto y completar `googleDesktopClientId` / `googleDesktopClientSecret` localmente. Seguir [Firebase y cuentas](FIREBASE-Y-CUENTAS.md#configurar-google-para-escritorio). No hace falta esperar Google para probar correo/contraseña.

No guardar JSON OAuth, tokens ni contraseñas en el repo. Los ID tokens permanecen en memoria; recordar sesión es opcional y protege el refresh token con DPAPI del usuario de Windows. El perfil local no cambia el correo de acceso.

## Qué conservar al continuar

- Amarillo como identidad de marca; temas claro/oscuro/sistema, ayudas contextuales y campos obligatorios con asterisco.
- Guardar en creación exige datos válidos; en edición exige además cambios.
- Precio por obra, programa aprobado fijo, avances acumulados hasta 100 % y gastos independientes por categoría.
- SQLite local como base de trabajo. La nube opcional solo guardará datos estructurados, no fotos/PDF/Excel originales.
- Suscripción vencida conservará los datos previamente sincronizados y bloqueará escrituras nuevas; esa autorización comercial todavía no está implementada. [Modelo acordado](https://github.com/JosephAntonyDev/Trazal-API-TS/blob/main/docs/MODELO-LOCAL-Y-NUBE.md).
- El inventario visual y flujo de efectivo del mockup no significan paridad completa en escritorio/API. Revisar el contrato antes de añadir campos o prometer sincronizarlos.
- La carpeta `backend/` es histórica. Los cambios actuales de servidor van en Trazal-API-TS. Un push allí puede desplegar el Worker, pero no actualiza este EXE.

## Próximas validaciones

Primero completar Firebase y sincronización reales, después Google y conflicto entre equipos. Probar Windows 7 SP1 real, incluyendo navegador/TLS, SQLite x86/x64, PDF/XLSX y restauración. Aún no se certificaron RAM mínima, tiempos ni compatibilidad real. Instalador, actualización, licencias, permisos comerciales y resolución visual de conflictos siguen pendientes.

Actualizar esta guía con fecha y evidencia cuando se cumplan esas pruebas. No reemplazar «pendiente» por «funciona» solo porque el build pase o el Worker responda a `/health`.

## Validación de 0.6.2 (15/11/2026)

104 pruebas de escritorio aprobadas, incluyendo el control de UID remoto antes de subir datos, comprobación sin transferencia, estados de sesión y OAuth/PKCE mediante servidor local de prueba. Pruebas adicionales de `configure-auth.ps1` verifican rechazo de otro proyecto/tipo de cliente, HTTPS y preservación del archivo ante errores. La primera ejecución dentro del sandbox no pudo abrir el socket loopback; al ejecutar las mismas pruebas con acceso local permitido pasaron completas.

Esto no certifica Google real, correo real ni escritura/descarga en Neon. Sigue pendiente recibir/configurar el JSON OAuth del proyecto correcto y completar la prueba con una cuenta del equipo. No se crearon usuarios reales ni se enviaron obras al servidor durante estas pruebas.

## Cuenta y seguridad: 0.6.3 (15/11/2026)

Mi cuenta conserva login, registro, Google y recuperación, y añade el envío de enlace de cambio para una sesión con proveedor de contraseña. Se comprueba identidad en línea antes del envío; Google solo se administra en Google. 109 pruebas de escritorio aprobadas, incluyendo rechazo de identidad distinta/revocada, proveedor sin contraseña, sesión recordada y mapeo de proveedores Firebase. Vistas de acceso y seguridad revisadas en claro y oscuro sin conexiones reales.

El mockup ahora incluye un recorrido de cuenta **solo visual**, autorizado posteriormente por Joseph, con licencia y escenarios de suscripción de ejemplo. No usa Firebase ni persiste credenciales; no confundirlo con implementación comercial. Sigue pendiente la prueba real de autenticación/correos y Google con el JSON correcto. Ver [Firebase y cuentas](FIREBASE-Y-CUENTAS.md).
