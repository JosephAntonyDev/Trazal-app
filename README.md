# Trazal Desktop

Primera base ejecutable del producto, iniciada el 13 de noviembre de 2026 con autorización de Joseph. **C# + WPF + .NET Framework 4.8 + SQLite local.** Conserva la identidad amarilla y los flujos del mockup React, con controles nativos y sin WebView.

Es una vista previa funcional; **no tiene todavía todas las funciones del mockup** ni está aprobada para presupuestos reales.

## Estado del equipo y conexión (15/11/2026)

El backend 0.6.0 ya está desplegado en `https://trazal-api.trazaldev.workers.dev`, con Firebase `trazal-773ba` e Hyperdrive hacia Neon. `/health` y el rechazo de acceso sin sesión están comprobados; el paquete local 0.6.4 incluye el origen y permite comprobar la conexión. Falta probar login y sincronización real desde el escritorio.

- [Guía de incorporación y conexión](docs/CONEXION-Y-CONTINUIDAD.md).
- [Firebase, Google y separación de cuentas](docs/FIREBASE-Y-CUENTAS.md).
- [Estado completo de infraestructura y procedimiento](https://github.com/JosephAntonyDev/Trazal-API-TS/blob/main/docs/ESTADO-Y-CONTINUIDAD.md).

## Cuenta y seguridad (0.6.3)

En **Mi cuenta** están inicio de sesión, registro, recuperación y Google. Con una sesión comprobada de correo/contraseña se puede solicitar el enlace para cambiarla en el navegador. Las cuentas solo Google muestran dónde administrar su acceso. Los proveedores se vuelven a verificar antes del envío. 109 pruebas aprobadas con servicios simulados; correos y OAuth reales siguen pendientes. La cuenta no activa una licencia comercial: esa parte sigue por implementar.

## Abrir la vista previa

Ejecuta `artifacts/Trazal-0.6.4-preview/Trazal.exe` después de compilar. Para compartir con el equipo, extrae **todo** `artifacts/Trazal-0.6.4-preview.zip`; conserva las DLL y subcarpetas x86/x64. No es un instalador y no incorpora .NET Framework.

Requiere **.NET Framework 4.8**. El destino previsto es Windows 7 **SP1**, tanto x86 como x64 mediante AnyCPU y los binarios SQLite correspondientes. La compilación y las pruebas realizadas en el equipo de desarrollo **no certifican Windows 7**: hay que comprobar runtime, DLL nativa y renderizado en el equipo real. RAM de diseño: 4 GB; arquitectura del ingeniero, SP1 y rendimiento pendientes de confirmar. [Requisitos de Microsoft](https://learn.microsoft.com/en-us/dotnet/framework/get-started/system-requirements).

## Gastos por obra (0.2.1)

Dentro del presupuesto, abre **Gastos de la obra**. Permite crear y editar registros por fecha elegida en calendario, categoría, descripción, importe sin IVA y tasa de IVA opcional. El IVA vacío se guarda como 0 %, sin impuesto adicional. Incluye Servicios, Gastos de oficina, Gastos de oficina de campo y Depreciación, mantenimiento y rentas, inspirados en los encabezados del Excel revisado; no incorpora datos ni nombres del cliente. Filtro por categoría, subtotales sin IVA y totales filtrados con IVA. Funciona sin conceptos ni programa aprobado. Los gastos no modifican el presupuesto ni generan avance.

Al abrir una base v1, v2 o v3, se crea primero una copia `trazal.db.v<versión>-<id>.trazal` junto a la base y luego se migra en una transacción. Usa la versión nueva para seguir trabajando: las anteriores no admiten el esquema v4. Los respaldos completos incluyen gastos, programa e historial de avances.

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

Siguen pendientes licencias, actualizaciones, instalador y la validación de Firebase con un proyecto real. Curva S, PDF/Excel y restauración ya están implementados. La vista previa no carga automáticamente los datos del navegador.

## Desarrollo

Herramientas: Windows moderno, Visual Studio 2022 o Build Tools con MSBuild y componentes WPF/.NET desktop. El equipo actual permite compilar con Build Tools y las tareas WPF del Framework. Para trabajar con diseñador, instalar la carga de escritorio .NET del IDE bajo una licencia aplicable.

```powershell
.\build.ps1 -Test -Package
```

La compilación del script se guarda en `build/output/Release/app` y las pruebas en `build/output/Release/tests`, separadas del ejecutable que pueda estar abierto desde Visual Studio. Para una revisión visual aislada: `build/output/Release/tests/Trazal.Tests.exe --ui-preview .qa/revision`. Esa ventana usa una obra ficticia y sus propios datos.

La primera ejecución descarga **paquetes oficiales de NuGet en `build/packages`**, fijados por versión y SHA-256; las posteriores pueden usar la caché sin internet. No instala herramientas globales ni usa un .NET SDK moderno. Referencias de compilación 4.8 a través de NuGet; runtime del cliente por separado. Abre `Trazal.sln` después de preparar las dependencias y selecciona Trazal.Desktop como proyecto de inicio.

Las pruebas ejecutables verifican importación, cálculos, programa, avances, reportes, respaldos, sincronización e identidad. Incluyen renovación concurrente, aislamiento de cuentas, DPAPI, contrato HTTP de Firebase con respuestas de prueba y el flujo local OAuth/PKCE de Google. Se ejecutan en una carpeta temporal aislada y no modifican los datos del usuario; no sustituyen las pruebas con servicios reales.

## Estructura

| Proyecto | Responsabilidad |
| --- | --- |
| Trazal.Core | Entidades, invariantes y cálculos `decimal`; sin WPF/SQLite. |
| Trazal.Data | Repositorio SQLite, esquema versionado, transacciones y copia consistente. |
| Trazal.Desktop | XAML, estilos semánticos claro/oscuro, MVVM y comandos. |
| Trazal.Tests | Casos de aceptación ejecutables, sin dependencias de un servicio. |

Los precios/cantidades se persisten como texto decimal invariante para evitar pérdida por coma flotante. Se parametriza todo SQL. El esquema tiene `user_version=4` y se rechazan versiones futuras. La base activa vive en `%LOCALAPPDATA%\Trazal\Preview\trazal.db`; no se escribe junto al EXE ni se requiere administrador. `TRAZAL_DATA_DIR` permite aislar datos de QA.

**Decisión provisional de esta base:** una base SQLite del espacio local, con obras separadas por ID y claves foráneas. El plan anterior proponía una base por obra; antes de estabilizar el formato evaluaremos exportación individual/migración. El respaldo `.trazal` actual copia el espacio completo, incluido el perfil, no tiene cifrado y no es el JSON del mockup. No editar directamente la base activa con aplicaciones de sincronización.

Las tablas virtualizan filas/columnas. Los listados iniciales aún consultan todas las obras y agregan cantidades/precios para sus totales; **paginación y agregados persistidos quedan pendientes** antes de validar volúmenes grandes. No se han demostrado todavía los objetivos de memoria o arranque en 4 GB.

## Curva S, reportes y restauración (0.4.0)

En una obra abre **Curva S y reportes**. Selecciona el corte con el calendario. El programa azul conserva toda la distribución, incluso las fechas posteriores al corte. Ejecutado gris y gastos rojos terminan en el corte. Las tres series son acumuladas sin IVA, escalonadas por fecha registrada; pasar el puntero muestra valores y **Detalle por fecha** ofrece la tabla accesible.

- PV: suma del importe de cada concepto multiplicado por su cantidad programada acumulada / cantidad contratada.
- EV: mismo cálculo con la última cantidad ejecutada acumulada; nunca se suman mediciones acumuladas. El porcentaje global se pondera por importe, no es el promedio simple de porcentajes.
- AC: todos los gastos de esta obra fechados hasta el corte, sin IVA.
- SPI = EV/PV; CPI = EV/AC. Sin denominador se muestra **No disponible**; sin línea base fijada no se presentan índices. CPI es orientativo: precios con utilidad y compras anticipadas no son una base de costo devengado. No representa utilidad contable. [Referencia de las fórmulas](https://www.pmi.org/learning/library/practical-calculation-schedule-variance-7028).
- Se redondea el acumulado por concepto a dos decimales, como en la vista de programa, para conservar el total del presupuesto.

**Exportar PDF** crea un documento paginado con gráfica, resumen, presupuesto, período programado por concepto, historial de avances al corte, gastos y totales por categoría. **Exportar Excel** agrega el detalle de los puntos de la curva y el programa diario completo en ocho hojas, con fechas e importes tipados, claves de texto, filtros y encabezados congelados. Son instantáneas de consulta; editar el archivo no modifica Trazal. El PDF incluye la gráfica; la hoja Curva S de Excel contiene los valores, sin gráfico incrustado. No requieren Office ni internet para exportar. El archivo anterior se reemplaza únicamente tras completar la escritura.

**Restaurar respaldo** está en el menú lateral. En ventanas compactas, pulsa **Respaldos → Restaurar un respaldo**. Selecciona un `.trazal`, revisa obras, conceptos, gastos, avances y perfil, y confirma el reemplazo. Se reemplaza todo el espacio local, no se combinan obras. Se verifica integridad, esquema, referencias y cantidades antes de aplicar. Los respaldos v1/v2 migran en una copia temporal; el original no se modifica. Antes del cambio se conserva `Antes-de-restaurar-<fecha>-<id>.trazal` junto a la base activa; la ruta aparece en la barra de estado. Cancelar no altera los datos. Mantén copias también fuera del disco del equipo.

Prueba visual aislada: `build/output/Release/tests/Trazal.Tests.exe --reports-ui .qa/reports`. Usa datos ficticios y genera muestras PDF/XLSX. Pruebas adicionales cubren corte histórico, adelanto con sobrecosto indicativo, denominadores cero, límite del 100 %, archivos exportados, fallos de escritura, restauración y copias anteriores.

## Backend y sincronización (0.5.0)

El backend TypeScript se desarrolla en el repositorio independiente [Trazal-API-TS](https://github.com/JosephAntonyDev/Trazal-API-TS). La carpeta `backend/` de este repositorio queda como copia histórica. Almacena documentos de obra versionados en PostgreSQL; el escritorio sigue funcionando sin conexión con SQLite.

La sección **Sincronización** muestra las obras del espacio activo y los conflictos detectados. En 0.6.0 el botón se habilita con una sesión comprobada, correo confirmado y origen del Worker configurado; sigue deshabilitado mientras falte esa configuración. No se simula una sesión ni se guardan tokens en SQLite.

Las pruebas incluyen dos SQLite comunicándose por HTTP con la API y el motor PostgreSQL de pruebas, reintentos tras perder una respuesta, persistencia de la cola y conflictos. Las descargas se aplican en una transacción, comprobando que no hubo una edición local durante la petición. No hay mezcla automática de versiones en conflicto; todavía falta su resolución visual.

El esquema local v4 agrega la identidad, estado de sincronización y cola durable. Al actualizar una base anterior, se conserva el respaldo previo. Después de abrirla con 0.5.0, no uses versiones anteriores sobre esa base. Al restaurar un respaldo se descarta su cola y vinculación de nube: se conservarán las obras, sin repetir envíos antiguos.

No se suben perfiles, preferencias ni archivos adjuntos. El piloto sincroniza documentos completos solo cuando cambian, con límite de 512 KiB por obra y cuotas documentadas en el backend. Los datos que exceden el límite siguen disponibles localmente. La sincronización automática y los deltas por registro quedan para la siguiente iteración después de medir las obras reales.

## Identidad modular (0.6.0)

**Mi cuenta** incorpora inicio de sesión, registro, recuperación, envío de verificación, comprobación del correo y cierre de sesión. El módulo `Trazal.Identity` separa dominio, casos de uso, puertos y adaptadores; la vista WPF y su composición viven en `Modules/Identity`. El resto del escritorio mantiene sus capas anteriores, pendiente de migración modular gradual.

Cada proyecto/UID abre un SQLite independiente, conservando el espacio local original. Recordar sesión es opcional y utiliza DPAPI del usuario de Windows. No se guardan contraseñas. Desde la versión 0.6.1 se incorpora **Continuar con Google** mediante navegador del sistema, PKCE y retorno loopback, con cancelación y caducidad. Requiere el cliente OAuth de tipo escritorio en el mismo proyecto Firebase. El Worker ya está publicado; el paquete local 0.6.4 ya incluye su origen. Los clones nuevos requieren configuración local; falta validar ambos proveedores y la sincronización con cuentas reales. [Pasos de configuración, flujo y arquitectura](docs/FIREBASE-Y-CUENTAS.md).

## Conexión al piloto (0.6.2)

**Sincronización → Comprobar conexión** valida el acceso autenticado al backend/base y que su UID corresponda al espacio local; no envía ni descarga obras. La pantalla explica si falta iniciar sesión, confirmar correo, comprobar la sesión o configurar el origen. Muestra el resultado de la operación y mensajes de red sin exponer excepciones internas. Sincronizar sigue siendo una acción manual separada.

`configure-auth.ps1` prepara la configuración local e importa opcionalmente el JSON OAuth de escritorio, verificando el proyecto antes de reemplazar el archivo. El ejemplo incluye el origen público. Credenciales y configuración real permanecen fuera de Git. Se aprobaron 104 pruebas de escritorio y las comprobaciones adicionales del script; la validación autenticada real sigue pendiente.

## Próximo hito

1. Probar en el Windows 7 SP1 real, incluyendo exportación PDF/XLSX y restauración. Registrar arquitectura, RAM, escala, rendimiento y errores. La compatibilidad no está certificada por compilar contra .NET 4.8.
2. Contrastar cifras con el ingeniero y el Excel autorizado; acordar redondeo, indirectos/utilidad y criterio contable de gastos.
3. Correcciones de avances con historial; ampliar formatos de importación y evaluar importación del programa original.
4. Configurar el origen publicado en el escritorio y validar Firebase Auth y lectura/escritura real mediante Worker/Hyperdrive/Neon; medir CPU/costos. Añadir resolución de conflictos y traslado explícito del espacio local a una cuenta.
5. Generar instalador, actualizaciones y licenciamiento; medir grandes catálogos antes de producción.

Los archivos del cliente quedan fuera del repositorio. Este código se mantiene en un repositorio local independiente; no se publica automáticamente en el repo público del mockup. Dependencias y términos: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Ventanas adaptables y Google configurado (0.6.4, 16/11/2026)

- La ventana inicial se limita al área de trabajo de Windows; mínimo nominal 680 × 440 unidades WPF. A menos de 1050 de ancho aparece **Menú** con obras, perfil, cuenta, sincronización y respaldos. La barra lateral oculta deja de recibir foco.
- Filtros y tarjetas se reorganizan por ancho disponible. En ventanas bajas, las pantallas de tablas conservan una altura útil y se recorren verticalmente; las tablas amplias mantienen su desplazamiento horizontal y anchos mínimos legibles. Las modales se limitan al espacio disponible y conservan sus acciones.
- Se revisaron renders de inicio, perfil, cuenta, presupuesto, gastos, programa y creación a 680, 911, 1024, 1366 y 1920 unidades de ancho, en ambos temas. 911 unidades sirve como aproximación del espacio de una pantalla 1366 con escala 150 %; no es una prueba de cambio de DPI real del sistema ni de Windows 7.
- Se recibió e importó el cliente OAuth Desktop del proyecto correcto **trazal-773ba** mediante `configure-auth.ps1`. El paquete local incluye la configuración ignorada por Git. El JSON y el secreto no se versionan; clones nuevos necesitan configurar su archivo local. Falta completar el login real con el usuario y comprobar sincronización autenticada.
