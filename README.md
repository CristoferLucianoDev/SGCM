<div align="center">

# SGCM

### Sistema de Gestión de Citas Médicas

Plataforma para administrar la agenda médica de punta a punta: registro de pacientes, disponibilidad de los médicos, agendamiento, cancelación y reprogramación de citas, recordatorios, historial clínico y reportes, mediante un portal web y una API REST para la aplicación móvil.

[![SGCM CI](https://github.com/CristoferLucianoDev/SGCM/actions/workflows/ci.yml/badge.svg?branch=dev)](https://github.com/CristoferLucianoDev/SGCM/actions/workflows/ci.yml)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core-MVC_+_Web_API-512BD4)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-Supabase-4169E1?logo=postgresql&logoColor=white)
![Pruebas](https://img.shields.io/badge/Pruebas-xUnit_·_Reqnroll_·_Testcontainers-2E7D32)

</div>

---

## Descripción

**SGCM** centraliza la gestión de citas de un centro médico. Los pacientes se registran, consultan la disponibilidad de los médicos y agendan sus citas; los médicos administran sus horarios y su agenda; y los administradores gestionan especialidades, proveedores de seguros y cuentas de médico.

La solución se compone de:

* Un **portal web** ASP.NET Core MVC para pacientes, médicos y administradores.
* Una **API REST** documentada con Swagger, que consume la aplicación móvil.
* **Procesos automáticos** en segundo plano para recordatorios, expiración de citas y limpieza de cuentas y credenciales.
* Una **herramienta de aprovisionamiento** para dar de alta cuentas de médico.

El proyecto aplica Clean Architecture, Domain-Driven Design, TDD y BDD, con integración continua en cada pull request.

## Funcionalidades

### Disponibles

* Registro de pacientes con confirmación de cuenta por correo electrónico.
* Reenvío del correo de confirmación con tiempo de espera entre intentos.
* Inicio de sesión con JWT y tokens de actualización.
* Límite de intentos fallidos por correo y por dirección IP.
* Recuperación y restablecimiento de contraseña.
* Creación y consulta de especialidades médicas.
* Alta de cuentas de médico mediante `SGCM.Provisioning`.
* Purga automática de cuentas no confirmadas.

### En desarrollo

* Perfil de paciente y de médico.
* Disponibilidad del médico con series recurrentes semanales.
* Agendamiento, cancelación y reprogramación de citas.
* Recordatorios automáticos por correo.
* Historial de citas y registro clínico.
* Reportes de citas en PDF, Excel y Word.
* Administración de proveedores de seguros.
* Autenticación de dos factores.
* Aplicación móvil.

## Arquitectura

```mermaid
flowchart TD
    WEB["Portal web MVC"] --> APP
    MOVIL["Aplicación móvil"] --> API["API REST"]
    API --> APP["Application<br/>casos de uso, validaciones, comportamientos"]
    JOBS["Procesos automáticos"] --> APP
    APP --> DOMAIN["Domain<br/>agregados, reglas de negocio, eventos"]
    INFRA["Infrastructure<br/>EF Core, seguridad, notificaciones"] --> APP
    INFRA --> DB[("PostgreSQL en Supabase")]
    INFRA --> SMTP["Servidor SMTP"]
```

Las dependencias apuntan siempre hacia el dominio: `Domain` no conoce a ninguna otra capa, `Application` define las interfaces que `Infrastructure` implementa y `SGCM.Web` solo compone y expone. Las pruebas de arquitectura con NetArchTest verifican estas reglas en cada ejecución.

El sistema se organiza en cinco módulos que se repiten en cada capa: `Identity`, `People`, `Scheduling`, `Clinical` y `Messaging`.

## Estructura de la solución

| Proyecto / carpeta    | Responsabilidad                                                                          |
| --------------------- | ---------------------------------------------------------------------------------------- |
| `SGCM.Data`           | Capas Domain, Application e Infrastructure: reglas de negocio, casos de uso y persistencia |
| `SGCM.Web`            | Portal MVC, API REST, contratos, mappers, procesos automáticos y configuración del host  |
| `SGCM.Test`           | Pruebas unitarias, de integración, de aceptación (BDD) y de arquitectura                 |
| `SGCM.Provisioning`   | Herramienta de consola para crear cuentas de médico                                      |
| `database`            | Scripts completos de PostgreSQL, orquestados por `deploy.sql`                            |
| `supabase`            | Configuración del entorno local de Supabase CLI                                          |
| `.github/workflows`   | Flujo de integración continua                                                            |

## Tecnologías

| Área                  | Tecnologías                                                    |
| --------------------- | -------------------------------------------------------------- |
| Lenguaje y plataforma | C# 12, .NET 8                                                  |
| Backend               | ASP.NET Core MVC y Web API                                     |
| Base de datos         | PostgreSQL 17 en Supabase                                      |
| Persistencia          | Entity Framework Core 8 y Npgsql, sin migraciones              |
| Seguridad             | JWT Bearer, ASP.NET Core Identity (hash de contraseñas), Data Protection, rate limiting |
| Validación            | FluentValidation                                               |
| Correo electrónico    | MailKit                                                        |
| Registro de eventos   | Serilog                                                        |
| Documentación de API  | Swagger / OpenAPI                                              |
| Pruebas               | xUnit, NSubstitute, Shouldly, Reqnroll, Testcontainers y NetArchTest |
| Integración continua  | GitHub Actions                                                 |
| Gestión del trabajo   | Azure Boards (Scrum)                                           |

## Requisitos

* [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (la versión exacta está fijada en `global.json`).
* [Docker Desktop](https://www.docker.com/products/docker-desktop/), para Supabase local y para las pruebas de integración.
* [Supabase CLI](https://supabase.com/docs/guides/local-development/cli/getting-started).
* `psql` (cliente de PostgreSQL).
* Git.
* Visual Studio 2022 o una versión compatible con .NET 8, opcionalmente.

## Instalación

### 1. Clonar el repositorio

```bash
git clone https://github.com/CristoferLucianoDev/SGCM.git
cd SGCM
```

### 2. Restaurar las dependencias

```bash
dotnet restore SGCM.sln
```

### 3. Levantar la base de datos local

```bash
supabase start
psql "postgresql://postgres:postgres@127.0.0.1:54322/postgres" -f database/deploy.sql \
     -v owner_password='...' \
     -v app_password='...' \
     -v reporting_password='...'
```

La base se crea completa desde `deploy.sql` en una sola transacción; no hay migraciones de EF Core. Para recrearla desde cero: `supabase db reset` y volver a ejecutar el script. Los detalles de roles y permisos están en [`database/README.md`](database/README.md).

### 4. Configurar los secretos

SGCM no guarda cadenas de conexión ni claves en el repositorio. En desarrollo se configuran con .NET User Secrets:

```bash
dotnet user-secrets set "ConnectionStrings:SgcmDatabase" "Host=127.0.0.1;Port=54322;Database=postgres;Username=sgcm_app;Password=..." --project SGCM.Web

dotnet user-secrets set "Jwt:SigningKey" "CLAVE-ALEATORIA-DE-AL-MENOS-32-BYTES" --project SGCM.Web
```

En QA y producción los mismos valores llegan por variables de entorno (`ConnectionStrings__SgcmDatabase`, `Jwt__SigningKey`).

## Ejecución

```bash
dotnet run --project SGCM.Web --launch-profile https
```

| Recurso                         | Dirección                        |
| ------------------------------- | -------------------------------- |
| Portal web                      | `https://localhost:7126`         |
| Swagger (solo en Development)   | `https://localhost:7126/swagger` |
| Buzón de correo local (Supabase) | `http://localhost:54324`        |

### Crear una cuenta de médico

Las cuentas de médico no se registran desde el portal: las crea un administrador con la herramienta de aprovisionamiento, que usa los mismos user-secrets de `SGCM.Web`:

```bash
dotnet run --project SGCM.Provisioning -- crear-medico medico@correo.com
```

La herramienta muestra una contraseña temporal una sola vez; el administrador se la entrega al médico, que debe cambiarla en su primer acceso.

## Pruebas

```bash
dotnet test SGCM.Test/SGCM.Test.csproj -c Release
```

| Nivel        | Qué cubre                                                    | Base de datos              |
| ------------ | ------------------------------------------------------------ | -------------------------- |
| Unitarias    | Reglas de dominio, handlers, validadores, mappers y servicios | No                         |
| Integración  | Repositorios, mapeos, scripts SQL y envío de correo           | PostgreSQL con Testcontainers |
| Aceptación   | Escenarios de los PBIs en Gherkin (español) con Reqnroll      | PostgreSQL con Testcontainers |
| Arquitectura | Reglas de dependencia entre capas y convenciones de nombres   | No                         |

Las pruebas de integración levantan un contenedor `postgres:17` y le aplican el mismo `deploy.sql` de producción, por lo que necesitan Docker en ejecución. Las pruebas de correo usan el servidor SMTP local en el puerto `54325`.

## Integración continua

El flujo [`.github/workflows/ci.yml`](.github/workflows/ci.yml) se ejecuta en cada push y en cada pull request hacia `dev`, `qa` y `main`:

1. Restaura las dependencias.
2. Compila la solución en `Release`, con las advertencias tratadas como errores.
3. Ejecuta todas las pruebas con un servidor SMTP de prueba (Mailpit) y PostgreSQL en Docker.
4. Publica los resultados de las pruebas como un check del pull request.
5. Genera el resumen de cobertura de código.
6. Guarda los reportes `.trx` y de cobertura como artefacto.

Un pull request solo se integra cuando el flujo termina en verde.

## Flujo de trabajo

```mermaid
flowchart LR
    U["users/&lt;miembro&gt;"] -->|PR| D["dev"]
    D -->|PR al cierre del sprint| Q["qa"]
    Q -->|PR de liberación| M["main"]
```

* Cada tarea se trabaja en una rama local y se entrega desde `users/<miembro>` mediante un pull request hacia `dev`.
* Los pull requests hacen referencia a su work item de Azure Boards con `AB#<id>`.
* `qa` recibe el trabajo terminado del sprint y `main` contiene la versión liberada.

## Paquetes NuGet

Las versiones de los paquetes NuGet se definen solo en `Directory.Packages.props`. Para agregar o actualizar un paquete: añadir o cambiar su `PackageVersion` ahí, poner el `PackageReference` sin versión en el `.csproj`, ejecutar `dotnet restore` y subir el `packages.lock.json` actualizado junto con el cambio. No se actualizan paquetes desde el administrador de NuGet de Visual Studio sin revisar este archivo.

## Equipo

Proyecto desarrollado bajo Scrum por:

* [Cristofer Luciano](https://github.com/CristoferLucianoDev)
* [Diego Mendez](https://github.com/Dieguittoxxx)
* Enmanuel
* [José Nuñez](https://github.com/JoseNunez1108)
* [Stanley Abreu](https://github.com/StanleyCM)

---

<div align="center">

Desarrollado con C#, .NET 8, ASP.NET Core y PostgreSQL.

</div>
