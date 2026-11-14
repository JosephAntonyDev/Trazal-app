# Trazal Desktop

Primera base ejecutable del producto, iniciada el 13 de noviembre de 2026 con autorización de Joseph. **C# + WPF + .NET Framework 4.8 + SQLite local.** Conserva la identidad amarilla y los flujos del mockup React, con controles nativos y sin WebView.

Es una vista previa funcional; **no tiene todavía todas las funciones del mockup** ni está aprobada para presupuestos reales.

## Abrir la vista previa

Ejecuta `artifacts/Trazal-0.3.0-preview/Trazal.exe` después de compilar. Para compartir con el equipo, extrae **todo** `artifacts/Trazal-0.3.0-preview.zip`; conserva las DLL y subcarpetas x86/x64. No es un instalador y no incorpora .NET Framework.

Requiere **.NET Framework 4.8**. El destino previsto es Windows 7 **SP1**, tanto x86 como x64 mediante AnyCPU y los binarios SQLite correspondientes. La compilación y las pruebas realizadas en el equipo de desarrollo **no certifican Windows 7**: hay que comprobar runtime, DLL nativa y renderizado en el equipo real. RAM de diseño: 4 GB; arquitectura del ingeniero, SP1 y rendimiento pendientes de confirmar. [Requisitos de Microsoft](https://learn.microsoft.com/en-us/dotnet/framework/get-started/system-requirements).

## Gastos por obra (0.2.1)

Dentro del presupuesto, abre **Gastos de la obra**. Permite crear y editar registros por fecha elegida en calendario, categoría, descripción, importe sin IVA y tasa de IVA opcional. El IVA vacío se guarda como 0 %, sin impuesto adicional. Incluye Servicios, Gastos de oficina, Gastos de oficina de campo y Depreciación, mantenimiento y rentas, inspirados en los encabezados del Excel revisado; no incorpora datos ni nombres del cliente. Filtro por categoría, subtotales sin IVA y totales filtrados con IVA. Funciona sin conceptos ni programa aprobado. Los gastos no modifican el presupuesto ni generan avance.

Al abrir una base v1 o v2, se crea primero una copia `trazal.db.v<versión>-<id>.trazal` junto a la base y luego se migra en una transacción. Usa la versión nueva para seguir trabajando: las anteriores no admiten el esquema v3. Los respaldos completos incluyen gastos, programa e historial de avances.

## Importación, programa y avance (0.3.0)

1. Abre una obra y pulsa **Importar Excel**. Admite XLS/XLSX sin Excel instalado. Selecciona hoja, fila del encabezado, primera/última fila y las cinco columnas: clave, descripción, unidad, cantidad y precio unitario sin IVA.
2. Si el formato coloca el precio debajo del concepto, activa **Precio en la fila siguiente** y selecciona la columna del precio. No se importan automáticamente fechas, porcentajes ni fórmulas del programa.
3. Pulsa **Preparar vista previa**. Las filas sin clave empiezan desmarcadas; revisa las omitidas. Excluye títulos/totales; corrige en Excel los datos inválidos o claves repetidas y vuelve a cargar. Revisa precios y subtotal, marca la confirmación y pulsa **Importar selección**. La selección se guarda en una sola transacción; nunca reemplaza conceptos existentes.
4. Abre **Programa de obra**. Selecciona un concepto y distribuye su cantidad entre inicio y término. El reparto es uniforme por días naturales, incluidos fines de semana, con el residuo decimal en el último día. No estima rendimientos ni calendario laboral. Puedes agregar o ajustar cantidades diarias; 0 al editar quita ese día del borrador.
5. **Fijar programa** se habilita cuando todos los conceptos completan exactamente su cantidad contratada. Requiere confirmar la revisión. Protege catálogo, precios, cantidades y días programados. No existe reapertura ni reprogramación de esta línea base en esta versión. Las fechas extremas de la distribución definen el período de la obra; las penalizaciones siguen fuera de la app.
6. Abre **Avance real**, selecciona concepto y **Registrar avance**. Captura cantidad o porcentaje acumulado a una fecha, no la cantidad del día. La app conserva cada corte, impide fechas futuras, duplicados, disminuciones y exceso del 100 %. Los cortes retroactivos respetan los vecinos de ambos lados. Al completar, muestra el aviso del 100 % y deshabilita nueva captura al corte completo.
7. Cambia la **fecha de corte** para comparar programa y ejecución y revisar el historial del concepto. El ejecutado se valora al precio del presupuesto, sin IVA; no representa pagos ni gastos reales.

Los valores de fórmulas se leen de la última copia guardada por Excel: recalcula y guarda el archivo antes de importar. No se ejecutan macros ni se alteran los originales. Límites de la vista previa: 20 MB, 50 hojas, 10 000 filas por hoja, 256 columnas y un millón de celdas en total. La lectura del libro se realiza fuera del hilo de interfaz. Los precios numéricos conservan hasta cuatro decimales; los textos usan punto decimal y admiten miles agrupados como `1,250.50`. Los identificadores con máscara de ceros simples conservan esos ceros.

Pendientes específicos: correcciones de avances con trazabilidad, versiones de programa autorizadas, importación de la distribución histórica de Excel, calendarios laborales y revisión masiva de cortes. Por ahora cada fecha admite un corte por concepto y los cortes guardados no se editan ni eliminan.

Los selectores de fecha comparten un calendario de 332 px, días de 40 px, navegación por mes/año y colores adaptados al tema claro u oscuro. La fecha queda centrada verticalmente, sin entrada de texto libre. Los campos compactos conservan un ancho mínimo para mostrar la fecha completa.

## Funciona ahora

- Inicio con búsqueda sin distinguir acentos/mayúsculas por título, tipo y alcance; filtro por tipo.
- Crear y editar obras: solo título/tipo obligatorios, alcance e IVA opcionales. Las fechas se definen en el programa.
- Presupuesto manual por obra: clave, descripción, unidad, cantidad y precio final unitario sin IVA; edición y rechazo de claves duplicadas dentro de la misma obra.
- Cálculo decimal: importe por concepto redondeado a centavos, suma, IVA y total. IVA desconocido se distingue de 0 %. No hay desglose APU/indirectos/utilidad todavía.
- Perfil completo en otra vista, con validación de correo opcional. Guardar se deshabilita sin cambios; formularios nuevos exigen el mínimo válido.
- Tema claro/oscuro/sistema persistido, ayudas «?», navegación con teclado y diálogo que bloquea la vista de fondo.
- Guardado local transaccional y respaldo SQLite consistente, verificado con `integrity_check`, fuera del hilo de interfaz.

No están implementados aún: curva S, PDF, restauración guiada, licencias, Firebase, actualización ni instalador. La vista previa no carga automáticamente los datos del navegador.

## Desarrollo

Herramientas: Windows moderno, Visual Studio 2022 o Build Tools con MSBuild y componentes WPF/.NET desktop. El equipo actual permite compilar con Build Tools y las tareas WPF del Framework. Para trabajar con diseñador, instalar la carga de escritorio .NET del IDE bajo una licencia aplicable.

```powershell
.\build.ps1 -Test -Package
```

La compilación del script se guarda en `build/output/Release/app` y las pruebas en `build/output/Release/tests`, separadas del ejecutable que pueda estar abierto desde Visual Studio. Para una revisión visual aislada: `build/output/Release/tests/Trazal.Tests.exe --ui-preview .qa/revision`. Esa ventana usa una obra ficticia y sus propios datos.

La primera ejecución descarga **tres paquetes oficiales de NuGet en `build/packages`**, fijados por versión y SHA-256; las posteriores pueden usar la caché sin internet. No instala herramientas globales ni usa un .NET SDK moderno. Referencias de compilación 4.8 a través de NuGet; runtime del cliente por separado. Abre `Trazal.sln` después de preparar las dependencias y selecciona Trazal.Desktop como proyecto de inicio.

47 pruebas ejecutables verifican importación y selección de filas, archivos XLSX sintéticos, números, distribución, línea base inmutable, cortes retroactivos, límite del 100 %, persistencia, migraciones y respaldos. Se ejecutan en una carpeta temporal aislada y no modifican los datos del usuario.

## Estructura

| Proyecto | Responsabilidad |
| --- | --- |
| Trazal.Core | Entidades, invariantes y cálculos `decimal`; sin WPF/SQLite. |
| Trazal.Data | Repositorio SQLite, esquema versionado, transacciones y copia consistente. |
| Trazal.Desktop | XAML, estilos semánticos claro/oscuro, MVVM y comandos. |
| Trazal.Tests | Casos de aceptación ejecutables, sin dependencias de un servicio. |

Los precios/cantidades se persisten como texto decimal invariante para evitar pérdida por coma flotante. Se parametriza todo SQL. El esquema tiene `user_version=3` y se rechazan versiones futuras. La base activa vive en `%LOCALAPPDATA%\Trazal\Preview\trazal.db`; no se escribe junto al EXE ni se requiere administrador. `TRAZAL_DATA_DIR` permite aislar datos de QA.

**Decisión provisional de esta base:** una base SQLite del espacio local, con obras separadas por ID y claves foráneas. El plan anterior proponía una base por obra; antes de estabilizar el formato evaluaremos exportación individual/migración. El respaldo `.trazal` actual copia el espacio completo, incluido el perfil, no tiene cifrado y no es el JSON del mockup. No editar directamente la base activa con aplicaciones de sincronización.

Las tablas virtualizan filas/columnas. Los listados iniciales aún consultan todas las obras y agregan cantidades/precios para sus totales; **paginación y agregados persistidos quedan pendientes** antes de validar volúmenes grandes. No se han demostrado todavía los objetivos de memoria o arranque en 4 GB.

## Próximo hito

1. Ejecutar este paquete en el Windows 7 real: abrir, crear/editar, cerrar/reabrir, cambiar tema y respaldar. Registrar SP1, arquitectura, RAM, escala y errores.
2. Ampliar pruebas de formatos Excel y agregar PDF; generar instalador y probar actualización sin perder datos.
3. Añadir recuperación guiada como copia con validación; documentar esquema y migraciones antes del piloto.
4. Contrastar cálculos con Excel autorizado y acordar redondeo, utilidad/indirectos y significado de gastos.
5. Implementar curva S enlazando programa fijo, avances y gastos; añadir correcciones de cortes con historial.

Los archivos del cliente quedan fuera del repositorio. Este código se mantiene en un repositorio local independiente; no se publica automáticamente en el repo público del mockup. Dependencias y términos: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
