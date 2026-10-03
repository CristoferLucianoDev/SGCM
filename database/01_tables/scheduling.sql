-- =============================================================================
-- 01_tables/scheduling.sql
-- Disponibilidad, citas y registro de reportes.
-- =============================================================================

CREATE TABLE scheduling.doctor_availability_series (
    id            integer      GENERATED ALWAYS AS IDENTITY,
    doctor_id     integer      NOT NULL,
    days_of_week  smallint[]   NOT NULL,
    start_time    time         NOT NULL,
    end_time      time         NOT NULL,
    start_date    date         NOT NULL,
    end_date      date         NULL,
    time_zone     varchar(50)  NOT NULL DEFAULT 'America/Santo_Domingo',
    is_active     boolean      NOT NULL DEFAULT true,
    created_at    timestamptz  NOT NULL DEFAULT now(),
    created_by    integer      NULL,
    updated_at    timestamptz  NULL,
    updated_by    integer      NULL,
    CONSTRAINT pk_doctor_availability_series PRIMARY KEY (id)
);
COMMENT ON TABLE scheduling.doctor_availability_series IS 'Horario semanal recurrente. El Worker genera un bloque por dia hasta el horizonte.';
COMMENT ON COLUMN scheduling.doctor_availability_series.days_of_week IS '1 = lunes ... 7 = domingo (ISO 8601).';
COMMENT ON COLUMN scheduling.doctor_availability_series.end_date IS 'NULL = sin fin.';

CREATE TABLE scheduling.doctor_availability (
    id            integer      GENERATED ALWAYS AS IDENTITY,
    doctor_id     integer      NOT NULL,
    series_id     integer      NULL,
    starts_at     timestamptz  NOT NULL,
    ends_at       timestamptz  NOT NULL,
    is_available  boolean      NOT NULL DEFAULT true,
    is_modified   boolean      NOT NULL DEFAULT false,
    created_at    timestamptz  NOT NULL DEFAULT now(),
    created_by    integer      NULL,
    updated_at    timestamptz  NULL,
    updated_by    integer      NULL,
    CONSTRAINT pk_doctor_availability PRIMARY KEY (id)
);
COMMENT ON TABLE scheduling.doctor_availability IS 'Bloques abiertos del medico. El horario libre real = bloques abiertos menos citas activas.';
COMMENT ON COLUMN scheduling.doctor_availability.series_id IS 'NULL = bloque suelto.';
COMMENT ON COLUMN scheduling.doctor_availability.is_available IS 'false = bloque cerrado.';
COMMENT ON COLUMN scheduling.doctor_availability.is_modified IS 'Ocurrencia editada a mano: editar la serie no la sobrescribe.';

CREATE TABLE scheduling.appointment (
    id                integer       GENERATED ALWAYS AS IDENTITY,
    patient_id        integer       NOT NULL,
    doctor_id         integer       NOT NULL,
    specialty_id      integer       NOT NULL,
    starts_at         timestamptz   NOT NULL,
    ends_at           timestamptz   NOT NULL,
    status            varchar(20)   NOT NULL DEFAULT 'pending',
    reason_for_visit  varchar(500)  NULL,
    status_reason     varchar(500)  NULL,
    created_at        timestamptz   NOT NULL DEFAULT now(),
    created_by        integer       NULL,
    updated_at        timestamptz   NULL,
    updated_by        integer       NULL,
    CONSTRAINT pk_appointment PRIMARY KEY (id)
);
COMMENT ON TABLE scheduling.appointment IS 'Cita. En curso = confirmed con la hora actual entre starts_at y ends_at.';
COMMENT ON COLUMN scheduling.appointment.status_reason IS 'Motivo de cancelacion o de rechazo.';

CREATE TABLE scheduling.appointment_report_log (
    id             integer      GENERATED ALWAYS AS IDENTITY,
    doctor_id      integer      NOT NULL,
    action         varchar(20)  NOT NULL,
    report_type    varchar(20)  NOT NULL,
    filters        jsonb        NOT NULL DEFAULT '{}',
    export_format  varchar(10)  NULL,
    row_count      integer      NOT NULL,
    generated_at   timestamptz  NOT NULL DEFAULT now(),
    created_at     timestamptz  NOT NULL DEFAULT now(),
    created_by     integer      NULL,
    updated_at     timestamptz  NULL,
    updated_by     integer      NULL,
    CONSTRAINT pk_appointment_report_log PRIMARY KEY (id)
);
COMMENT ON TABLE scheduling.appointment_report_log IS 'Reportes generados o exportados por un medico. Solo insercion.';
