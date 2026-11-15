# Firebase y cuentas · preparación 0.6.0

## Lo que debes enviar

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
  "projectId": "id-del-proyecto",
  "apiOrigin": ""
}
```

`apiOrigin` se deja vacío hasta desplegar el Worker; posteriormente será su origen HTTPS, sin `/v1`. Se puede probar la cuenta Firebase antes de disponer de PostgreSQL. Reinicia Trazal después de configurar. Alternativas de desarrollo: variables de entorno `TRAZAL_FIREBASE_API_KEY`, `TRAZAL_FIREBASE_PROJECT_ID` y `TRAZAL_API_ORIGIN`. Las variables prevalecen sobre el archivo. El archivo real está ignorado en Git; el ejemplo contiene valores vacíos.

En el Worker, `FIREBASE_PROJECT_ID` debe ser el mismo proyecto. El backend está en [Trazal-API-TS](https://github.com/JosephAntonyDev/Trazal-API-TS); la carpeta antigua `backend/` de este repositorio es histórica y no debe desplegarse ni modificarse para continuar el desarrollo.

## Flujo del usuario

Entra a **Mi cuenta** en el menú; en ventanas compactas, entra al perfil y pulsa **Mi cuenta · acceso y sesión**.

- **Crear cuenta:** correo, contraseña y confirmación. Mínimo local de ocho caracteres; Firebase aplica además su política configurada. El botón permanece deshabilitado hasta completar los campos y hacer coincidir las contraseñas. La cuenta queda abierta tras registrarse.
- **Verificar correo:** pulsa **Enviar verificación**. Abre el enlace recibido en tu navegador y vuelve a Trazal para pulsar **Ya confirmé mi correo / comprobar sesión**. La verificación y la recuperación usan las páginas alojadas por Firebase; no se incrusta un navegador en Windows 7.
- **Iniciar sesión:** correo y contraseña; no se guardan contraseñas. Las solicitudes van directamente a Firebase por HTTPS, nunca al Worker ni a PostgreSQL.
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
Adapters/Windows/       Persistencia protegida con DPAPI
Adapters/Files/         Configuración y rutas de espacios
```

`Desktop/Modules/Identity/Presentation` contiene la vista WPF y su ViewModel. `Composition` conecta las implementaciones, selecciona SQLite y construye el cliente de sincronización. La aplicación de identidad solo conoce `IIdentityProvider`, `ISessionStore` e `IClock`; se prueba con adaptadores de prueba, sin Firebase real.

La API usa módulos hexagonales de identidad y sincronización. Los módulos previos de presupuesto/programa/reportes conservan por ahora sus capas Core/Data/Desktop; su migración modular completa queda pendiente. No se renombraron carpetas para simular que todo el escritorio ya está migrado.

## Validaciones y límites pendientes

Se probaron credenciales inválidas, renovación concurrente, sesión recordada, correo sin confirmar, cambio inesperado de UID, tokens rechazados, DPAPI, aislamiento de rutas, formularios y contrato HTTP mediante un adaptador de pruebas. Falta ejecutar el flujo completo contra tu proyecto real, verificar entrega de correos, restricciones de API key y la política de contraseñas elegida. No se implementaron Google OAuth, MFA, cambio de correo, eliminación de cuenta, traslado de obras ni resolución visual de conflictos.

Windows 7 sigue pendiente de validación real, incluyendo TLS, certificados raíz y exportaciones. No se relaja la validación de certificados. La verificación básica del Worker no consulta la revocación individual en cada petición; un ID token puede seguir siendo válido hasta expirar. El bloqueo `accounts.disabled` del servidor continúa disponible.

Fuentes oficiales: [configuración de Firebase](https://firebase.google.com/docs/web/setup), [correo y contraseña](https://firebase.google.com/docs/auth/web/password-auth), [API REST de autenticación](https://firebase.google.com/docs/reference/rest/auth), [claves de API](https://firebase.google.com/docs/projects/api-keys).
