# Dependencias de la vista previa

- ExcelDataReader 3.7.0, biblioteca net462, para leer XLS/XLSX sin Office. Licencia MIT incluida íntegramente en `ExcelDataReader-LICENSE.txt`. Proyecto y documentación: https://github.com/ExcelDataReader/ExcelDataReader. NuGet: https://www.nuget.org/packages/ExcelDataReader/3.7.0. No ejecuta macros ni recalcula fórmulas; se leen valores guardados.

- System.Data.SQLite, paquete oficial `Stub.System.Data.SQLite.Core.NetFramework` 1.0.119 (metadatos 1.0.119.0), biblioteca net46 e interoperabilidad x86/x64. El paquete señala la dedicación al dominio público de SQLite: https://www.sqlite.org/copyright.html. Proyecto: https://system.data.sqlite.org/. Debe validarse esta versión y sus binarios en el Windows 7 destino antes del piloto.
- Microsoft.NETFramework.ReferenceAssemblies.net48 1.0.3: referencias de compilación, no se distribuyen con el ejecutable. https://www.nuget.org/packages/Microsoft.NETFramework.ReferenceAssemblies.net48/1.0.3
- .NET Framework 4.8 / WPF: runtime de Microsoft instalado por separado bajo sus términos. https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48
- Marca y símbolo Trazal: trasladados del kit del proyecto. Tipografía de interfaz: Segoe UI disponible en Windows; no se redistribuyen archivos de fuentes.

No se incorporan hojas de cálculo, fotografías ni datos del cliente en este proyecto.
