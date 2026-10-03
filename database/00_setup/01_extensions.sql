-- =============================================================================
-- 00_setup/01_extensions.sql
-- Extensiones en el esquema extensions, igual que las instala Supabase.
-- btree_gist: permite combinar = sobre enteros con && sobre rangos en los
--             EXCLUDE que impiden traslapes.
-- unaccent:   quita tildes en people.sp_text_normalize.
-- =============================================================================

CREATE SCHEMA IF NOT EXISTS extensions;

CREATE EXTENSION IF NOT EXISTS btree_gist WITH SCHEMA extensions;
CREATE EXTENSION IF NOT EXISTS unaccent   WITH SCHEMA extensions;
