# Base de datos SGCM

Scripts de la base de datos completa del Sistema de Gestión de Citas Médicas, según el Modelo de Datos v1.3. No hay migraciones: la base se crea completa desde cero con `deploy.sql`. El mismo script se ejecuta en la base local de cada desarrollador (Supabase CLI) y en producción (Supabase).

## Estructura

```
deploy.sql                 Orquesta todo, en este orden y en una sola transacción
sgcm_database.sql          Lo mismo en un solo archivo (generado con build_single_file.py)
00_setup/                  Extensiones, roles, esquemas, funciones base y permisos
01_tables/                 Tablas, un archivo por esquema
02_constraints/            Únicos, claves foráneas, CHECK y EXCLUDE
03_indexes/                Índices de consulta frecuente
04_views/                  Vistas vw_
05_functions/              Funciones de lectura y triggers
06_seed/                   Roles, tipos de token, plantillas, provincias y municipios
tests/rules_test.sql       Pruebas de todas las reglas
```

## Ejecutar

Lo ejecuta el usuario administrador del servidor (`postgres` en Supabase). Las contraseñas de los tres roles se pasan como variables; nunca se escriben en los scripts ni en el repositorio.

```
psql "<cadena de conexión como postgres>" -f deploy.sql \
     -v owner_password='...' \
     -v app_password='...' \
     -v reporting_password='...'
```

Si falta una variable, el script se detiene sin crear nada. Si cualquier sentencia falla, la transacción se revierte completa.

### Base local (Supabase CLI)

Requiere Docker y Supabase CLI.

```
supabase init            # solo la primera vez, en la raíz del repositorio
supabase start           # levanta Supabase local; la base queda en el puerto 54322
psql "postgresql://postgres:postgres@127.0.0.1:54322/postgres" -f database/deploy.sql -v owner_password=... -v app_password=... -v reporting_password=...
```

Para recrear la base desde cero: `supabase db reset` y volver a ejecutar `deploy.sql`. Nadie aplica cambios a mano sobre su base.

### Producción (Supabase)

El pipeline ejecuta `deploy.sql` con la cadena de conexión y las contraseñas guardadas como secretos. La API y el Worker se conectan como `sgcm_app` por el pooler en modo sesión (puerto 5432); en el pooler el usuario se escribe `sgcm_app.<referencia-del-proyecto>`. La Data API de Supabase debe estar apagada en la configuración del proyecto.

Recrear desde cero solo es posible mientras no existan datos reales.

## Roles

| Rol | Uso | Permisos |
| --- | --- | --- |
| `sgcm_owner` | Administrador de la base de datos | Dueño de todos los esquemas y objetos |
| `sgcm_app` | API y Worker | CRUD; solo SELECT e INSERT en medical_record, medical_record_access_log y appointment_report_log |
| `sgcm_reporting` | Reportes externos | Solo lectura de people, scheduling y clinical |

Los roles `anon` y `authenticated` de Supabase no tienen ningún permiso sobre estos esquemas.

## Errores de los triggers

Los triggers que rechazan una operación lanzan SQLSTATE `23514` con el nombre de la regla en `ConstraintName` (por ejemplo `tg_appointment_within_block`), igual que un CHECK. Los repositorios de Infrastructure los traducen a errores de dominio.

## Cambios

Todo cambio se aplica primero en el Modelo de Datos y después en el script del objeto afectado, en el mismo Pull Request. Después de cambiar un script, regenera `sgcm_database.sql` con `python build_single_file.py` y ejecuta `tests/rules_test.sql`.
