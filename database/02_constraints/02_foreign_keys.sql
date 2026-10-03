-- =============================================================================
-- 02_constraints/02_foreign_keys.sql
-- Claves foraneas. Regla general: sin cascada, porque el borrado es logico.
-- Excepcion: user_token, user_two_factor, user_recovery_code y user_session
-- usan ON DELETE CASCADE para que borrar una cuenta sin confirmar arrastre sus
-- datos tecnicos. medical_record_access_log nunca usa cascada.
-- =============================================================================

-- ---------------------------------------------------------------------------
-- Relaciones del dominio
-- ---------------------------------------------------------------------------

-- app
ALTER TABLE app."user"
    ADD CONSTRAINT fk_user_role FOREIGN KEY (role_id) REFERENCES app.role (id);
ALTER TABLE app.user_token
    ADD CONSTRAINT fk_user_token_user       FOREIGN KEY (user_id)       REFERENCES app."user" (id) ON DELETE CASCADE,
    ADD CONSTRAINT fk_user_token_token_type FOREIGN KEY (token_type_id) REFERENCES app.token_type (id);
ALTER TABLE app.user_two_factor
    ADD CONSTRAINT fk_user_two_factor_user FOREIGN KEY (user_id) REFERENCES app."user" (id) ON DELETE CASCADE;
ALTER TABLE app.user_recovery_code
    ADD CONSTRAINT fk_user_recovery_code_user FOREIGN KEY (user_id) REFERENCES app."user" (id) ON DELETE CASCADE;
ALTER TABLE app.user_session
    ADD CONSTRAINT fk_user_session_user FOREIGN KEY (user_id) REFERENCES app."user" (id) ON DELETE CASCADE;

-- people
ALTER TABLE people.municipality
    ADD CONSTRAINT fk_municipality_province FOREIGN KEY (province_id) REFERENCES people.province (id);
ALTER TABLE people.patient
    ADD CONSTRAINT fk_patient_user               FOREIGN KEY (user_id)               REFERENCES app."user" (id),
    ADD CONSTRAINT fk_patient_insurance_provider FOREIGN KEY (insurance_provider_id) REFERENCES people.insurance_provider (id);
ALTER TABLE people.doctor
    ADD CONSTRAINT fk_doctor_user         FOREIGN KEY (user_id)     REFERENCES app."user" (id),
    ADD CONSTRAINT fk_doctor_province     FOREIGN KEY (province_id) REFERENCES people.province (id),
    -- El municipio debe pertenecer a la provincia seleccionada.
    ADD CONSTRAINT fk_doctor_municipality FOREIGN KEY (municipality_id, province_id)
        REFERENCES people.municipality (id, province_id);
ALTER TABLE people.doctor_specialty
    ADD CONSTRAINT fk_doctor_specialty_doctor    FOREIGN KEY (doctor_id)    REFERENCES people.doctor (id),
    ADD CONSTRAINT fk_doctor_specialty_specialty FOREIGN KEY (specialty_id) REFERENCES people.specialty (id);
ALTER TABLE people.administrator
    ADD CONSTRAINT fk_administrator_user FOREIGN KEY (user_id) REFERENCES app."user" (id);

-- scheduling
ALTER TABLE scheduling.doctor_availability_series
    ADD CONSTRAINT fk_doctor_availability_series_doctor FOREIGN KEY (doctor_id) REFERENCES people.doctor (id);
ALTER TABLE scheduling.doctor_availability
    ADD CONSTRAINT fk_doctor_availability_doctor FOREIGN KEY (doctor_id) REFERENCES people.doctor (id),
    -- Un bloque generado pertenece a una serie del mismo medico.
    ADD CONSTRAINT fk_doctor_availability_doctor_availability_series FOREIGN KEY (series_id, doctor_id)
        REFERENCES scheduling.doctor_availability_series (id, doctor_id);
ALTER TABLE scheduling.appointment
    ADD CONSTRAINT fk_appointment_patient   FOREIGN KEY (patient_id)   REFERENCES people.patient (id),
    ADD CONSTRAINT fk_appointment_doctor    FOREIGN KEY (doctor_id)    REFERENCES people.doctor (id),
    ADD CONSTRAINT fk_appointment_specialty FOREIGN KEY (specialty_id) REFERENCES people.specialty (id);
