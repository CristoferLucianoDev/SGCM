-- =============================================================================
-- 01_tables/clinical.sql
-- Registro clinico versionado y registro de accesos. Ambas tablas son de solo
-- insercion: sgcm_app no tiene UPDATE ni DELETE sobre ellas.
-- =============================================================================

CREATE TABLE clinical.medical_record (
    id              integer        GENERATED ALWAYS AS IDENTITY,
    appointment_id  integer        NOT NULL,
    patient_id      integer        NOT NULL,
    diagnosis       varchar(1000)  NULL,
    treatment       varchar(1000)  NULL,
    notes           varchar(2000)  NULL,
    created_at      timestamptz    NOT NULL DEFAULT now(),
    created_by      integer        NULL,
    updated_at      timestamptz    NULL,
    updated_by      integer        NULL,
    CONSTRAINT pk_medical_record PRIMARY KEY (id)
);
COMMENT ON TABLE clinical.medical_record IS 'Cada edicion es una fila nueva; la vigente de una cita es la de created_at mas reciente.';

CREATE TABLE clinical.medical_record_access_log (
    id                 integer      GENERATED ALWAYS AS IDENTITY,
    medical_record_id  integer      NOT NULL,
    accessed_by        integer      NOT NULL,
    access_type        varchar(20)  NOT NULL,
    accessed_at        timestamptz  NOT NULL DEFAULT now(),
    ip_address         varchar(45)  NULL,
    created_at         timestamptz  NOT NULL DEFAULT now(),
    created_by         integer      NULL,
    updated_at         timestamptz  NULL,
    updated_by         integer      NULL,
    CONSTRAINT pk_medical_record_access_log PRIMARY KEY (id)
);
COMMENT ON TABLE clinical.medical_record_access_log IS 'Cada consulta (view), creacion (create) o nueva version (update) de un registro clinico.';
