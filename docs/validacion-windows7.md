# Registro del primer piloto de compatibilidad

Estado: **pendiente de ejecutar en el Windows 7 del ingeniero**.

## Ficha

- Equipo / procesador: pendiente.
- Windows 7 Service Pack 1: pendiente de confirmar.
- Arquitectura: probablemente 64 bits; no comprobada.
- RAM: estimada 4–8 GB; objetivo de diseño 4 GB.
- .NET Framework 4.8 y actualizaciones del sistema: pendiente.
- Resolución, escala y controlador gráfico: pendiente.

## Recorrido de la vista previa

1. Extraer el ZIP completo; abrir `Trazal.exe` como usuario normal. Si falta .NET, resolver con TI el runtime 4.8 oficial; no instalar 4.8.1 como sustituto para Windows 7.
2. Crear obra con título y tipo, dejando alcance/IVA vacíos. Confirmar «IVA pendiente».
3. Agregar `CON-01 / Concreto / m² / 100 / 100`. Esperado: subtotal **$10,000.00**.
4. Editar obra, IVA **16**. Esperado: IVA **$1,600.00**, total **$11,600.00**. Precio unitario sin desglose APU en esta versión.
5. Cerrar y reabrir: mismos valores. Editar sin cambiar nada debe mantener Guardar deshabilitado; modificar y revertir también.
6. Crear segunda obra con la misma clave y precio distinto. La primera conserva su precio.
7. Probar búsqueda por nombre y tipo, perfil completo, temas claro/oscuro/sistema y tamaño reducido de ventana; comprobar texto al 100 % y 125 %.
8. Exportar respaldo a otra carpeta; debe avisar que se verificó. Antes de producción aún falta restauración guiada y prueba de actualización.
9. Medir arranque, memoria y captura durante una sesión. Usar datos sintéticos; todavía no hay aprobación para información de producción.

Registrar resultado esperado/obtenido, duración, versión de aplicación, captura del error y configuración del equipo. Una captura correcta en Windows moderno no cierra esta validación.

## Pendiente de la prueba de compatibilidad completa

Importar XLS/XLSX, exportar PDF, instalar/desinstalar, restaurar como copia y actualizar conservando información. Estas capacidades aún no pertenecen a la vista previa 0.1; no marcarlas aprobadas.
