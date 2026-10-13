# Reglas para trabajar en el repositorio SGCM

Vas a implementar instrucciones que te daré por partes. Cada instrucción dice exactamente qué crear o modificar. Respeta estas reglas en todo momento.

## Relación con las instrucciones globales
- Además de estas reglas, sigue aplicando las instrucciones del `AGENTS.md` global del usuario.
- Por jerarquía, estas reglas tienen más autoridad: si una regla de este archivo choca con una del `AGENTS.md` global, gana la de este archivo.

## Revisión de código automática
- Cada vez que termines de escribir o modificar código, ejecuta la skill `code-review` sobre los cambios sin esperar a que te lo pida. Corrige lo que encuentre dentro de los archivos de la instrucción y reporta en el resumen lo que quede pendiente.

## Alcance
- Haz solo lo que pide la instrucción. No refactorices, no renombres, no reformatees y no "mejores" código que la instrucción no menciona.
- No toques archivos que la instrucción no nombra. Si para cumplirla necesitas cambiar un archivo no mencionado, detente y pregúntame antes.
- Si la instrucción es ambigua, contradice estas reglas o choca con el código existente, detente y pregúntame. No improvises una solución.
- Crea solo las carpetas que la instrucción indique. Si creas un archivo en una carpeta que tiene un `.gitkeep`, borra ese `.gitkeep`.

## Lo que no debes hacer
- No ejecutes pruebas de ningún tipo (`dotnet test` ni similares).
- No uses git para nada que cambie el estado del repo: nada de `commit`, `branch`, `switch`, `checkout`, `merge`, `rebase`, `stash`, `reset`, `push` ni `pull`. De ramas y commits me encargo yo.
- Puedes ejecutar `dotnet build SGCM.sln` para confirmar que compila. Si hay errores o advertencias, corrígelos solo dentro de los archivos de la instrucción; si el problema está en otro archivo, avísame.
- No agregues paquetes NuGet ni cambies versiones salvo que la instrucción lo pida. Las versiones van solo en `Directory.Packages.props` (Central Package Management); en los `.csproj` el `PackageReference` va sin versión.
- No modifiques, salvo que la instrucción lo pida expresamente: `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `nuget.config`, `SGCM.sln`, los `.csproj`, `.gitignore`, `appsettings*.json`, `Properties/launchSettings.json`, `Program.cs`, ni las carpetas `database/`, `supabase/`, `docs/`, `.azuredevops/`, `node_modules/`, ni `package.json`.
- Nunca escribas secretos, contraseñas, tokens ni cadenas de conexión reales en el repositorio.
- No cambies los finales de línea ni la codificación de los archivos existentes.
- No uses emojis en el código ni en los comentarios.

## Arquitectura
- Solución .NET 8 con tres proyectos: `SGCM.Data` (Domain, Application e Infrastructure como carpetas), `SGCM.Web` (MVC, API REST en `Api/`, contratos en `Contracts/`, mappers en `Mappers/`, configuración en `Hosting/`) y `SGCM.Test`.
- Clean Architecture: Domain no conoce a nadie; Application define interfaces en `Application/Abstractions/<tema>` e Infrastructure las implementa. Application nunca usa clases de Infrastructure.
- Módulos, repetidos en cada capa: `Identity`, `People`, `Scheduling`, `Clinical`, `Messaging`.
- El namespace sigue a la carpeta (por ejemplo `SGCM.Data.Application.Identity.Commands.RegisterPatientAccount`). Nombres de carpetas, clases y miembros en inglés y PascalCase.
- Casos de uso: un comando o consulta por carpeta en `Application/<Módulo>/Commands|Queries/<Caso>/`, con su Command/Query, Handler y Validator. Contratos propios `ICommand<T>` / `ICommandHandler<,>`; sin MediatR. Validación con FluentValidation aplicada por `ValidationDecorator`; guardado y transacción por `TransactionDecorator`. El handler no llama a `SaveChangesAsync`.
- Efectos secundarios con eventos de dominio: una clase por efecto que implementa `IDomainEventHandler<TEvent>`; se publican después del commit.
- Inyección de dependencias: `Application/DependencyInjection.cs` (`AddApplication`, registra handlers y manejadores de eventos por escaneo) e `Infrastructure/DependencyInjection.cs` (`AddInfrastructure`). Solo inyección por constructor (o `[FromServices]` en acciones de controladores); nada de service locator ni de `new` de servicios.
- Configuración: una clase `XxxOptions` por sección, con su validador `IValidateOptions<T>` en el mismo archivo, registrada en `SGCM.Web/Hosting/OptionsRegistration.cs` con `ValidateOnStart`. Reglas comunes en `Application/Common/OptionsRules`.
- La API devuelve los errores con `ApiControllerBase.ErrorResult` (ProblemDetails con la extensión `code`).

## Convenciones de código
- C# 12, nullable activo y `TreatWarningsAsErrors`: el código debe compilar sin advertencias.
- Clases `sealed` por defecto. Comandos, consultas, DTOs y value objects como `record` inmutables.
- Errores de negocio esperados: se devuelven con `Result` / `Result<T>` y un `Error` declarado en una clase `XxxErrors` junto a su agregado. No se lanzan excepciones para eso. `DomainException` solo para errores de programación.
- Agregados con setters privados; el estado cambia por métodos del agregado (Tell, don't ask).
- Hora actual siempre con `IDateTimeProvider`; nunca `DateTime.Now` ni `DateTimeOffset.UtcNow` en código de negocio.
- Logging con Serilog (`Serilog.ILogger`). No uses `Microsoft.Extensions.Logging`. Sin AutoMapper: mappers escritos a mano.
- Sin números ni textos mágicos: constantes con nombre. Métodos cortos (alrededor de 20 líneas) con guard clauses; máximo dos niveles de anidamiento.
- Documentación XML en interfaces y miembros públicos. Los comentarios explican el porqué, nunca el qué. Sin código comentado.
- Textos para el usuario (descripciones de errores, mensajes) en español.

## Pruebas (cuando la instrucción pida escribirlas)
- Escríbelas, pero no las ejecutes.
- xUnit, Shouldly y NSubstitute. Ubicación: `SGCM.Test/Unit/<Domain|Application|Infrastructure|Web>`, `SGCM.Test/Integration/<tema>`, `SGCM.Test/Acceptance/Features|Steps` (Reqnroll, en español). Datos de prueba en `Common/Mothers`, fakes en `Common/Fakes`.
- Las pruebas de integración y los escenarios BDD llevan la categoría `Integration` (`[Trait("Category", "Integration")]` o la etiqueta `@Integration`).
- En archivos con namespace bajo `SGCM.Test.Unit` que usen el tipo `Unit`, agrega `using Unit = SGCM.Data.Domain.Common.Unit;` debajo de la declaración del namespace.
- Sigue el estilo de nombres de las pruebas que ya existen en la misma carpeta.

## Al terminar cada instrucción
Dame un resumen con:
1. Archivos creados, modificados y borrados (con su ruta).
2. Carpetas creadas.
3. Resultado de `dotnet build SGCM.sln`.
4. Hallazgos de la `code-review` y cómo se resolvieron.
5. Cualquier duda o decisión que hayas tenido que tomar.
