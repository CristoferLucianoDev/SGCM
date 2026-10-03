-- =============================================================================
-- 01_tables/messaging.sql
-- Recordatorios y plantillas.
-- =============================================================================

CREATE TABLE messaging.reminder_template (
    id             integer        GENERATED ALWAYS AS IDENTITY,
    doctor_id      integer        NULL,
    name           varchar(100)   NOT NULL,
    subject        varchar(200)   NOT NULL,
    body_template  varchar(2000)  NOT NULL,
    is_active      boolean        NOT NULL DEFAULT true,
    created_at     timestamptz    NOT NULL DEFAULT now(),
    created_by     integer        NULL,
    updated_at     timestamptz    NULL,
    updated_by     integer        NULL,
    CONSTRAINT pk_reminder_template PRIMARY KEY (id)
);
COMMENT ON TABLE messaging.reminder_template IS 'doctor_id NULL = plantilla global (solo dato semilla).';
COMMENT ON COLUMN messaging.reminder_template.body_template IS 'Variables: {PatientName} {DoctorName} {AppointmentDate} {StartTime} {SpecialtyName}.';

CREATE TABLE messaging.reminder (
    id               integer        GENERATED ALWAYS AS IDENTITY,
    appointment_id   integer        NOT NULL,
    template_id      integer        NULL,
    message_body     varchar(2000)  NOT NULL,
    scheduled_at     timestamptz    NOT NULL,
    sent_at          timestamptz    NULL,
    delivery_status  varchar(20)    NOT NULL DEFAULT 'pending',
    created_at       timestamptz    NOT NULL DEFAULT now(),
    created_by       integer        NULL,
    updated_at       timestamptz    NULL,
    updated_by       integer        NULL,
    CONSTRAINT pk_reminder PRIMARY KEY (id)
);
COMMENT ON TABLE messaging.reminder IS 'Recordatorio automatico (created_by NULL) o personalizado del medico. message_body ya renderizado.';
