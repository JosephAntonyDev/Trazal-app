# Trazal Desktop

Primera base ejecutable del producto, iniciada el 13 de noviembre de 2026 con autorización de Joseph. **C# + WPF + .NET Framework 4.8 + SQLite local.** Conserva la identidad amarilla y los flujos del mockup React, con controles nativos y sin WebView.

Es una vista previa funcional; **no tiene todavía todas las funciones del mockup** ni está aprobada para presupuestos reales.

## Abrir la vista previa

Ejecuta `artifacts/Trazal-0.1.0-preview/Trazal.exe` después de compilar. Para compartir con el equipo, extrae **todo** `artifacts/Trazal-0.1.0-preview.zip`; conserva las DLL y subcarpetas x86/x64. No es un instalador y no incorpora .NET Framework.

Requiere **.NET Framework 4.8**. El destino previsto es Windows 7 **SP1**, tanto x86 como x64 mediante AnyCPU y los binarios SQLite correspondientes. La compilación y las pruebas realizadas en el equipo de desarrollo **no certifican Windows 7**: hay que comprobar runtime, DLL nativa y renderizado en el equipo real. RAM de diseño: 4 GB; arquitectura del ingeniero, SP1 y rendimiento pendientes de confirmar. [Requisitos de Microsoft](https://learn.microsoft.com/en-us/dotnet/framework/get-started/system-requirements).

## Funciona ahora

- Inicio con búsqueda sin distinguir acentos/mayúsculas por título, tipo y alcance; filtro por tipo.
- Crear y editar obras: solo título/tipo obligatorios, alcance e IVA opcionales. Fechas pendientes del módulo Programa.
- Presupuesto manual por obra: clave, descripción, unidad, cantidad y precio final unitario sin IVA; edición y rechazo de claves duplicadas dentro de la misma obra.
- Cálculo decimal: importe por concepto redondeado a centavos, suma, IVA y total. IVA desconocido se distingue de 0 %. No hay desglose APU/indirectos/utilidad todavía.
- Perfil completo en otra vista, con validación de correo opcional. Guardar se deshabilita sin cambios; formularios nuevos exigen el mínimo válido.
- Tema claro/oscuro/sistema persistido, ayudas «?», navegación con teclado y diálogo que bloquea la vista de fondo.
- Guardado local transaccional y respaldo SQLite consistente, verificado con `integrity_check`, fuera del hilo de interfaz.

No están implementados aún: importación XLS/XLSX, programa y línea base, avances, gastos, curva S, PDF, restauración guiada, licencias, Firebase, actualización ni instalador. Se muestran como «En desarrollo», sin botones falsos. La vista previa no carga automáticamente los datos del navegador.

## Desarrollo

Herramientas: Windows moderno, Visual Studio 2022 o Build Tools con MSBuild y componentes WPF/.NET desktop. El equipo actual permite compilar con Build Tools y las tareas WPF del Framework. Para trabajar con diseñador, instalar la carga de escritorio .NET del IDE bajo una licencia aplicable.

```powershell
.\build.ps1 -Test -Package
```

La primera ejecución descarga **dos paquetes oficiales de NuGet en `build/packages`**, fijados por versión y SHA-256; las posteriores pueden usar la caché sin internet. No instala herramientas globales ni usa un .NET SDK moderno. Referencias de compilación 4.8 a través de NuGet; runtime del cliente por separado. Abre `Trazal.sln` después de preparar las dependencias y selecciona Trazal.Desktop como proyecto de inicio.

22 pruebas ejecutables verifican números, reglas, persistencia, respaldo/reapertura, separación de precios, validación y cambios en formularios. Se ejecutan en una carpeta temporal aislada y no modifican los datos del usuario.

## Estructura

| Proyecto | Responsabilidad |
| --- | --- |
| Trazal.Core | Entidades, invariantes y cálculos `decimal`; sin WPF/SQLite. |
| Trazal.Data | Repositorio SQLite, esquema versionado, transacciones y copia consistente. |
| Trazal.Desktop | XAML, estilos semánticos claro/oscuro, MVVM y comandos. |
| Trazal.Tests | Casos de aceptación ejecutables, sin dependencias de un servicio. |

Los precios/cantidades se persisten como texto decimal invariante para evitar pérdida por coma flotante. Se parametriza todo SQL. El esquema tiene `user_version=1` y se rechazan versiones futuras. La base activa vive en `%LOCALAPPDATA%\Trazal\Preview\trazal.db`; no se escribe junto al EXE ni se requiere administrador. `TRAZAL_DATA_DIR` permite aislar datos de QA.

**Decisión provisional de esta base:** una base SQLite del espacio local, con obras separadas por ID y claves foráneas. El plan anterior proponía una base por obra; antes de estabilizar el formato evaluaremos exportación individual/migración. El respaldo `.trazal` actual copia el espacio completo, incluido el perfil, no tiene cifrado y no es el JSON del mockup. No editar directamente la base activa con aplicaciones de sincronización.

Las tablas virtualizan filas/columnas. Los listados iniciales aún consultan todas las obras y agregan cantidades/precios para sus totales; **paginación y agregados persistidos quedan pendientes** antes de validar volúmenes grandes. No se han demostrado todavía los objetivos de memoria o arranque en 4 GB.

## Próximo hito

1. Ejecutar este paquete en el Windows 7 real: abrir, crear/editar, cerrar/reabrir, cambiar tema y respaldar. Registrar SP1, arquitectura, RAM, escala y errores.
2. Probar dependencias XLS/XLSX y PDF con archivos sintéticos; generar instalador y probar actualización sin perder datos.
3. Añadir recuperación guiada como copia con validación; documentar esquema y migraciones antes del piloto.
4. Contrastar cálculos con Excel autorizado y acordar redondeo, utilidad/indirectos y significado de gastos.
5. Implementar programa fijo, avances hasta 100 %, gastos y curva S, en ese orden.

Los archivos del cliente quedan fuera del repositorio. Este código se mantiene en un repositorio local independiente; no se publica automáticamente en el repo público del mockup. Dependencias y términos: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
