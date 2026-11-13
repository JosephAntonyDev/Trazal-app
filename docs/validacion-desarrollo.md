# Validación de la vista previa 0.1

Fecha: 13 de noviembre de 2026. Entorno: equipo de desarrollo actual, **no Windows 7**.

## Automatizada

`build.ps1 -Test -Package`: compilación Release y 22 casos aprobados, sin advertencias de compilación.

Casos: obra mínima, título/tipo obligatorios, IVA dentro de rango, distinción entre tasa pendiente y cero, cálculo exacto, redondeo por concepto, precisión máxima de cantidades/precios, perfil/correo, reapertura de SQLite, precios independientes por obra, edición que preserva conceptos, claves duplicadas, rechazo de traslado de concepto, Unicode en perfil, respaldo consistente/reapertura/sobrescritura/independencia, bloqueo de sobrescritura de la base activa, creación y edición de formularios, reversión de cambios, equivalencia de IVA 16 y 16.00, perfil y búsqueda con acentos/filtro.

Una prueba inicial detectó un comando SQLite sin liberar que retenía el archivo de respaldo. Se corrigió disponiendo cada comando; el caso de respaldo verifica tanto creación como sustitución y reapertura del archivo resultante.

## Interfaz nativa

- Inicio en modo oscuro de sistema; cambio manual a claro y persistencia después de cerrar/reabrir.
- Creación con título/tipo, dejando IVA y alcance pendientes; Guardar inactivo hasta completar ambos.
- Obra conservada al reabrir desde el paquete portable.
- Captura de concepto con clave, descripción, unidad m², cantidad 100 y precio 100; subtotal mostrado $10,000.00.
- Edición sin cambios con Guardar inactivo; IVA posterior de 16 %; pantalla con IVA $1,600.00 y total $11,600.00.
- Perfil en vista completa con campos repartidos al ancho y Guardar cambios inactivo inicialmente.
- Modal con fondo bloqueado y desplazamiento interior. Corregida la herencia del estilo de la ventana y el doble espaciado interior de los campos de texto.

Los datos de QA viven fuera del producto distribuido, en `.qa/data`, ignorados por Git. El ZIP no lleva obras de ejemplo ni hojas de cálculo del ingeniero.

## Aún no validado

- Windows 7 SP1 real, proceso de 32 bits, carga SQLite en ese sistema, requisitos gráficos y de runtime.
- Rendimiento en 4 GB, apertura en frío y miles de conceptos; no hay métricas mínimas garantizadas.
- Todos los tamaños de pantalla y escalas de DPI. La interfaz incluye adaptación de columnas y navegación compacta, pero falta esa matriz de pruebas.
- Importación XLS/XLSX, PDF, restauración guiada, instalador y actualización: pendientes de implementación y validación.
