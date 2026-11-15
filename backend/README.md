# Trazal API · 0.5.0

> Copia histórica. El desarrollo y despliegue actuales están en [Trazal-API-TS](https://github.com/JosephAntonyDev/Trazal-API-TS). No continuar cambios ni desplegar desde esta carpeta.

Backend TypeScript para Cloudflare Workers. PostgreSQL conserva documentos JSONB de obra con revisiones; SQLite sigue siendo la base de trabajo del escritorio. No requiere un servidor Node permanente. El código está dentro de Trazal-app; no se ha desplegado ni conectado a cuentas reales.

## Qué funciona

- Verificación de Firebase ID tokens mediante RS256, claves públicas oficiales con caché, issuer, audience, expiración, issued-at, auth_time y UID. No acepta custom tokens ni tiene modo anónimo de escritura.
- Aislamiento por UID autenticado en cada consulta. El cliente no elige un propietario ni envía cadenas SQL.
- Subida y descarga de obras completas, línea base, avances y gastos; cantidades e importes viajan como cadenas decimales. La validación utiliza enteros escalados, no aritmética binaria para comparar cantidades.
- Revisión optimista: `If-Match` debe coincidir con la obra del servidor. Dos equipos no se sobrescriben silenciosamente. Una solicitud repetida con la misma clave y contenido conserva el resultado; una clave reutilizada con otro contenido se rechaza.
- Cursor por cuenta ordenado bajo bloqueo transaccional. El reloj se confirma junto con el documento para no omitir cambios entre transacciones concurrentes.
- Catálogo/programa fijados inmutables, avances existentes conservados, límite del 100 %, categorías y cantidades validadas.
- Cola SQLite durable, reintento después de perder una respuesta, descarga transaccional y detección de una edición local durante la descarga. Restaurar un `.trazal` borra cola/cursor/vinculación de la copia, evitando reproducir solicitudes antiguas.

## Desarrollo sin cuentas de nube

Requiere Node 22.12 o posterior en el equipo de desarrollo. Esto **no se instala en el Windows 7 del ingeniero**; allí corre únicamente C# / WPF / .NET Framework 4.8.

```powershell
npm ci
npm run typecheck
npm test
npm run build
```

`build` empaqueta con Wrangler `--dry-run`; no publica. La telemetría de Wrangler se desactiva solo para estos comandos y los logs se escriben en `.wrangler/logs`, ignorado por Git.

Las pruebas de datos usan PGlite: motor PostgreSQL embebido para validar SQL y transacciones, sin Docker ni datos reales. No se distribuye en el Worker. Las pruebas de autenticación firman JWT de prueba con claves generadas durante la ejecución. No se simula Google dentro del Worker publicado.

Para comprobar el contrato con el cliente real de .NET, compila primero las pruebas de escritorio desde la raíz y ejecuta:

```powershell
npm run test:desktop
```

Arranca un servidor de pruebas exclusivamente en `127.0.0.1`, crea PostgreSQL efímero y ejecuta dos SQLite separados a través de `CloudClient`. Prueba subida, descarga, actualización y conflicto. El autenticador fijo existe únicamente en ese script de pruebas; el entrypoint de producción usa Firebase.

`npm run dev` arranca el Worker local. `/health` funciona sin configuración; las rutas privadas responden 503 mientras falte Firebase/PostgreSQL. Para una conexión real local, copia `.env.example` a `.dev.vars` y configura un PostgreSQL de desarrollo y el proyecto Firebase. La URL de la base y los tokens nunca se guardan en Git.

## Despliegue pendiente

1. Crear PostgreSQL en el proveedor elegido, con TLS. El plan de Workers no incluye alojamiento PostgreSQL. Separar el usuario de migraciones del usuario de ejecución; este último solo necesita SELECT/INSERT/UPDATE/DELETE sobre `accounts`, `works`, `sync_requests` y acceso al esquema. No exponer la base directamente al escritorio.
2. Con `DATABASE_URL` en el entorno del equipo administrador, ejecutar `npm run migrate`. La migración usa transacción y bloqueo; no borra tablas y solo se aplica una vez. No correr migraciones desde peticiones HTTP.
3. Crear Hyperdrive apuntando a esa base con **query caching desactivado** para lecturas de sincronización consistentes. No hace falta contratar Durable Objects, KV ni R2 para este alcance.
4. Copiar `wrangler.jsonc` a `wrangler.production.jsonc` (ignorado por Git). Configurar `workers_dev: true` o una ruta propia y añadir:

```json
{
  "vars": { "FIREBASE_PROJECT_ID": "proyecto-real" },
  "hyperdrive": [{ "binding": "HYPERDRIVE", "id": "id-real-de-hyperdrive" }]
}
```

Son campos que se **añaden** al archivo copiado, conservando `main`, flags y cron. El ID del proyecto es público; la contraseña de PostgreSQL se guarda en Hyperdrive. `DATABASE_URL` es el fallback local, no debe ponerse en `vars` de producción. El escritorio no necesita CORS; añadir `ALLOWED_ORIGINS` solo para orígenes web concretos si se habilita un cliente web.

5. Iniciar sesión en Wrangler y ejecutar `npm run deploy` después de revisar el destino. Verificar `/health`, sesión Firebase real, PostgreSQL y límites de CPU antes de habilitar a un usuario.
6. Conectar el inicio de sesión del escritorio a Firebase, incluyendo renovación y protección de credenciales con Windows DPAPI. `CloudClient` recibe un proveedor de ID tokens en memoria; el constructor no guarda tokens. `ShellViewModel.ConfigureSync(...)` activa el botón únicamente después de integrar la sesión. **El login de escritorio todavía no está implementado.**

Para la verificación básica de ID tokens el Worker usa claves públicas, no necesita un JSON de cuenta de servicio. Todavía no se consulta revocación individual de Firebase: un token firmado es válido hasta expirar. `accounts.disabled` permite bloquear la sincronización de una cuenta de inmediato; la administración de ese campo es externa por ahora.

## API v1

Todas las rutas `/v1` requieren `Authorization: Bearer <Firebase ID token>`. Respuestas privadas con `Cache-Control: no-store`. Errores incluyen `error.code` y `error.message`; un conflicto de revisión también entrega la revisión remota. Cada respuesta lleva `X-Request-Id`, sin registrar documentos, contraseñas ni tokens.

| Ruta | Resultado |
| --- | --- |
| GET `/health` | Servicio y versión, sin consulta a PostgreSQL. |
| GET `/v1/me` | UID verificado y habilitación de la cuenta. |
| GET `/v1/sync/changes?cursor=0` | Hasta 100 IDs/revisiones, `nextCursor` y `hasMore`; sin documentos completos. |
| GET `/v1/works/{id}` | `{ revision, document }` y ETag. |
| PUT `/v1/works/{id}` | Documento schemaVersion 1. Cabeceras `If-Match: "0"` para crear o revisión conocida para editar, e `Idempotency-Key` de 32 caracteres hexadecimales. Retorna `{ revision }`. |

Las revisiones/cursor son cadenas enteras para conservar precisión. El ID local de 32 hexadecimales se conserva. Los perfiles, temas, archivos Excel originales, PDFs y adjuntos no se suben.

## Decisiones y límites del piloto

- Sincronización manual de las obras del espacio local: no hay consultas continuas ni una petición por pulsación de teclado. Obras sin cambios no se vuelven a subir. La unidad de conflicto y escritura es la obra completa; **aún no hay deltas por registro**. Es una primera versión conservadora para no mezclar presupuestos y líneas base de equipos distintos.
- Máximo 512 KiB por documento, 100 obras por cuenta, 2,000 conceptos, 10,000 días programados, 5,000 avances y 5,000 gastos por obra; rige también el límite de bytes. Al superar un límite, la obra permanece en SQLite. Debemos medir obras reales antes de ampliar límites o pasar a deltas.
- El cron elimina hasta 1,000 recibos de idempotencia de más de 30 días cada hora. No borra obras. Reintentar después de esa ventana mantiene la protección por revisión y puede requerir revisar un conflicto.
- 120 operaciones exitosas por minuto/cuenta; respuesta 429 con `Retry-After`. No es una protección global contra abuso del endpoint. Revisar métricas reales y reglas de acceso antes de abrir registro público.
- Actualmente cada usuario tiene un espacio propio; no hay organizaciones, permisos entre compañeros, invitaciones, eliminaciones remotas ni resolución visual de conflictos. Cuando hay conflicto, una copia queda en cada lado y la obra deja de sincronizarse hasta implementar/revisar la resolución.
- Un espacio SQLite se vincula a un UID y un servidor. Cambiar de cuenta exige un espacio separado; se rechaza reutilizar la misma cola con otra identidad.
- Workers Free tiene actualmente 100,000 peticiones/día y 10 ms de CPU por petición; Hyperdrive Free tiene 100,000 consultas/día. Autenticación, JSON y validación deben medirse en Cloudflare: las pruebas locales **no certifican** esos 10 ms. PostgreSQL tiene costos y límites propios. No se habilitaron planes de pago ni servicios remotos durante este desarrollo.

Fuentes: [Workers Free](https://developers.cloudflare.com/workers/platform/limits/), [Hyperdrive](https://developers.cloudflare.com/hyperdrive/platform/pricing/), [Postgres.js en Workers](https://developers.cloudflare.com/hyperdrive/examples/connect-to-postgres/postgres-drivers-and-libraries/postgres-js/), [Firebase ID tokens](https://firebase.google.com/docs/auth/admin/verify-id-tokens).