ALTER TABLE scheduling.appointment_report_log
    ADD CONSTRAINT fk_appointment_report_log_doctor FOREIGN KEY (doctor_id) REFERENCES people.doctor (id);

-- clinical
ALTER TABLE clinical.medical_record
    -- El paciente del registro es el paciente de la cita.
    ADD CONSTRAINT fk_medical_record_appointment FOREIGN KEY (appointment_id, patient_id)
        REFERENCES scheduling.appointment (id, patient_id),
    ADD CONSTRAINT fk_medical_record_patient FOREIGN KEY (patient_id) REFERENCES people.patient (id);
ALTER TABLE clinical.medical_record_access_log
    ADD CONSTRAINT fk_medical_record_access_log_medical_record FOREIGN KEY (medical_record_id) REFERENCES clinical.medical_record (id),
    ADD CONSTRAINT fk_medical_record_access_log_user           FOREIGN KEY (accessed_by)       REFERENCES app."user" (id);

-- messaging
ALTER TABLE messaging.reminder_template
    ADD CONSTRAINT fk_reminder_template_doctor FOREIGN KEY (doctor_id) REFERENCES people.doctor (id);
ALTER TABLE messaging.reminder
    ADD CONSTRAINT fk_reminder_appointment       FOREIGN KEY (appointment_id) REFERENCES scheduling.appointment (id),
    ADD CONSTRAINT fk_reminder_reminder_template FOREIGN KEY (template_id)    REFERENCES messaging.reminder_template (id);

-- ---------------------------------------------------------------------------
-- Auditoria: created_by y updated_by de todas las tablas hacia app."user".
-- NULL = accion automatica del sistema o fila creada durante el registro.
-- ---------------------------------------------------------------------------

ALTER TABLE app.role
    ADD CONSTRAINT fk_role_created_by FOREIGN KEY (created_by) REFERENCES app."user" (id),
    ADD CONSTRAINT fk_role_updated_by FOREIGN KEY (updated_by) REFERENCES app."user" (id);

ALTER TABLE app.token_type
    ADD CONSTRAINT fk_token_type_created_by FOREIGN KEY (created_by) REFERENCES app."user" (id),
    ADD CONSTRAINT fk_token_type_updated_by FOREIGN KEY (updated_by) REFERENCES app."user" (id);

ALTER TABLE app."user"
    ADD CONSTRAINT fk_user_created_by FOREIGN KEY (created_by) REFERENCES app."user" (id),
    ADD CONSTRAINT fk_user_updated_by FOREIGN KEY (updated_by) REFERENCES app."user" (id);

ALTER TABLE app.user_token
    ADD CONSTRAINT fk_user_token_created_by FOREIGN KEY (created_by) REFERENCES app."user" (id),
    ADD CONSTRAINT fk_user_token_updated_by FOREIGN KEY (updated_by) REFERENCES app."user" (id);

ALTER TABLE app.user_two_factor
    ADD CONSTRAINT fk_user_two_factor_created_by FOREIGN KEY (created_by) REFERENCES app."user" (id),
    ADD CONSTRAINT fk_user_two_factor_updated_by FOREIGN KEY (updated_by) REFERENCES app."user" (id);

ALTER TABLE app.user_recovery_code
    ADD CONSTRAINT fk_user_recovery_code_created_by FOREIGN KEY (created_by) REFERENCES app."user" (id),
    ADD CONSTRAINT fk_user_recovery_code_updated_by FOREIGN KEY (updated_by) REFERENCES app."user" (id);

ALTER TABLE app.user_session
    ADD CONSTRAINT fk_user_session_created_by FOREIGN KEY (created_by) REFERENCES app."user" (id),
    ADD CONSTRAINT fk_user_session_updated_by FOREIGN KEY (updated_by) REFERENCES app."user" (id);

ALTER TABLE people.province
    ADD CONSTRAINT fk_province_created_by FOREIGN KEY (created_by) REFERENCES app."user" (id),
    ADD CONSTRAINT fk_province_updated_by FOREIGN KEY (updated_by) REFERENCES app."user" (id);

ALTER TABLE people.municipality
    ADD CONSTRAINT fk_municipality_created_by FOREIGN KEY (created_by) REFERENCES app."user" (id),
    ADD CONSTRAINT fk_municipality_updated_by FOREIGN KEY (updated_by) REFERENCES app."user" (id);

