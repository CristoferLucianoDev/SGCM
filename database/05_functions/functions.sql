-- =============================================================================
-- 05_functions/functions.sql
-- Funciones de lectura. No modifican datos: las reglas de negocio y los plazos
-- se validan en el dominio antes de guardar.
-- =============================================================================

-- Horarios libres: bloques abiertos menos citas activas, con inicio en hora en
-- punto. Devuelve un renglon por medico y hora de inicio.
--   p_from, p_to     Ventana de busqueda: el horario empieza en p_from o
--                    despues y termina en p_to o antes.
--   p_duration       Duracion de la cita (configurable en el dominio).
--   p_doctor_id      Solo ese medico; NULL = cualquiera.
--   p_specialty_id   Solo medicos con esa especialidad activa; NULL = todas.
--   p_province_id,
--   p_municipality_id Filtros de ubicacion; NULL = sin filtro.
--   p_patient_id     Si se indica, quita las horas en que el paciente ya
--                    tiene una cita activa.
-- La cita debe caber dentro de un solo bloque.
CREATE FUNCTION scheduling.sp_availability_get_free_slots(
    p_from             timestamptz,
    p_to               timestamptz,
    p_duration         interval,
    p_doctor_id        integer DEFAULT NULL,
    p_specialty_id     integer DEFAULT NULL,
    p_province_id      integer DEFAULT NULL,
    p_municipality_id  integer DEFAULT NULL,
    p_patient_id       integer DEFAULT NULL
)
RETURNS TABLE (doctor_id integer, starts_at timestamptz, ends_at timestamptz)
LANGUAGE sql
STABLE
AS $$
    SELECT b.doctor_id,
           slot.slot_start,
           slot.slot_start + p_duration
    FROM scheduling.doctor_availability b
    JOIN people.doctor d ON d.id = b.doctor_id AND d.is_active
    JOIN app."user"    u ON u.id = d.user_id   AND u.is_active
    CROSS JOIN LATERAL generate_series(
        -- Primera hora en punto dentro del bloque y de la ventana.
        to_timestamp(ceil(extract(epoch FROM greatest(b.starts_at, p_from)) / 3600) * 3600),
        b.ends_at - p_duration,
        interval '1 hour'
    ) AS slot(slot_start)
    WHERE b.is_available
      AND b.starts_at < p_to
      AND b.ends_at   > p_from
      AND slot.slot_start + p_duration <= p_to
      AND (p_doctor_id       IS NULL OR b.doctor_id       = p_doctor_id)
      AND (p_province_id     IS NULL OR d.province_id     = p_province_id)
      AND (p_municipality_id IS NULL OR d.municipality_id = p_municipality_id)
      AND (p_specialty_id IS NULL OR EXISTS (
              SELECT 1
              FROM people.doctor_specialty ds
              JOIN people.specialty s ON s.id = ds.specialty_id AND s.is_active
              WHERE ds.doctor_id    = b.doctor_id
                AND ds.specialty_id = p_specialty_id))
      AND NOT EXISTS (
              SELECT 1
              FROM scheduling.appointment a
              WHERE a.doctor_id = b.doctor_id
                AND a.status IN ('pending', 'confirmed')
                AND tstzrange(a.starts_at, a.ends_at) && tstzrange(slot.slot_start, slot.slot_start + p_duration))
      AND (p_patient_id IS NULL OR NOT EXISTS (
              SELECT 1
              FROM scheduling.appointment a
              WHERE a.patient_id = p_patient_id
                AND a.status IN ('pending', 'confirmed')
                AND tstzrange(a.starts_at, a.ends_at) && tstzrange(slot.slot_start, slot.slot_start + p_duration)))
    ORDER BY slot.slot_start, b.doctor_id;
$$;

COMMENT ON FUNCTION scheduling.sp_availability_get_free_slots(timestamptz, timestamptz, interval, integer, integer, integer, integer, integer) IS
    'Horarios libres por medico (bloques abiertos menos citas activas), filtrables por especialidad y ubicacion.';

-- Asignacion aleatoria de medico: elige al azar un medico activo de la
-- especialidad, libre en ese horario y dentro de los filtros de ubicacion.
-- Devuelve NULL si ninguno esta libre.
CREATE FUNCTION scheduling.sp_doctor_pick_random(
    p_specialty_id     integer,
    p_starts_at        timestamptz,
    p_ends_at          timestamptz,
    p_province_id      integer DEFAULT NULL,
    p_municipality_id  integer DEFAULT NULL,
    p_patient_id       integer DEFAULT NULL
)
RETURNS integer
LANGUAGE sql
VOLATILE
AS $$
    SELECT f.doctor_id
    FROM scheduling.sp_availability_get_free_slots(
             p_starts_at, p_ends_at, p_ends_at - p_starts_at,
             NULL, p_specialty_id, p_province_id, p_municipality_id, p_patient_id) AS f
    WHERE f.starts_at = p_starts_at
    ORDER BY random()
    LIMIT 1;
$$;

COMMENT ON FUNCTION scheduling.sp_doctor_pick_random(integer, timestamptz, timestamptz, integer, integer, integer) IS
    'Medico al azar libre en el horario indicado; NULL si no hay ninguno.';
