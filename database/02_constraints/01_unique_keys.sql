-- =============================================================================
-- 02_constraints/01_unique_keys.sql
-- Restricciones e indices unicos. Van antes de las claves foraneas porque las
-- FK compuestas se apoyan en uq_municipality_id_province_id,
-- uq_appointment_id_patient_id y uq_doctor_availability_series_id_doctor_id.
-- =============================================================================

-- app
ALTER TABLE app.role       ADD CONSTRAINT uq_role_name       UNIQUE (name);
ALTER TABLE app.token_type ADD CONSTRAINT uq_token_type_name UNIQUE (name);
ALTER TABLE app."user"     ADD CONSTRAINT uq_user_email      UNIQUE (email);
ALTER TABLE app.user_session ADD CONSTRAINT uq_user_session_refresh_token_hash UNIQUE (refresh_token_hash);

-- Un solo token vigente (sin usar ni anular) por usuario y tipo.
CREATE UNIQUE INDEX uq_user_token_user_id_token_type_id_active
    ON app.user_token (user_id, token_type_id)
    WHERE used_at IS NULL AND invalidated_at IS NULL;

-- people
ALTER TABLE people.province ADD CONSTRAINT uq_province_name UNIQUE (name);
ALTER TABLE people.municipality
    ADD CONSTRAINT uq_municipality_province_id_name UNIQUE (province_id, name),
    ADD CONSTRAINT uq_municipality_id_province_id   UNIQUE (id, province_id);
ALTER TABLE people.insurance_provider ADD CONSTRAINT uq_insurance_provider_name UNIQUE (name);
ALTER TABLE people.patient       ADD CONSTRAINT uq_patient_user_id       UNIQUE (user_id);
ALTER TABLE people.doctor
    ADD CONSTRAINT uq_doctor_user_id        UNIQUE (user_id),
    ADD CONSTRAINT uq_doctor_license_number UNIQUE (license_number);
ALTER TABLE people.administrator ADD CONSTRAINT uq_administrator_user_id UNIQUE (user_id);

-- Nombre de especialidad unico sin mayusculas ni tildes.
CREATE UNIQUE INDEX uq_specialty_name ON people.specialty (people.sp_text_normalize(name));

-- scheduling
ALTER TABLE scheduling.doctor_availability_series
    ADD CONSTRAINT uq_doctor_availability_series_id_doctor_id UNIQUE (id, doctor_id);
ALTER TABLE scheduling.appointment
    ADD CONSTRAINT uq_appointment_id_patient_id UNIQUE (id, patient_id);
