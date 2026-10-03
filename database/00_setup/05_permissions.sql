-- =============================================================================
-- 00_setup/05_permissions.sql
-- Permisos por esquema. Se ejecuta al final, como sgcm_owner, cuando ya existen
-- todos los objetos.
-- =============================================================================

-- Nadie fuera de los roles del sistema entra a los esquemas. Incluye los roles
-- anon y authenticated de Supabase, que no reciben ningun permiso.
REVOKE ALL ON SCHEMA app, people, scheduling, clinical, messaging FROM PUBLIC;
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA app, people, scheduling, clinical, messaging FROM PUBLIC;

-- ---------------------------------------------------------------------------
-- sgcm_app: API y Worker
-- ---------------------------------------------------------------------------
GRANT USAGE ON SCHEMA app, people, scheduling, clinical, messaging TO sgcm_app;

GRANT SELECT, INSERT, UPDATE, DELETE
    ON ALL TABLES IN SCHEMA app, people, scheduling, clinical, messaging
    TO sgcm_app;

-- Tablas de solo insercion: el motor impide modificarlas o borrarlas.
REVOKE UPDATE, DELETE ON
    clinical.medical_record,
    clinical.medical_record_access_log,
    scheduling.appointment_report_log
FROM sgcm_app;

GRANT EXECUTE ON FUNCTION
    people.sp_text_normalize(text),
    scheduling.sp_availability_get_free_slots(timestamptz, timestamptz, interval, integer, integer, integer, integer, integer),
    scheduling.sp_doctor_pick_random(integer, timestamptz, timestamptz, integer, integer, integer)
TO sgcm_app;

-- ---------------------------------------------------------------------------
-- sgcm_reporting: solo lectura, sin acceso a app ni a messaging
-- ---------------------------------------------------------------------------
GRANT USAGE ON SCHEMA people, scheduling, clinical TO sgcm_reporting;
GRANT SELECT ON ALL TABLES IN SCHEMA people, scheduling, clinical TO sgcm_reporting;
GRANT EXECUTE ON FUNCTION people.sp_text_normalize(text) TO sgcm_reporting;

-- ---------------------------------------------------------------------------
-- Objetos que se agreguen despues heredan los mismos permisos.
-- ---------------------------------------------------------------------------
ALTER DEFAULT PRIVILEGES IN SCHEMA app, people, scheduling, clinical, messaging
    REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;
ALTER DEFAULT PRIVILEGES IN SCHEMA app, people, scheduling, clinical, messaging
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO sgcm_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA people, scheduling, clinical
    GRANT SELECT ON TABLES TO sgcm_reporting;
