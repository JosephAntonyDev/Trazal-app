# Firebase y cuentas · escritorio 0.6.2

Actualizado el 15/11/2026. Firebase `trazal-773ba` ya existe y tiene Correo/contraseña y Google habilitados. El Worker está publicado en `https://trazal-api.trazaldev.workers.dev`; el flujo autenticado real sigue pendiente. Consulta [la guía del equipo](CONEXION-Y-CONTINUIDAD.md).

## Preparar Firebase en una instalación nueva

1. Abre [Firebase Console](https://console.firebase.google.com/) y crea un proyecto **Trazal**, o elige el proyecto que ya tengas. Analytics no es necesario para este flujo.
2. En **Authentication → Comenzar → Método de acceso**, habilita **Correo electrónico/contraseña**. El acceso sin contraseña mediante enlace no se utiliza en esta versión.
3. En **Configuración del proyecto → General → Tus apps**, registra una app **Web (`</>`)** llamada **Trazal Desktop**. No es necesario configurar Hosting. Usamos su configuración con la API REST desde C#; no cambia la tecnología del escritorio.
4. En la configuración de esa app, copia el bloque `firebaseConfig`, o únicamente sus campos `apiKey` y `projectId`. `authDomain` y `appId` pueden conservarse para otras integraciones, pero este cliente REST no los necesita.
5. Comparte esos dos valores y confirma que habilitaste Correo electrónico/contraseña. No envíes una contraseña de usuario, refresh tokens, claves privadas ni JSON de cuentas de servicio.

La clave de la app Firebase identifica el proyecto; no concede permisos de administrador. La API de Trazal verifica por separado el token y el UID. No necesitamos crear Firestore: las obras usan SQLite y PostgreSQL.

## Configuración del desarrollador

Copia `authsettings.example.json` a `authsettings.json` **junto a Trazal.exe** y rellena:

```json
{
  "apiKey": "valor-de-firebase",
  "projectId": "trazal-773ba",
  "apiOrigin": "https://trazal-api.trazaldev.workers.dev",
  "googleDesktopClientId": "",
  "googleDesktopClientSecret": ""
}
```

`apiOrigin` utiliza ahora el origen publicado indicado arriba, sin `/v1`, `/health` ni barra final. Dejarlo vacío mantiene deshabilitada la sincronización. Se puede probar la cuenta Firebase antes de disponer de PostgreSQL. Reinicia Trazal después de configurar. Alternativas de desarrollo: variables de entorno `TRAZAL_FIREBASE_API_KEY`, `TRAZAL_FIREBASE_PROJECT_ID` y `TRAZAL_API_ORIGIN`. Las variables prevalecen sobre el archivo. El archivo real está ignorado en Git; el ejemplo incluye proyecto y origen públicos, y deja vacíos la API key y los campos OAuth.

En el Worker, `FIREBASE_PROJECT_ID` debe ser el mismo proyecto. El backend está en [Trazal-API-TS](https://github.com/JosephAntonyDev/Trazal-API-TS); la carpeta antigua `backend/` de este repositorio es histórica y no debe desplegarse ni modificarse para continuar el desarrollo.

## Preparar la configuración desde PowerShell (0.6.2)

Si ya existe `authsettings.json` en la raíz, ejecuta `./configure-auth.ps1` para conservar sus credenciales y configurar el origen publicado. Después compila y reinicia la aplicación. Para importar Google:

```powershell
.\configure-auth.ps1 -OAuthJson 'C:\ruta\client_secret_trazal_desktop.json'
.\build.ps1 -Test -Package
```

El script exige un cliente `installed` del mismo `project_id`, rechaza clientes Web o de otro proyecto y valida el origen HTTPS. No imprime las credenciales y valida antes de reemplazar atómicamente el archivo. No modifica Firebase, Neon ni los datos de usuario. Si se prepara un archivo desde cero, completar primero `apiKey` localmente desde Firebase o pasar el parámetro `-FirebaseApiKey` (identificador público de la app Web).

## Flujo del usuario

Entra a **Mi cuenta** en el menú; en ventanas compactas, entra al perfil y pulsa **Mi cuenta · acceso y sesión**.

- **Crear cuenta:** correo, contraseña y confirmación. Mínimo local de ocho caracteres; Firebase aplica además su política configurada. El botón permanece deshabilitado hasta completar los campos y hacer coincidir las contraseñas. La cuenta queda abierta tras registrarse.
- **Verificar correo:** pulsa **Enviar verificación**. Abre el enlace recibido en tu navegador y vuelve a Trazal para pulsar **Ya confirmé mi correo / comprobar sesión**. La verificación y la recuperación usan las páginas alojadas por Firebase; no se incrusta un navegador en Windows 7.
- **Iniciar sesión:** correo y contraseña; no se guardan contraseñas. Las solicitudes van directamente a Firebase por HTTPS, nunca al Worker ni a PostgreSQL.
- **Continuar con Google:** abre el navegador predeterminado, permite elegir cuenta y regresa a Trazal mediante un puerto temporal en `127.0.0.1`. Puedes cancelar desde la app; caduca a los tres minutos. No necesita contraseña en Trazal. El botón permanece deshabilitado mientras falte configurar el cliente OAuth de escritorio.
- **Olvidé mi contraseña:** envía el enlace de recuperación. El resultado no revela si ese correo está registrado.
- **Recordar mi sesión:** opcional y desmarcado inicialmente. Guarda únicamente UID, correo y refresh token en un archivo cifrado con Windows DPAPI, asociado al usuario de Windows y al proyecto. Los ID tokens se mantienen en memoria.
- **Sin conexión:** una sesión recordada puede abrir su espacio local. El estado indica que falta comprobarla en línea. Para habilitar sincronización después de reiniciar, pulsa **comprobar sesión**; requiere red, correo confirmado y Worker configurado. Un token caducado se renueva al sincronizar, con exclusión mutua para evitar renovaciones duplicadas.
- **Cerrar sesión:** elimina la sesión recordada y vuelve al espacio local original. No elimina obras.

## Separación de datos

El espacio original sigue en `%LOCALAPPDATA%\Trazal\Preview\trazal.db`. Cada cuenta usa `accounts/<SHA256 del proyecto y UID>/trazal.db` dentro de ese directorio. La carpeta se deriva de un hash; el UID nunca se utiliza directamente como ruta. Cada espacio tiene sus obras, perfil, preferencias y respaldos. El perfil local es independiente del correo de acceso; editarlo no cambia el correo en Firebase.

No se copian ni se suben automáticamente las obras del espacio original al iniciar sesión. Un futuro traslado explícito debe tener vista previa y respaldo. La sesión protegida se guarda fuera de SQLite; un respaldo `.trazal` no contiene tokens. Cerrar sesión no cifra ni elimina las bases SQLite: quien tiene acceso a los archivos del usuario de Windows puede leerlas. La separación es funcional, no una bóveda de archivos.

## Arquitectura

`Trazal.Identity` es el módulo de identidad, sin referencia a WPF ni a Trazal.Data:

```text
Domain/                 Identidad, credenciales, errores y resultados
Application/            Casos de uso AccountSession y puertos
Adapters/Firebase/      HTTP REST, interpretación de respuestas y errores
Adapters/Google/        Navegador del sistema, callback loopback y PKCE S256
Adapters/Windows/       Persistencia protegida con DPAPI
Adapters/Files/         Configuración y rutas de espacios
```

`Desktop/Modules/Identity/Presentation` contiene la vista WPF y su ViewModel. `Composition` conecta las implementaciones, selecciona SQLite y construye el cliente de sincronización. La aplicación de identidad solo conoce `IIdentityProvider`, `ISessionStore` e `IClock`; se prueba con adaptadores de prueba, sin Firebase real.

La API usa módulos hexagonales de identidad y sincronización. Los módulos previos de presupuesto/programa/reportes conservan por ahora sus capas Core/Data/Desktop; su migración modular completa queda pendiente. No se renombraron carpetas para simular que todo el escritorio ya está migrado.

## Validaciones y límites pendientes

Se probaron credenciales inválidas, renovación concurrente, sesión recordada, correo sin confirmar, cambio inesperado de UID, tokens rechazados, DPAPI, aislamiento de rutas, formularios y contrato HTTP mediante un adaptador de pruebas. Google incluye pruebas del callback HTTP local, state incorrecto, PKCE, cancelación e intercambio Firebase. Falta ejecutar el flujo completo contra tu proyecto real, verificar entrega de correos, restricciones de API key y la política de contraseñas elegida. No se implementaron MFA, vinculación manual de proveedores, cambio de correo, eliminación de cuenta, traslado de obras ni resolución visual de conflictos.

Windows 7 sigue pendiente de validación real, incluyendo TLS, certificados raíz y exportaciones. No se relaja la validación de certificados. La verificación básica del Worker no consulta la revocación individual en cada petición; un ID token puede seguir siendo válido hasta expirar. El bloqueo `accounts.disabled` del servidor continúa disponible.

Fuentes oficiales: [configuración de Firebase](https://firebase.google.com/docs/web/setup), [correo y contraseña](https://firebase.google.com/docs/auth/web/password-auth), [API REST de autenticación](https://firebase.google.com/docs/reference/rest/auth), [claves de API](https://firebase.google.com/docs/projects/api-keys).

## Configurar Google para escritorio

El proveedor Google ya está habilitado en Firebase. El JSON recibido anteriormente pertenecía a `trazal-773ba-514820`, diferente de `trazal-773ba`; no se aplicó. Sigue pendiente obtener/configurar el cliente Desktop correcto y probarlo. Además hace falta el cliente OAuth nativo; la configuración de la app Web que compartiste no contiene estos campos.

1. Abre [Google Auth Platform → Clients](https://console.cloud.google.com/auth/clients?project=trazal-773ba). Confirma el proyecto **trazal-773ba** arriba.
2. Si pide configurar la plataforma, usa nombre **Trazal**, correo de soporte y contacto del equipo. En **Audience / Público**, elige externo si no es una app exclusiva de tu organización. Para el piloto conserva modo de pruebas y añade los correos del equipo como usuarios de prueba cuando corresponda.
3. **Create client → Desktop app / Aplicación de escritorio**. Nombre: **Trazal Windows**. No reutilices ni cambies el cliente Web creado por Firebase.
4. Descarga el JSON del cliente al crearlo y guárdalo localmente como `client_secret_trazal_desktop.json`. No es un JSON de cuenta de servicio. Está excluido de Git mediante `client_secret*.json`.
5. Del bloque `installed`, copia `client_id` y `client_secret` en `googleDesktopClientId` y `googleDesktopClientSecret` de `authsettings.json`, junto al ejecutable. También puedes configurar el archivo en la raíz del repo de escritorio: el build lo copia al paquete local. Reinicia Trazal. No hace falta copiar el JSON completo dentro de la app.
6. Prueba **Mi cuenta → Continuar con Google**. La app pide solo `openid email`; no acceso a Drive, archivos ni contactos. Firebase valida la credencial Google y entrega sus propios ID/refresh tokens. La sesión y el espacio SQLite se identifican por el UID Firebase, igual que correo/contraseña.

El `client_secret` de un cliente instalado viaja con la aplicación y no puede considerarse confidencial; PKCE protege el canje del código. No uses aquí el secreto de un cliente Web ni una clave privada de administrador. Las credenciales Google temporales no se persisten; la sesión Firebase recordada mantiene la protección DPAPI existente.

Si Firebase rechaza el ID OAuth, verifica que lo creaste en el mismo proyecto. La lista de clientes externos permitidos del proveedor Google se usa cuando un cliente pertenece a otro proyecto; no sustituye elegir el proyecto correcto. Si hay conflicto de proveedor/correo, la app no copia ni fusiona obras: muestra un error para usar el acceso original y revisar la vinculación.

Revisar en el Windows 7 real que el navegador instalado todavía pueda abrir el acceso de Google y que TLS/certificados funcionen. El flujo no utiliza WebView2 ni el navegador integrado de Internet Explorer; no se garantiza el soporte de Google a navegadores antiguos.

Referencia: [OAuth para escritorio](https://developers.google.com/identity/protocols/oauth2/native-app).
