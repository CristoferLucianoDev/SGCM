-- =============================================================================
-- Sistema de Gestion de Citas Medicas (SGCM)
-- deploy.sql: crea la base de datos completa (Modelo de Datos v1.3).
--
-- El mismo script se ejecuta en la base local (Supabase CLI) y en produccion
-- (Supabase). Las contrasenas de los roles nunca se escriben en el codigo: se
-- pasan como variables de psql.
--
-- Uso:
--   psql "<cadena de conexion>" -f deploy.sql \
--        -v owner_password='...' \
--        -v app_password='...' \
--        -v reporting_password='...'
--
-- Debe ejecutarlo el usuario administrador del servidor (postgres en Supabase).
-- =============================================================================

\set ON_ERROR_STOP on
\set QUIET on
-- Los scripts estan en UTF-8. En Windows psql usa por defecto la codificacion
-- de la consola (WIN1252) y dañaria las tildes y la ñ de los datos semilla.
\encoding UTF8

\if :{?owner_password}
\else
  \echo 'Falta la variable owner_password (-v owner_password=...)'
  \quit
\endif
\if :{?app_password}
\else
  \echo 'Falta la variable app_password (-v app_password=...)'
  \quit
\endif
\if :{?reporting_password}
\else
  \echo 'Falta la variable reporting_password (-v reporting_password=...)'
  \quit
\endif

BEGIN;

-- Setup: se ejecuta con el usuario administrador del servidor.
\ir 00_setup/01_extensions.sql
\ir 00_setup/02_roles.sql
\ir 00_setup/03_schemas.sql

-- Todo lo demas se crea como sgcm_owner, que queda como dueno de los objetos.
SET ROLE sgcm_owner;

\ir 00_setup/04_base_functions.sql
\ir 01_tables/app.sql
\ir 01_tables/people.sql
\ir 01_tables/scheduling.sql
\ir 01_tables/clinical.sql
\ir 01_tables/messaging.sql
\ir 02_constraints/01_unique_keys.sql
\ir 02_constraints/02_foreign_keys.sql
\ir 02_constraints/03_checks_and_exclusions.sql
\ir 03_indexes/indexes.sql
\ir 04_views/views.sql
\ir 05_functions/functions.sql
\ir 05_functions/triggers.sql
\ir 06_seed/seed.sql
\ir 00_setup/05_permissions.sql

RESET ROLE;

COMMIT;

\echo 'Base de datos SGCM creada correctamente.'
