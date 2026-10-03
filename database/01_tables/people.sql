-- =============================================================================
-- 01_tables/people.sql
-- Personas y catalogos asociados.
-- =============================================================================

CREATE TABLE people.province (
    id          integer       GENERATED ALWAYS AS IDENTITY,
    name        varchar(100)  NOT NULL,
    created_at  timestamptz   NOT NULL DEFAULT now(),
    created_by  integer       NULL,
    updated_at  timestamptz   NULL,
    updated_by  integer       NULL,
    CONSTRAINT pk_province PRIMARY KEY (id)
);
COMMENT ON TABLE people.province IS 'Provincias de Republica Dominicana y el Distrito Nacional. Dato semilla.';

CREATE TABLE people.municipality (
    id           integer       GENERATED ALWAYS AS IDENTITY,
    province_id  integer       NOT NULL,
    name         varchar(100)  NOT NULL,
    created_at   timestamptz   NOT NULL DEFAULT now(),
    created_by   integer       NULL,
    updated_at   timestamptz   NULL,
    updated_by   integer       NULL,
    CONSTRAINT pk_municipality PRIMARY KEY (id)
);
COMMENT ON TABLE people.municipality IS 'Municipios de Republica Dominicana, cada uno dentro de una provincia. Dato semilla.';

CREATE TABLE people.insurance_provider (
    id             integer       GENERATED ALWAYS AS IDENTITY,
    name           varchar(150)  NOT NULL,
    contact_phone  varchar(20)   NOT NULL,
    contact_email  varchar(256)  NOT NULL,
    address        varchar(250)  NOT NULL,
    website        varchar(250)  NULL,
    is_active      boolean       NOT NULL DEFAULT true,
    created_at     timestamptz   NOT NULL DEFAULT now(),
    created_by     integer       NULL,
    updated_at     timestamptz   NULL,
    updated_by     integer       NULL,
    CONSTRAINT pk_insurance_provider PRIMARY KEY (id)
);
COMMENT ON TABLE people.insurance_provider IS 'Proveedores de seguros. No se borran: se desactivan.';

CREATE TABLE people.specialty (
    id           integer       GENERATED ALWAYS AS IDENTITY,
    name         varchar(100)  NOT NULL,
    description  varchar(500)  NOT NULL,
    is_active    boolean       NOT NULL DEFAULT true,
    created_at   timestamptz   NOT NULL DEFAULT now(),
    created_by   integer       NULL,
    updated_at   timestamptz   NULL,
    updated_by   integer       NULL,
    CONSTRAINT pk_specialty PRIMARY KEY (id)
);
COMMENT ON TABLE people.specialty IS 'Especialidades medicas. Nombre unico sin mayusculas ni tildes. No se borran: se desactivan.';

CREATE TABLE people.patient (
    id                       integer       GENERATED ALWAYS AS IDENTITY,
    user_id                  integer       NOT NULL,
    first_name               varchar(100)  NOT NULL,
    last_name                varchar(100)  NOT NULL,
    birth_date               date          NOT NULL,
    phone_number             varchar(20)   NOT NULL,
    address                  varchar(250)  NULL,
    emergency_contact_name   varchar(150)  NULL,
    emergency_contact_phone  varchar(20)   NULL,
    insurance_provider_id    integer       NULL,
    policy_number            varchar(50)   NULL,
    is_active                boolean       NOT NULL DEFAULT true,
    created_at               timestamptz   NOT NULL DEFAULT now(),
    created_by               integer       NULL,
    updated_at               timestamptz   NULL,
    updated_by               integer       NULL,
    CONSTRAINT pk_patient PRIMARY KEY (id)
);
COMMENT ON TABLE people.patient IS 'Perfil del paciente. first_name, last_name y birth_date solo los corrige el administrador.';

CREATE TABLE people.doctor (
    id                   integer        GENERATED ALWAYS AS IDENTITY,
    user_id              integer        NOT NULL,
    first_name           varchar(100)   NOT NULL,
    last_name            varchar(100)   NOT NULL,
    license_number       varchar(50)    NOT NULL,
    phone_number         varchar(20)    NOT NULL,
    province_id          integer        NOT NULL,
    municipality_id      integer        NOT NULL,
    address_line         varchar(250)   NOT NULL,
    consultation_fee     numeric(10,2)  NOT NULL,
    years_of_experience  integer        NOT NULL,
    biography            varchar(1000)  NOT NULL,
    is_active            boolean        NOT NULL DEFAULT true,
    created_at           timestamptz    NOT NULL DEFAULT now(),
    created_by           integer        NULL,
    updated_at           timestamptz    NULL,
    updated_by           integer        NULL,
    CONSTRAINT pk_doctor PRIMARY KEY (id)
);
COMMENT ON TABLE people.doctor IS 'Perfil del medico. phone_number y license_number solo los ve el administrador.';

CREATE TABLE people.doctor_specialty (
    doctor_id     integer      NOT NULL,
    specialty_id  integer      NOT NULL,
    created_at    timestamptz  NOT NULL DEFAULT now(),
    created_by    integer      NULL,
    updated_at    timestamptz  NULL,
    updated_by    integer      NULL,
    CONSTRAINT pk_doctor_specialty PRIMARY KEY (doctor_id, specialty_id)
);
COMMENT ON TABLE people.doctor_specialty IS 'Relacion N:M entre medicos y especialidades.';

CREATE TABLE people.administrator (
    id          integer       GENERATED ALWAYS AS IDENTITY,
    user_id     integer       NOT NULL,
    first_name  varchar(100)  NOT NULL,
    last_name   varchar(100)  NOT NULL,
    is_active   boolean       NOT NULL DEFAULT true,
    created_at  timestamptz   NOT NULL DEFAULT now(),
    created_by  integer       NULL,
    updated_at  timestamptz   NULL,
    updated_by  integer       NULL,
    CONSTRAINT pk_administrator PRIMARY KEY (id)
);
COMMENT ON TABLE people.administrator IS 'Perfil del administrador de la aplicacion. Lo crea el seeder de la API desde la configuracion.';