ALTER TABLE people.insurance_provider
    ADD CONSTRAINT fk_insurance_provider_created_by FOREIGN KEY (created_by) REFERENCES app."user" (id),
    ADD CONSTRAINT fk_insurance_provider_updated_by FOREIGN KEY (updated_by) REFERENCES app."user" (id);

ALTER TABLE people.specialty
    ADD CONSTRAINT fk_specialty_created_by FOREIGN KEY (created_by) REFERENCES app."user" (id),
    ADD CONSTRAINT fk_specialty_updated_by FOREIGN KEY (updated_by) REFERENCES app."user" (id);

ALTER TABLE people.patient
    ADD CONSTRAINT fk_patient_created_by FOREIGN KEY (created_by) REFERENCES app."user" (id),
    ADD CONSTRAINT fk_patient_updated_by FOREIGN KEY (updated_by) REFERENCES app."user" (id);

ALTER TABLE people.doctor
    ADD CONSTRAINT fk_doctor_created_by FOREIGN KEY (created_by) REFERENCES app."user" (id),
    ADD CONSTRAINT fk_doctor_updated_by FOREIGN KEY (updated_by) REFERENCES app."user" (id);

ALTER TABLE people.doctor_specialty
    ADD CONSTRAINT fk_doctor_specialty_created_by FOREIGN KEY (created_by) REFERENCES app."user" (id),
    ADD CONSTRAINT fk_doctor_specialty_updated_by FOREIGN KEY (updated_by) REFERENCES app."user" (id);

ALTER TABLE people.administrator
    ADD CONSTRAINT fk_administrator_created_by FOREIGN KEY (created_by) REFERENCES app."user" (id),
    ADD CONSTRAINT fk_administrator_updated_by FOREIGN KEY (updated_by) REFERENCES app."user" (id);

ALTER TABLE scheduling.doctor_availability_series
    ADD CONSTRAINT fk_doctor_availability_series_created_by FOREIGN KEY (created_by) REFERENCES app."user" (id),
    ADD CONSTRAINT fk_doctor_availability_series_updated_by FOREIGN KEY (updated_by) REFERENCES app."user" (id);

ALTER TABLE scheduling.doctor_availability
    ADD CONSTRAINT fk_doctor_availability_created_by FOREIGN KEY (created_by) REFERENCES app."user" (id),
    ADD CONSTRAINT fk_doctor_availability_updated_by FOREIGN KEY (updated_by) REFERENCES app."user" (id);

ALTER TABLE scheduling.appointment
    ADD CONSTRAINT fk_appointment_created_by FOREIGN KEY (created_by) REFERENCES app."user" (id),
    ADD CONSTRAINT fk_appointment_updated_by FOREIGN KEY (updated_by) REFERENCES app."user" (id);

ALTER TABLE scheduling.appointment_report_log
    ADD CONSTRAINT fk_appointment_report_log_created_by FOREIGN KEY (created_by) REFERENCES app."user" (id),
    ADD CONSTRAINT fk_appointment_report_log_updated_by FOREIGN KEY (updated_by) REFERENCES app."user" (id);

ALTER TABLE clinical.medical_record
    ADD CONSTRAINT fk_medical_record_created_by FOREIGN KEY (created_by) REFERENCES app."user" (id),
    ADD CONSTRAINT fk_medical_record_updated_by FOREIGN KEY (updated_by) REFERENCES app."user" (id);

ALTER TABLE clinical.medical_record_access_log
    ADD CONSTRAINT fk_medical_record_access_log_created_by FOREIGN KEY (created_by) REFERENCES app."user" (id),
    ADD CONSTRAINT fk_medical_record_access_log_updated_by FOREIGN KEY (updated_by) REFERENCES app."user" (id);

ALTER TABLE messaging.reminder_template
    ADD CONSTRAINT fk_reminder_template_created_by FOREIGN KEY (created_by) REFERENCES app."user" (id),
    ADD CONSTRAINT fk_reminder_template_updated_by FOREIGN KEY (updated_by) REFERENCES app."user" (id);

ALTER TABLE messaging.reminder
    ADD CONSTRAINT fk_reminder_created_by FOREIGN KEY (created_by) REFERENCES app."user" (id),
    ADD CONSTRAINT fk_reminder_updated_by FOREIGN KEY (updated_by) REFERENCES app."user" (id);
