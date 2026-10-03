-- =============================================================================
-- 00_setup/02_roles.sql
-- Roles de base de datos. Los roles son del servidor, no de la base, por eso se
-- crean solo si no existen y siempre se les actualiza la contrasena.
--
--   sgcm_owner      Administrador de la base de datos del sistema. Dueno de todos
--                   los esquemas y objetos; tiene todos los permisos sobre ellos.
--   sgcm_app        Lo usan la API y el Worker. CRUD sobre las tablas, salvo las
--                   de solo insercion (solo SELECT e INSERT).
--   sgcm_reporting  Solo lectura de people, scheduling y clinical. Sin acceso a
--                   app, donde viven contrasenas, tokens y sesiones.
-- =============================================================================

SELECT format('CREATE ROLE %I LOGIN', r)
FROM unnest(ARRAY['sgcm_owner', 'sgcm_app', 'sgcm_reporting']) AS r
WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = r)
\gexec

SELECT format('ALTER ROLE sgcm_owner LOGIN PASSWORD %L', :'owner_password') \gexec
SELECT format('ALTER ROLE sgcm_app LOGIN PASSWORD %L', :'app_password') \gexec
SELECT format('ALTER ROLE sgcm_reporting LOGIN PASSWORD %L', :'reporting_password') \gexec

-- El usuario que ejecuta el deploy necesita poder actuar como sgcm_owner para
-- crear los objetos a su nombre (SET ROLE en deploy.sql).
GRANT sgcm_owner TO CURRENT_USER;

-- Conexion a la base actual. sgcm_owner, como administrador, recibe todos los
-- privilegios de la base (crear esquemas y tablas temporales incluidos).
SELECT format('GRANT ALL PRIVILEGES ON DATABASE %I TO sgcm_owner', current_database()) \gexec
SELECT format('GRANT CONNECT ON DATABASE %I TO sgcm_app, sgcm_reporting', current_database()) \gexec

-- Las extensiones viven en el esquema extensions.
GRANT USAGE ON SCHEMA extensions TO sgcm_owner, sgcm_app, sgcm_reporting;
