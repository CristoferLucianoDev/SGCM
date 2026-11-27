# ADR 0019: Librerías para exportar reportes a PDF, Excel y Word

- **Estado:** Propuesto
- **Fecha:** 2026-11-27
- **Autor:** Diego Mendez
- **Tarea:** AB#379 (PBI 76, Exportar reportes)
- **Secciones afectadas del documento de arquitectura:** 2 (Exportación de reportes) y 8 (Reportes)

## Contexto

El PBI 76 pide exportar el reporte de citas en PDF, Excel y Word, con las mismas filas y en el mismo orden que en
pantalla. El documento de arquitectura deja la elección de las librerías a un ADR, con una condición: deben ser
librerías **sin licencia comercial**, igual que se descartaron MediatR (desde la v13) y FluentAssertions 8.

Cada formato es una clase que implementa `IReportExporter` en `Infrastructure/Reporting`, así que cambiar la librería
de un formato no afecta a Application ni a los demás formatos.

Criterios de la tarea:

1. Licencia permisiva (MIT, Apache 2.0 o similar), sin restricción por ingresos, uso comercial o número de usuarios.
2. Soporte para .NET 8, la versión actual del proyecto (ADR 0018), y para .NET 10, a la que el proyecto debe migrar.
3. Facilidad para generar un documento con título, rango, totales y una tabla.

También se revisó que funcionen en Linux sin dependencias de Windows, porque el pipeline y los contenedores corren
en Linux.

En los últimos años varias de las librerías habituales cambiaron de licencia, así que cada candidata se verificó en
NuGet y en su sitio oficial en noviembre de 2026.

## Decisión

| Formato | Paquete | Versión | Licencia |
|---|---|---|---|
| PDF | `PDFsharp-MigraDoc` | **6.2.4, fijada** | MIT |
| Excel | `ClosedXML` | 0.105.1 | MIT |
| Word | `DocumentFormat.OpenXml` (Open XML SDK) | 3.5.1 | MIT |

- **PDF con MigraDoc 6.2.4.** MigraDoc construye el documento con un modelo de alto nivel (secciones, párrafos y
  tablas) y PDFsharp lo renderiza. La 6.2.4 es la última versión publicada con licencia MIT; incluye `net8.0`,
  `net9.0`, `net10.0` y `netstandard2.0`, y no depende de Windows. **La versión se fija en 6.2.4**: desde la 6.5.0 el
  paquete se publica con licencia AGPL-3.0.
- **Excel con ClosedXML.** Crea libros `.xlsx` con una API sencilla para celdas, estilos y tablas, sin tener Excel
  instalado.
- **Word con Open XML SDK.** Es el SDK oficial de Microsoft para documentos de Office, con licencia MIT. ClosedXML ya
  depende de él, así que Word no agrega una dependencia nueva. Su API es de bajo nivel, pero el documento del reporte
  es fijo (título, rango, totales y una tabla), y esa parte se escribe una sola vez dentro de su exportador.

## Alternativas descartadas

**PDF**

- **PDFsharp-MigraDoc 6.5.0 o superior:** desde la 6.5.0 su licencia es AGPL-3.0. Para una API publicada en la web,
  la AGPL obliga a publicar el código de toda la aplicación o a comprar una licencia comercial.
- **QuestPDF:** tiene la mejor API para maquetar, pero desde el 6 de julio de 2026 se distribuye con la
  *Community License* v3.0, una licencia comercial de código disponible que es gratuita solo bajo ciertos requisitos:
  individuos, instituciones académicas y empresas con menos de 1 millón de dólares de ingresos anuales. Excluye al
  sector público sin importar sus ingresos y obliga a declarar el tipo de licencia en el código. No cumple el criterio
  de "sin licencia comercial".
- **iText 9:** licencia doble, AGPL-3.0 o comercial. Se descarta por la misma razón que MigraDoc 6.5.0.

**Excel**

- **EPPlus 5 o superior:** licencia Polyform Noncommercial; el uso comercial requiere una licencia de pago por
  desarrollador.
- **NPOI 2.8:** el código sigue bajo Apache 2.0, pero los binarios que se publican en NuGet ahora llevan un
  "Open Source Maintenance Fee Agreement" con una cuota mensual. Usarlo sin la cuota obligaría a compilarlo a partir
  del código fuente, y su API para `.xlsx` es más verbosa que la de ClosedXML.

**Word**

- **DocX / Xceed Words:** la licencia gratuita de Xceed es solo para uso no comercial y para menos de 10 usuarios;
  todo lo demás requiere una licencia de pago.
- **NPOI (XWPF):** mismo problema de distribución de binarios que en Excel.

## Consecuencias

- **Paquetes nuevos** en `Directory.Packages.props` (los agrega la tarea 380, con aviso previo al equipo, como pide el
  flujo de trabajo): `PDFsharp-MigraDoc` 6.2.4, `ClosedXML` 0.105.1 y `DocumentFormat.OpenXml` 3.5.1. Solo los
  referencia `SGCM.Data`, porque los exportadores viven en `Infrastructure/Reporting`.
- **`PDFsharp-MigraDoc` no se actualiza a 6.5.0 ni a versiones posteriores** mientras su licencia sea AGPL. Si una
  actualización de seguridad lo exige, se crea un ADR nuevo que reemplace este y elija otra librería de PDF. Conviene
  documentarlo junto a la línea del paquete en `Directory.Packages.props`.
- **`SixLabors.Fonts` se fija en la serie 1.x.** ClosedXML acepta las versiones 1.x y 2.x de esta dependencia, pero
  desde la 2.0 su licencia dejó de ser Apache 2.0 y pasó a la *Six Labors Split License*. La tarea 380 debe declararla
  en `Directory.Packages.props` con una versión 1.x para que NuGet no resuelva una 2.x.
- **Las fuentes del PDF se resuelven en Linux.** PDFsharp 6 en su variante multiplataforma necesita un
  `IFontResolver` o fuentes disponibles en el contenedor. La tarea 380 debe incluir una fuente con licencia libre o un
  resolvedor de fuentes, y probarlo en el pipeline, que corre en Linux.
- **El documento de arquitectura** debe actualizar la fila "Exportación de reportes" de la sección 2 (hoy "Pendiente
  (ADR)") y la tabla de ADR de la sección 15 con este ADR.
- **Numeración:** la tabla de la sección 15 lista dos ADR con el número 0018. Este ADR usa el 0019 y no corrige esa
  duplicación; queda pendiente para quien mantiene el documento.
- Las pruebas de la tarea 382 verifican que cada formato genere un archivo válido con las mismas filas y en el mismo
  orden que la pantalla, así que un cambio futuro de librería queda cubierto por esas pruebas.
