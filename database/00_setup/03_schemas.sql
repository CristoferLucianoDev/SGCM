-- =============================================================================
-- 00_setup/03_schemas.sql
-- Los cinco esquemas del sistema, propiedad de sgcm_owner.
-- El esquema de credenciales se llama app porque Supabase reserva auth para su
-- propio sistema de usuarios y no permite crear objetos en el.
-- =============================================================================

CREATE SCHEMA app        AUTHORIZATION sgcm_owner;
CREATE SCHEMA people     AUTHORIZATION sgcm_owner;
CREATE SCHEMA scheduling AUTHORIZATION sgcm_owner;
CREATE SCHEMA clinical   AUTHORIZATION sgcm_owner;
CREATE SCHEMA messaging  AUTHORIZATION sgcm_owner;

COMMENT ON SCHEMA app        IS 'Credenciales, tokens, sesiones y segundo factor. Solo accesible para sgcm_app.';
COMMENT ON SCHEMA people     IS 'Personas (paciente, medico, administrador) y catalogos asociados.';
COMMENT ON SCHEMA scheduling IS 'Disponibilidad, citas y registro de reportes.';
COMMENT ON SCHEMA clinical   IS 'Registro clinico versionado y registro de accesos.';
COMMENT ON SCHEMA messaging  IS 'Recordatorios y plantillas.';
