-- =============================================================================
-- 00_setup/04_base_functions.sql
-- Funciones que otras piezas del script necesitan antes de existir:
--   people.sp_text_normalize   la usa el indice unico de people.specialty.
--   app.sp_audit_set_updated_at la usan los triggers de auditoria (T1).
-- =============================================================================

-- Normaliza un texto para comparaciones sin mayusculas ni tildes.
-- IMMUTABLE porque se usa en un indice; el diccionario se indica de forma
-- explicita y calificada para que el resultado no dependa del search_path.
CREATE FUNCTION people.sp_text_normalize(p_text text)
RETURNS text
LANGUAGE sql
IMMUTABLE
PARALLEL SAFE
RETURNS NULL ON NULL INPUT
AS $$
    SELECT lower(extensions.unaccent('extensions.unaccent'::regdictionary, btrim(p_text)));
$$;

COMMENT ON FUNCTION people.sp_text_normalize(text) IS
    'Quita espacios extremos, tildes y mayusculas. Base de la unicidad de people.specialty.name.';

-- T1: llena updated_at en cada UPDATE si la aplicacion no lo hizo.
-- El AuditInterceptor de EF Core lo llena en SaveChanges, pero ExecuteUpdate y
-- las sentencias del Worker lo saltan. Si la aplicacion ya envio un valor
-- distinto al anterior, se respeta (pruebas con IDateTimeProvider).
CREATE FUNCTION app.sp_audit_set_updated_at()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.updated_at IS NOT DISTINCT FROM OLD.updated_at THEN
        NEW.updated_at := now();
    END IF;
    RETURN NEW;
END;
$$;

COMMENT ON FUNCTION app.sp_audit_set_updated_at() IS
    'T1. Trigger BEFORE UPDATE comun a todas las tablas que se actualizan.';
