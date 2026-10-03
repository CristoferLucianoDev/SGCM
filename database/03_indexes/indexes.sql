-- =============================================================================
-- 03_indexes/indexes.sql
-- Indices de consulta frecuente. Los indices que respaldan PK, UNIQUE y EXCLUDE
-- ya se crearon con sus restricciones. municipality(province_id) no se crea:
-- uq_municipality_province_id_name empieza por province_id y lo cubre.
-- =============================================================================

-- app
CREATE INDEX ix_user_token_user_id_token_type_id ON app.user_token (user_id, token_type_id);
-- Purga de registros pendientes de confirmacion.
CREATE INDEX ix_user_created_at_unconfirmed ON app."user" (created_at) WHERE is_email_confirmed = false;
-- Sesiones activas de un usuario (limite de 5, revocar todas).
CREATE INDEX ix_user_session_user_id_active ON app.user_session (user_id) WHERE revoked_at IS NULL;

-- people
CREATE INDEX ix_doctor_province_id_municipality_id ON people.doctor (province_id, municipality_id);
CREATE INDEX ix_doctor_specialty_specialty_id      ON people.doctor_specialty (specialty_id);

-- scheduling
CREATE INDEX ix_doctor_availability_series_doctor_id_is_active ON scheduling.doctor_availability_series (doctor_id, is_active);
CREATE INDEX ix_doctor_availability_doctor_id_starts_at        ON scheduling.doctor_availability (doctor_id, starts_at);
CREATE INDEX ix_doctor_availability_series_id                  ON scheduling.doctor_availability (series_id);
CREATE INDEX ix_appointment_doctor_id_starts_at  ON scheduling.appointment (doctor_id, starts_at);
CREATE INDEX ix_appointment_patient_id_starts_at ON scheduling.appointment (patient_id, starts_at);
-- Procesos automaticos del Worker (vencer pendientes, completar confirmadas).
CREATE INDEX ix_appointment_status_starts_at     ON scheduling.appointment (status, starts_at);
CREATE INDEX ix_appointment_report_log_doctor_id_generated_at ON scheduling.appointment_report_log (doctor_id, generated_at);

-- clinical
CREATE INDEX ix_medical_record_patient_id                ON clinical.medical_record (patient_id);
CREATE INDEX ix_medical_record_appointment_id_created_at ON clinical.medical_record (appointment_id, created_at);
CREATE INDEX ix_medical_record_access_log_medical_record_id_accessed_at ON clinical.medical_record_access_log (medical_record_id, accessed_at);
CREATE INDEX ix_medical_record_access_log_accessed_by_accessed_at       ON clinical.medical_record_access_log (accessed_by, accessed_at);

-- messaging
CREATE INDEX ix_reminder_scheduled_at_delivery_status ON messaging.reminder (scheduled_at, delivery_status);
-- T7: cancelar los recordatorios pendientes de una cita.
CREATE INDEX ix_reminder_appointment_id               ON messaging.reminder (appointment_id);
CREATE INDEX ix_reminder_template_doctor_id_is_active ON messaging.reminder_template (doctor_id, is_active);
