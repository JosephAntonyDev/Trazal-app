# Dependencias de la vista previa

- ExcelDataReader 3.7.0, biblioteca net462, para leer XLS/XLSX sin Office. Licencia MIT incluida íntegramente en `ExcelDataReader-LICENSE.txt`. Proyecto y documentación: https://github.com/ExcelDataReader/ExcelDataReader. NuGet: https://www.nuget.org/packages/ExcelDataReader/3.7.0. No ejecuta macros ni recalcula fórmulas; se leen valores guardados.

- System.Data.SQLite, paquete oficial `Stub.System.Data.SQLite.Core.NetFramework` 1.0.119 (metadatos 1.0.119.0), biblioteca net46 e interoperabilidad x86/x64. El paquete señala la dedicación al dominio público de SQLite: https://www.sqlite.org/copyright.html. Proyecto: https://system.data.sqlite.org/. Debe validarse esta versión y sus binarios en el Windows 7 destino antes del piloto.
- Microsoft.NETFramework.ReferenceAssemblies.net48 1.0.3: referencias de compilación, no se distribuyen con el ejecutable. https://www.nuget.org/packages/Microsoft.NETFramework.ReferenceAssemblies.net48/1.0.3
- .NET Framework 4.8 / WPF: runtime de Microsoft instalado por separado bajo sus términos. https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48
- Marca y símbolo Trazal: trasladados del kit del proyecto. Tipografía de interfaz: Segoe UI disponible en Windows; no se redistribuyen archivos de fuentes.

No se incorporan hojas de cálculo, fotografías ni datos del cliente en este proyecto.

## Exportación de reportes (0.4.0)

PDFsharp-GDI 6.2.4, variante net462, licencia MIT: https://docs.pdfsharp.net/General/Overview/Dotnet-Framework-Support.html y https://www.nuget.org/packages/PDFsharp-GDI/6.2.4. Dependencias transitivas fijadas: Microsoft.Extensions.Logging.Abstractions 8.0.3, Microsoft.Extensions.DependencyInjection.Abstractions 8.0.2, Microsoft.Bcl.AsyncInterfaces 8.0.0, System.Security.Cryptography.Pkcs 8.0.1, System.Buffers 4.5.1, System.Memory 4.5.5, System.Runtime.CompilerServices.Unsafe 4.5.3, System.Numerics.Vectors 4.5.0, System.Threading.Tasks.Extensions 4.5.4. Las licencias y avisos descargados se incluyen en REPORTING-LICENSES.txt. Versiones, SHA-256 y frameworks en build.ps1/csproj. No se usan servicios externos de conversión.

XLSX se escribe como Open XML mediante System.Xml y System.IO.Compression del Framework; sin dependencia de Office. El paquete incluye el motor PDF de .NET Framework; debe probarse en el Windows 7 destino.
