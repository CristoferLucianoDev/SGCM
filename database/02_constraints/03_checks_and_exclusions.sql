-- =============================================================================
-- 02_constraints/03_checks_and_exclusions.sql
-- CHECK de listas cerradas y reglas fijas, y EXCLUDE contra traslapes.
-- Los plazos de negocio (4 dias, 24/48/6 horas, 90 dias, bloque minimo de
-- 6 horas, duracion de la cita) no van aqui: son configurables por ambiente y
-- los valida el dominio.
-- "Hora en punto" se valida con el epoch (segundos desde 1970 en UTC), que no
-- depende de la zona horaria de la sesion.
-- =============================================================================

-- ---------------------------------------------------------------------------
-- app
-- ---------------------------------------------------------------------------

-- La aplicacion guarda el correo en minusculas; asi uq_user_email no admite
-- el mismo correo escrito con otras mayusculas.
ALTER TABLE app."user"
    ADD CONSTRAINT ck_user_email_lowercase CHECK (email = lower(email));

ALTER TABLE app.user_session
    ADD CONSTRAINT ck_user_session_revocation_reason CHECK (
        revocation_reason IN ('logout', 'password_reset', 'two_factor_disabled',
                              'account_deactivated', 'token_reuse', 'session_limit')),
    -- Una sesion revocada siempre dice por que, y una activa no tiene motivo.
    ADD CONSTRAINT ck_user_session_revocation_consistency CHECK (
        (revoked_at IS NULL) = (revocation_reason IS NULL));

-- ---------------------------------------------------------------------------
-- people
-- ---------------------------------------------------------------------------

ALTER TABLE people.patient
    ADD CONSTRAINT ck_patient_policy_number CHECK (
        insurance_provider_id IS NULL OR policy_number IS NOT NULL);

ALTER TABLE people.doctor
    ADD CONSTRAINT ck_doctor_consultation_fee    CHECK (consultation_fee >= 0),
    ADD CONSTRAINT ck_doctor_years_of_experience CHECK (years_of_experience >= 0);

-- ---------------------------------------------------------------------------
-- scheduling
-- ---------------------------------------------------------------------------

ALTER TABLE scheduling.doctor_availability_series
    ADD CONSTRAINT ck_doctor_availability_series_days CHECK (
        cardinality(days_of_week) > 0
        AND array_position(days_of_week, NULL) IS NULL
        AND days_of_week <@ ARRAY[1, 2, 3, 4, 5, 6, 7]::smallint[]),
    ADD CONSTRAINT ck_doctor_availability_series_on_hour CHECK (
        extract(minute FROM start_time) = 0 AND extract(second FROM start_time) = 0
        AND extract(minute FROM end_time) = 0 AND extract(second FROM end_time) = 0),
    ADD CONSTRAINT ck_doctor_availability_series_time_range CHECK (end_time > start_time),
    ADD CONSTRAINT ck_doctor_availability_series_date_range CHECK (end_date IS NULL OR end_date >= start_date);

ALTER TABLE scheduling.doctor_availability
    ADD CONSTRAINT ck_doctor_availability_time_range CHECK (ends_at > starts_at),
    ADD CONSTRAINT ck_doctor_availability_on_hour CHECK (
        mod(extract(epoch FROM starts_at), 3600) = 0
        AND mod(extract(epoch FROM ends_at), 3600) = 0),
    -- Dos bloques del mismo medico no se cruzan, esten abiertos o cerrados.
    ADD CONSTRAINT ex_doctor_availability_no_overlap EXCLUDE USING gist (
        doctor_id WITH =,
        tstzrange(starts_at, ends_at) WITH &&);

ALTER TABLE scheduling.appointment
    ADD CONSTRAINT ck_appointment_time_range CHECK (ends_at > starts_at),
    -- Solo el inicio: el medico puede extender o terminar antes la cita.
    ADD CONSTRAINT ck_appointment_on_hour CHECK (mod(extract(epoch FROM starts_at), 3600) = 0),
    ADD CONSTRAINT ck_appointment_status CHECK (
        status IN ('pending', 'confirmed', 'rejected', 'cancelled', 'completed')),
    ADD CONSTRAINT ck_appointment_rejected_reason CHECK (
        status <> 'rejected' OR (status_reason IS NOT NULL AND btrim(status_reason) <> '')),
    -- Ni el medico ni el paciente tienen dos citas activas que se crucen.
    ADD CONSTRAINT ex_appointment_doctor_no_overlap EXCLUDE USING gist (
        doctor_id WITH =,
        tstzrange(starts_at, ends_at) WITH &&)
        WHERE (status IN ('pending', 'confirmed')),
    ADD CONSTRAINT ex_appointment_patient_no_overlap EXCLUDE USING gist (
        patient_id WITH =,
        tstzrange(starts_at, ends_at) WITH &&)
        WHERE (status IN ('pending', 'confirmed'));

ALTER TABLE scheduling.appointment_report_log
    ADD CONSTRAINT ck_appointment_report_log_action      CHECK (action IN ('generate', 'export')),
    ADD CONSTRAINT ck_appointment_report_log_report_type CHECK (report_type IN ('completed', 'cancelled', 'pending', 'all')),
    ADD CONSTRAINT ck_appointment_report_log_export_format CHECK (
        (action = 'export' AND export_format IN ('pdf', 'xlsx', 'docx'))
        OR (action = 'generate' AND export_format IS NULL)),
    ADD CONSTRAINT ck_appointment_report_log_row_count CHECK (row_count >= 0);

-- ---------------------------------------------------------------------------
-- clinical
-- ---------------------------------------------------------------------------

ALTER TABLE clinical.medical_record_access_log
    ADD CONSTRAINT ck_medical_record_access_log_access_type CHECK (access_type IN ('view', 'create', 'update'));

-- ---------------------------------------------------------------------------
-- messaging
-- ---------------------------------------------------------------------------

ALTER TABLE messaging.reminder
    ADD CONSTRAINT ck_reminder_delivery_status CHECK (
        delivery_status IN ('pending', 'sent', 'failed', 'cancelled'));

-- Solo se aceptan las variables de la lista; cualquier otra {Variable} se rechaza.
ALTER TABLE messaging.reminder_template
    ADD CONSTRAINT ck_reminder_template_variables CHECK (
        subject !~ '\{(?!(PatientName|DoctorName|AppointmentDate|StartTime|SpecialtyName)\})[^{}]*\}'
        AND body_template !~ '\{(?!(PatientName|DoctorName|AppointmentDate|StartTime|SpecialtyName)\})[^{}]*\}');
