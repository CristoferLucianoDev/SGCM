-- =============================================================================
-- 04_views/views.sql
-- Vistas de solo lectura. EF Core las mapea como entidades sin clave.
-- =============================================================================

-- Directorio publico de medicos: lo pueden ver usuarios sin sesion, por eso
-- no incluye phone_number ni license_number. Solo medicos con cuenta y perfil
-- activos, y solo sus especialidades activas.
CREATE VIEW people.vw_doctor_directory AS
SELECT
    d.id,
    d.first_name,
    d.last_name,
    d.province_id,
    pr.name                                   AS province_name,
    d.municipality_id,
    m.name                                    AS municipality_name,
    d.address_line,
    d.consultation_fee,
    d.years_of_experience,
    d.biography,
    COALESCE(sp.specialty_ids,   '{}')        AS specialty_ids,
    COALESCE(sp.specialty_names, '{}')        AS specialty_names
FROM people.doctor d
JOIN app."user"          u  ON u.id  = d.user_id
JOIN people.province     pr ON pr.id = d.province_id
JOIN people.municipality m  ON m.id  = d.municipality_id
LEFT JOIN LATERAL (
    SELECT array_agg(s.id   ORDER BY s.name) AS specialty_ids,
           array_agg(s.name ORDER BY s.name) AS specialty_names
    FROM people.doctor_specialty ds
    JOIN people.specialty s ON s.id = ds.specialty_id
    WHERE ds.doctor_id = d.id
      AND s.is_active
) sp ON true
WHERE d.is_active
  AND u.is_active;

COMMENT ON VIEW people.vw_doctor_directory IS
    'Directorio publico de medicos activos, sin telefono ni licencia.';

-- Detalle de citas en cualquier estado: vista de lista con filtros, historial
-- del paciente, calendario y reportes.
CREATE VIEW scheduling.vw_appointment_detail AS
SELECT
    a.id,
    a.patient_id,
    p.first_name                              AS patient_first_name,
    p.last_name                               AS patient_last_name,
    a.doctor_id,
    d.first_name                              AS doctor_first_name,
    d.last_name                               AS doctor_last_name,
    a.specialty_id,
    s.name                                    AS specialty_name,
    d.consultation_fee,
    a.starts_at,
    a.ends_at,
    a.status,
    (a.status = 'confirmed' AND now() >= a.starts_at AND now() < a.ends_at) AS is_in_progress,
    a.reason_for_visit,
    a.status_reason,
    a.created_at,
    a.created_by,
    a.updated_at,
    a.updated_by
FROM scheduling.appointment a
JOIN people.patient   p ON p.id = a.patient_id
JOIN people.doctor    d ON d.id = a.doctor_id
JOIN people.specialty s ON s.id = a.specialty_id;

COMMENT ON VIEW scheduling.vw_appointment_detail IS
    'Citas en cualquier estado con paciente, medico y especialidad. is_in_progress se calcula con la hora actual.';

-- Agenda del medico: citas activas (pending y confirmed), con el contacto del
-- paciente.
CREATE VIEW scheduling.vw_doctor_schedule AS
SELECT
    a.id,
    a.doctor_id,
    a.patient_id,
    p.first_name                              AS patient_first_name,
    p.last_name                               AS patient_last_name,
    p.phone_number                            AS patient_phone_number,
    a.specialty_id,
    s.name                                    AS specialty_name,
    a.starts_at,
    a.ends_at,
    a.status,
    (a.status = 'confirmed' AND now() >= a.starts_at AND now() < a.ends_at) AS is_in_progress,
    a.reason_for_visit
FROM scheduling.appointment a
JOIN people.patient   p ON p.id = a.patient_id
JOIN people.specialty s ON s.id = a.specialty_id
WHERE a.status IN ('pending', 'confirmed');

COMMENT ON VIEW scheduling.vw_doctor_schedule IS
    'Agenda del medico: citas pending y confirmed.';

-- Version vigente del registro clinico de cada cita (la mas reciente). Es lo
-- que ve el paciente; el medico tratante consulta ademas la tabla completa.
CREATE VIEW clinical.vw_medical_record_current AS
SELECT DISTINCT ON (mr.appointment_id)
    mr.id,
    mr.appointment_id,
    mr.patient_id,
    a.doctor_id,
    a.specialty_id,
    a.starts_at                               AS appointment_starts_at,
    mr.diagnosis,
    mr.treatment,
    mr.notes,
    count(*) OVER (PARTITION BY mr.appointment_id) AS version_count,
    mr.created_at,
    mr.created_by
FROM clinical.medical_record mr
JOIN scheduling.appointment a ON a.id = mr.appointment_id
ORDER BY mr.appointment_id, mr.created_at DESC, mr.id DESC;

COMMENT ON VIEW clinical.vw_medical_record_current IS
    'Version vigente de cada registro clinico y cuantas versiones tiene.';
