-- =============================================================================
-- Sistema de Gestion de Citas Medicas (SGCM)
-- sgcm_database.sql: todo deploy.sql en un solo archivo (Modelo de Datos v1.3).
-- Generado con build_single_file.py; si cambias un script, vuelve a generarlo.
--
-- El mismo script se ejecuta en la base local (Supabase CLI) y en produccion
-- (Supabase). Las contrasenas de los roles nunca se escriben en el codigo: se
-- pasan como variables de psql.
--
-- Uso:
--   psql "<cadena de conexion>" -f sgcm_database.sql \
--        -v owner_password='...' \
--        -v app_password='...' \
--        -v reporting_password='...'
--
-- Debe ejecutarlo el usuario administrador del servidor (postgres en Supabase).
-- =============================================================================

\set ON_ERROR_STOP on
\set QUIET on
-- Los scripts estan en UTF-8. En Windows psql usa por defecto la codificacion
-- de la consola (WIN1252) y dañaria las tildes y la ñ de los datos semilla.
\encoding UTF8

\if :{?owner_password}
\else
  \echo 'Falta la variable owner_password (-v owner_password=...)'
  \quit
\endif
\if :{?app_password}
\else
  \echo 'Falta la variable app_password (-v app_password=...)'
  \quit
\endif
\if :{?reporting_password}
\else
  \echo 'Falta la variable reporting_password (-v reporting_password=...)'
  \quit
\endif

BEGIN;

-- Setup: se ejecuta con el usuario administrador del servidor.
-- >>> 00_setup/01_extensions.sql
-- =============================================================================
-- 00_setup/01_extensions.sql
-- Extensiones en el esquema extensions, igual que las instala Supabase.
-- btree_gist: permite combinar = sobre enteros con && sobre rangos en los
--             EXCLUDE que impiden traslapes.
-- unaccent:   quita tildes en people.sp_text_normalize.
-- =============================================================================

CREATE SCHEMA IF NOT EXISTS extensions;

CREATE EXTENSION IF NOT EXISTS btree_gist WITH SCHEMA extensions;
CREATE EXTENSION IF NOT EXISTS unaccent   WITH SCHEMA extensions;

-- >>> 00_setup/02_roles.sql
-- =============================================================================
-- 00_setup/02_roles.sql
-- Roles de base de datos. Los roles son del servidor, no de la base, por eso se
-- crean solo si no existen y siempre se les actualiza la contrasena.
--
--   sgcm_owner      Administrador de la base de datos del sistema. Dueno de todos
--                   los esquemas y objetos; tiene todos los permisos sobre ellos.
--   sgcm_app        Lo usan la API y el Worker. CRUD sobre las tablas, salvo las
--                   de solo insercion (solo SELECT e INSERT).
--   sgcm_reporting  Solo lectura de people, scheduling y clinical. Sin acceso a
--                   app, donde viven contrasenas, tokens y sesiones.
-- =============================================================================

SELECT format('CREATE ROLE %I LOGIN', r)
FROM unnest(ARRAY['sgcm_owner', 'sgcm_app', 'sgcm_reporting']) AS r
WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = r)
\gexec

SELECT format('ALTER ROLE sgcm_owner LOGIN PASSWORD %L', :'owner_password') \gexec
SELECT format('ALTER ROLE sgcm_app LOGIN PASSWORD %L', :'app_password') \gexec
SELECT format('ALTER ROLE sgcm_reporting LOGIN PASSWORD %L', :'reporting_password') \gexec

-- El usuario que ejecuta el deploy necesita poder actuar como sgcm_owner para
-- crear los objetos a su nombre (SET ROLE en deploy.sql).
GRANT sgcm_owner TO CURRENT_USER;

-- Conexion a la base actual. sgcm_owner, como administrador, recibe todos los
-- privilegios de la base (crear esquemas y tablas temporales incluidos).
SELECT format('GRANT ALL PRIVILEGES ON DATABASE %I TO sgcm_owner', current_database()) \gexec
SELECT format('GRANT CONNECT ON DATABASE %I TO sgcm_app, sgcm_reporting', current_database()) \gexec

-- Las extensiones viven en el esquema extensions.
GRANT USAGE ON SCHEMA extensions TO sgcm_owner, sgcm_app, sgcm_reporting;

-- >>> 00_setup/03_schemas.sql
-- =============================================================================
-- 00_setup/03_schemas.sql
-- Los cinco esquemas del sistema, propiedad de sgcm_owner.
-- El esquema de credenciales se llama app porque Supabase reserva auth para su
-- propio sistema de usuarios y no permite crear objetos en el.
-- =============================================================================

CREATE SCHEMA app        AUTHORIZATION sgcm_owner;
CREATE SCHEMA people     AUTHORIZATION sgcm_owner;
CREATE SCHEMA scheduling AUTHORIZATION sgcm_owner;
CREATE SCHEMA clinical   AUTHORIZATION sgcm_owner;
CREATE SCHEMA messaging  AUTHORIZATION sgcm_owner;

COMMENT ON SCHEMA app        IS 'Credenciales, tokens, sesiones y segundo factor. Solo accesible para sgcm_app.';
COMMENT ON SCHEMA people     IS 'Personas (paciente, medico, administrador) y catalogos asociados.';
COMMENT ON SCHEMA scheduling IS 'Disponibilidad, citas y registro de reportes.';
COMMENT ON SCHEMA clinical   IS 'Registro clinico versionado y registro de accesos.';
COMMENT ON SCHEMA messaging  IS 'Recordatorios y plantillas.';


-- Todo lo demas se crea como sgcm_owner, que queda como dueno de los objetos.
SET ROLE sgcm_owner;

-- >>> 00_setup/04_base_functions.sql
-- =============================================================================
-- 00_setup/04_base_functions.sql
-- Funciones que otras piezas del script necesitan antes de existir:
--   people.sp_text_normalize   la usa el indice unico de people.specialty.
--   app.sp_audit_set_updated_at la usan los triggers de auditoria (T1).
-- =============================================================================

-- Normaliza un texto para comparaciones sin mayusculas ni tildes.
-- IMMUTABLE porque se usa en un indice; el diccionario se indica de forma
-- explicita y calificada para que el resultado no dependa del search_path.
CREATE FUNCTION people.sp_text_normalize(p_text text)
RETURNS text
LANGUAGE sql
IMMUTABLE
PARALLEL SAFE
RETURNS NULL ON NULL INPUT
AS $$
    SELECT lower(extensions.unaccent('extensions.unaccent'::regdictionary, btrim(p_text)));
$$;

COMMENT ON FUNCTION people.sp_text_normalize(text) IS
    'Quita espacios extremos, tildes y mayusculas. Base de la unicidad de people.specialty.name.';

-- T1: llena updated_at en cada UPDATE si la aplicacion no lo hizo.
-- El AuditInterceptor de EF Core lo llena en SaveChanges, pero ExecuteUpdate y
-- las sentencias del Worker lo saltan. Si la aplicacion ya envio un valor
-- distinto al anterior, se respeta (pruebas con IDateTimeProvider).
CREATE FUNCTION app.sp_audit_set_updated_at()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.updated_at IS NOT DISTINCT FROM OLD.updated_at THEN
        NEW.updated_at := now();
    END IF;
    RETURN NEW;
END;
$$;

COMMENT ON FUNCTION app.sp_audit_set_updated_at() IS
    'T1. Trigger BEFORE UPDATE comun a todas las tablas que se actualizan.';

-- >>> 01_tables/app.sql
-- =============================================================================
-- 01_tables/app.sql
-- Credenciales, tokens, sesiones y segundo factor.
-- user es palabra reservada en PostgreSQL: en SQL se escribe siempre app."user".
-- Columnas de auditoria en todas las tablas: created_at, created_by,
-- updated_at, updated_by (FK hacia app."user" en 02_constraints).
-- =============================================================================

CREATE TABLE app.role (
    id          integer      GENERATED ALWAYS AS IDENTITY,
    name        varchar(50)  NOT NULL,
    created_at  timestamptz  NOT NULL DEFAULT now(),
    created_by  integer      NULL,
    updated_at  timestamptz  NULL,
    updated_by  integer      NULL,
    CONSTRAINT pk_role PRIMARY KEY (id)
);
COMMENT ON TABLE app.role IS 'Catalogo de roles: Patient, Doctor, Administrator.';

CREATE TABLE app.token_type (
    id          integer      GENERATED ALWAYS AS IDENTITY,
    name        varchar(50)  NOT NULL,
    created_at  timestamptz  NOT NULL DEFAULT now(),
    created_by  integer      NULL,
    updated_at  timestamptz  NULL,
    updated_by  integer      NULL,
    CONSTRAINT pk_token_type PRIMARY KEY (id)
);
COMMENT ON TABLE app.token_type IS 'Catalogo de tipos de token: EmailConfirmation, PasswordReset.';

CREATE TABLE app."user" (
    id                    integer       GENERATED ALWAYS AS IDENTITY,
    email                 varchar(256)  NOT NULL,
    password_hash         varchar(512)  NOT NULL,
    role_id               integer       NOT NULL,
    is_email_confirmed    boolean       NOT NULL DEFAULT false,
    must_change_password  boolean       NOT NULL DEFAULT false,
    is_active             boolean       NOT NULL DEFAULT true,
    created_at            timestamptz   NOT NULL DEFAULT now(),
    created_by            integer       NULL,
    updated_at            timestamptz   NULL,
    updated_by            integer       NULL,
    CONSTRAINT pk_user PRIMARY KEY (id)
);
COMMENT ON TABLE app."user" IS 'Cuenta de acceso. is_email_confirmed = false es un registro pendiente.';
COMMENT ON COLUMN app."user".must_change_password IS 'true en cuentas de medico recien generadas: solo puede cambiar la contrasena.';

CREATE TABLE app.user_token (
    id              integer       GENERATED ALWAYS AS IDENTITY,
    user_id         integer       NOT NULL,
    token_type_id   integer       NOT NULL,
    token_hash      varchar(256)  NOT NULL,
    expires_at      timestamptz   NOT NULL,
    used_at         timestamptz   NULL,
    invalidated_at  timestamptz   NULL,
    created_at      timestamptz   NOT NULL DEFAULT now(),
    created_by      integer       NULL,
    updated_at      timestamptz   NULL,
    updated_by      integer       NULL,
    CONSTRAINT pk_user_token PRIMARY KEY (id)
);
COMMENT ON TABLE app.user_token IS 'Tokens de un solo uso (confirmacion de correo y recuperacion de contrasena), guardados hasheados.';
COMMENT ON COLUMN app.user_token.used_at IS 'Consumido por el usuario.';
COMMENT ON COLUMN app.user_token.invalidated_at IS 'Anulado al emitirse uno nuevo del mismo tipo.';

CREATE TABLE app.user_two_factor (
    user_id              integer       NOT NULL,
    secret_key           varchar(256)  NOT NULL,
    is_enabled           boolean       NOT NULL DEFAULT false,
    confirmed_at         timestamptz   NULL,
    last_used_time_step  bigint        NULL,
    created_at           timestamptz   NOT NULL DEFAULT now(),
    created_by           integer       NULL,
    updated_at           timestamptz   NULL,
    updated_by           integer       NULL,
    CONSTRAINT pk_user_two_factor PRIMARY KEY (user_id)
);
COMMENT ON TABLE app.user_two_factor IS 'Secreto TOTP (RFC 6238). secret_key llega cifrado desde la aplicacion.';
COMMENT ON COLUMN app.user_two_factor.last_used_time_step IS 'Impide reutilizar un codigo dentro de su ventana.';

CREATE TABLE app.user_recovery_code (
    id          integer       GENERATED ALWAYS AS IDENTITY,
    user_id     integer       NOT NULL,
    code_hash   varchar(256)  NOT NULL,
    used_at     timestamptz   NULL,
    created_at  timestamptz   NOT NULL DEFAULT now(),
    created_by  integer       NULL,
    updated_at  timestamptz   NULL,
    updated_by  integer       NULL,
    CONSTRAINT pk_user_recovery_code PRIMARY KEY (id)
);
COMMENT ON TABLE app.user_recovery_code IS 'Codigos de recuperacion del 2FA, de un solo uso y hasheados.';

CREATE TABLE app.user_session (
    id                           integer       GENERATED ALWAYS AS IDENTITY,
    user_id                      integer       NOT NULL,
    refresh_token_hash           varchar(256)  NOT NULL,
    previous_refresh_token_hash  varchar(256)  NULL,
    device_name                  varchar(150)  NULL,
    ip_address                   varchar(45)   NULL,
    expires_at                   timestamptz   NOT NULL,
    last_used_at                 timestamptz   NOT NULL,
    revoked_at                   timestamptz   NULL,
    revocation_reason            varchar(50)   NULL,
    created_at                   timestamptz   NOT NULL DEFAULT now(),
    created_by                   integer       NULL,
    updated_at                   timestamptz   NULL,
    updated_by                   integer       NULL,
    CONSTRAINT pk_user_session PRIMARY KEY (id)
);
COMMENT ON TABLE app.user_session IS 'Sesion por dispositivo. Guarda el hash del refresh token; el JWT no se almacena.';
COMMENT ON COLUMN app.user_session.previous_refresh_token_hash IS 'Hash anterior a la ultima rotacion. Presentarlo con la sesion activa y fuera del margen de gracia se trata como robo y revoca todas las sesiones.';
COMMENT ON COLUMN app.user_session.last_used_at IS 'Lo fijan el inicio de sesion y cada rotacion. Decide que sesion se revoca por limite y desde cuando corre el margen de gracia.';

-- >>> 01_tables/people.sql
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

-- >>> 01_tables/scheduling.sql
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

-- >>> 01_tables/clinical.sql
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

-- >>> 01_tables/messaging.sql
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

-- >>> 02_constraints/01_unique_keys.sql
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

-- >>> 02_constraints/02_foreign_keys.sql
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

-- >>> 02_constraints/03_checks_and_exclusions.sql
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

-- >>> 03_indexes/indexes.sql
-- =============================================================================
-- 03_indexes/indexes.sql
-- Indices de consulta frecuente. Los indices que respaldan PK, UNIQUE y EXCLUDE
-- ya se crearon con sus restricciones. municipality(province_id) no se crea:
-- uq_municipality_province_id_name empieza por province_id y lo cubre.
-- =============================================================================

-- app
CREATE INDEX ix_user_token_user_id_token_type_id ON app.user_token (user_id, token_type_id);
-- Confirmacion y restablecimiento: buscar el token por su hash.
CREATE INDEX ix_user_token_token_type_id_token_hash ON app.user_token (token_type_id, token_hash);
-- Purga de registros pendientes de confirmacion.
CREATE INDEX ix_user_created_at_unconfirmed ON app."user" (created_at) WHERE is_email_confirmed = false;
-- Sesiones activas de un usuario (limite de 5, revocar todas).
CREATE INDEX ix_user_session_user_id_active ON app.user_session (user_id) WHERE revoked_at IS NULL;
-- Permite encontrar al dueno de un hash rotado al detectar la reutilizacion.
CREATE INDEX ix_user_session_previous_refresh_token_hash ON app.user_session (previous_refresh_token_hash) WHERE previous_refresh_token_hash IS NOT NULL;

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

-- >>> 04_views/views.sql
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

-- >>> 05_functions/functions.sql
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

-- >>> 05_functions/triggers.sql
-- =============================================================================
-- 05_functions/triggers.sql
-- Triggers: la base de datos como ultima defensa. Ninguno repite plazos
-- configurables. Los que rechazan una operacion lanzan SQLSTATE 23514
-- (check_violation) con el nombre de la regla en CONSTRAINT, asi
-- Infrastructure los traduce igual que un CHECK.
--
--   T1  updated_at automatico .............. todas las tablas que se actualizan
--   T2  transiciones de estado de la cita .. scheduling.appointment
--   T3  coherencia de la cita .............. scheduling.appointment
--   T4  proteccion del bloque .............. scheduling.doctor_availability
--   T5  registro clinico con cita iniciada . clinical.medical_record
--   T6  rol de la cuenta y su perfil ....... people.patient/doctor/administrator, app."user"
--   T7  cancelar recordatorios ............. scheduling.appointment -> messaging.reminder
--   T8  revocar sesiones al desactivar ..... app."user" -> app.user_session
--
-- Los triggers de una tabla se ejecutan en orden alfabetico de nombre; T1
-- (set_updated_at) corre antes que las validaciones (validate_*).
-- =============================================================================

-- ---------------------------------------------------------------------------
-- T1. updated_at automatico (funcion en 00_setup/04_base_functions.sql)
-- ---------------------------------------------------------------------------

CREATE TRIGGER tg_role_set_updated_at
    BEFORE UPDATE ON app.role
    FOR EACH ROW EXECUTE FUNCTION app.sp_audit_set_updated_at();

CREATE TRIGGER tg_token_type_set_updated_at
    BEFORE UPDATE ON app.token_type
    FOR EACH ROW EXECUTE FUNCTION app.sp_audit_set_updated_at();

CREATE TRIGGER tg_user_set_updated_at
    BEFORE UPDATE ON app."user"
    FOR EACH ROW EXECUTE FUNCTION app.sp_audit_set_updated_at();

CREATE TRIGGER tg_user_token_set_updated_at
    BEFORE UPDATE ON app.user_token
    FOR EACH ROW EXECUTE FUNCTION app.sp_audit_set_updated_at();

CREATE TRIGGER tg_user_two_factor_set_updated_at
    BEFORE UPDATE ON app.user_two_factor
    FOR EACH ROW EXECUTE FUNCTION app.sp_audit_set_updated_at();

CREATE TRIGGER tg_user_recovery_code_set_updated_at
    BEFORE UPDATE ON app.user_recovery_code
    FOR EACH ROW EXECUTE FUNCTION app.sp_audit_set_updated_at();

CREATE TRIGGER tg_user_session_set_updated_at
    BEFORE UPDATE ON app.user_session
    FOR EACH ROW EXECUTE FUNCTION app.sp_audit_set_updated_at();

CREATE TRIGGER tg_province_set_updated_at
    BEFORE UPDATE ON people.province
    FOR EACH ROW EXECUTE FUNCTION app.sp_audit_set_updated_at();

CREATE TRIGGER tg_municipality_set_updated_at
    BEFORE UPDATE ON people.municipality
    FOR EACH ROW EXECUTE FUNCTION app.sp_audit_set_updated_at();

CREATE TRIGGER tg_insurance_provider_set_updated_at
    BEFORE UPDATE ON people.insurance_provider
    FOR EACH ROW EXECUTE FUNCTION app.sp_audit_set_updated_at();

CREATE TRIGGER tg_specialty_set_updated_at
    BEFORE UPDATE ON people.specialty
    FOR EACH ROW EXECUTE FUNCTION app.sp_audit_set_updated_at();

CREATE TRIGGER tg_patient_set_updated_at
    BEFORE UPDATE ON people.patient
    FOR EACH ROW EXECUTE FUNCTION app.sp_audit_set_updated_at();

CREATE TRIGGER tg_doctor_set_updated_at
    BEFORE UPDATE ON people.doctor
    FOR EACH ROW EXECUTE FUNCTION app.sp_audit_set_updated_at();

CREATE TRIGGER tg_doctor_specialty_set_updated_at
    BEFORE UPDATE ON people.doctor_specialty
    FOR EACH ROW EXECUTE FUNCTION app.sp_audit_set_updated_at();

CREATE TRIGGER tg_administrator_set_updated_at
    BEFORE UPDATE ON people.administrator
    FOR EACH ROW EXECUTE FUNCTION app.sp_audit_set_updated_at();

CREATE TRIGGER tg_doctor_availability_series_set_updated_at
    BEFORE UPDATE ON scheduling.doctor_availability_series
    FOR EACH ROW EXECUTE FUNCTION app.sp_audit_set_updated_at();

CREATE TRIGGER tg_doctor_availability_set_updated_at
    BEFORE UPDATE ON scheduling.doctor_availability
    FOR EACH ROW EXECUTE FUNCTION app.sp_audit_set_updated_at();

CREATE TRIGGER tg_appointment_set_updated_at
    BEFORE UPDATE ON scheduling.appointment
    FOR EACH ROW EXECUTE FUNCTION app.sp_audit_set_updated_at();

CREATE TRIGGER tg_reminder_template_set_updated_at
    BEFORE UPDATE ON messaging.reminder_template
    FOR EACH ROW EXECUTE FUNCTION app.sp_audit_set_updated_at();

CREATE TRIGGER tg_reminder_set_updated_at
    BEFORE UPDATE ON messaging.reminder
    FOR EACH ROW EXECUTE FUNCTION app.sp_audit_set_updated_at();

-- ---------------------------------------------------------------------------
-- T2. Transiciones de estado de la cita
--   Al crear:      pending o confirmed (la cita del medico nace confirmada).
--   pending   ->   confirmed, rejected, cancelled
--   confirmed ->   cancelled, completed, pending (reprogramacion del paciente)
--   rejected, cancelled y completed son finales: la cita ya no cambia.
-- ---------------------------------------------------------------------------

CREATE FUNCTION scheduling.sp_appointment_validate_status()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        IF NEW.status NOT IN ('pending', 'confirmed') THEN
            RAISE EXCEPTION 'An appointment can only be created as pending or confirmed (got %).', NEW.status
                USING ERRCODE = 'check_violation', CONSTRAINT = 'tg_appointment_initial_status';
        END IF;
        RETURN NEW;
    END IF;

    IF OLD.status IN ('rejected', 'cancelled', 'completed')
       AND (NEW.patient_id, NEW.doctor_id, NEW.specialty_id, NEW.starts_at, NEW.ends_at,
            NEW.status, NEW.reason_for_visit, NEW.status_reason)
           IS DISTINCT FROM
           (OLD.patient_id, OLD.doctor_id, OLD.specialty_id, OLD.starts_at, OLD.ends_at,
            OLD.status, OLD.reason_for_visit, OLD.status_reason) THEN
        RAISE EXCEPTION 'Appointment % is % and can no longer be modified.', OLD.id, OLD.status
            USING ERRCODE = 'check_violation', CONSTRAINT = 'tg_appointment_final_status';
    END IF;

    IF NEW.status IS DISTINCT FROM OLD.status
       AND NOT (
           (OLD.status = 'pending'   AND NEW.status IN ('confirmed', 'rejected', 'cancelled'))
        OR (OLD.status = 'confirmed' AND NEW.status IN ('cancelled', 'completed', 'pending'))
       ) THEN
        RAISE EXCEPTION 'Invalid appointment status transition: % -> %.', OLD.status, NEW.status
            USING ERRCODE = 'check_violation', CONSTRAINT = 'tg_appointment_status_transition';
    END IF;

    RETURN NEW;
END;
$$;

COMMENT ON FUNCTION scheduling.sp_appointment_validate_status() IS
    'T2. Estado inicial, transiciones validas y estados finales inmutables.';

CREATE TRIGGER tg_appointment_validate_status
    BEFORE INSERT OR UPDATE ON scheduling.appointment
    FOR EACH ROW EXECUTE FUNCTION scheduling.sp_appointment_validate_status();

-- ---------------------------------------------------------------------------
-- T3. Coherencia de la cita, al crearla o al cambiar paciente, medico,
-- especialidad u horario mientras esta activa:
--   - paciente y medico activos (perfil y cuenta);
--   - la especialidad es del medico y esta activa;
--   - la cita cabe dentro de un solo bloque abierto del medico.
-- El bloque se bloquea con FOR SHARE: si el medico lo cierra al mismo tiempo,
-- una de las dos transacciones espera a la otra y T3 o T4 la rechaza.
-- ---------------------------------------------------------------------------

CREATE FUNCTION scheduling.sp_appointment_validate_booking()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.status NOT IN ('pending', 'confirmed') THEN
        RETURN NEW;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM people.patient p
        JOIN app."user" u ON u.id = p.user_id
        WHERE p.id = NEW.patient_id AND p.is_active AND u.is_active
    ) THEN
        RAISE EXCEPTION 'Patient % is not active.', NEW.patient_id
            USING ERRCODE = 'check_violation', CONSTRAINT = 'tg_appointment_patient_active';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM people.doctor d
        JOIN app."user" u ON u.id = d.user_id
        WHERE d.id = NEW.doctor_id AND d.is_active AND u.is_active
    ) THEN
        RAISE EXCEPTION 'Doctor % is not active.', NEW.doctor_id
            USING ERRCODE = 'check_violation', CONSTRAINT = 'tg_appointment_doctor_active';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM people.doctor_specialty ds
        JOIN people.specialty s ON s.id = ds.specialty_id
        WHERE ds.doctor_id = NEW.doctor_id
          AND ds.specialty_id = NEW.specialty_id
          AND s.is_active
    ) THEN
        RAISE EXCEPTION 'Specialty % is not an active specialty of doctor %.', NEW.specialty_id, NEW.doctor_id
            USING ERRCODE = 'check_violation', CONSTRAINT = 'tg_appointment_doctor_specialty';
    END IF;

    PERFORM 1
    FROM scheduling.doctor_availability b
    WHERE b.doctor_id = NEW.doctor_id
      AND b.is_available
      AND b.starts_at <= NEW.starts_at
      AND b.ends_at   >= NEW.ends_at
    FOR SHARE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Appointment [%, %) is not inside an open availability block of doctor %.',
                        NEW.starts_at, NEW.ends_at, NEW.doctor_id
            USING ERRCODE = 'check_violation', CONSTRAINT = 'tg_appointment_within_block';
    END IF;

    RETURN NEW;
END;
$$;

COMMENT ON FUNCTION scheduling.sp_appointment_validate_booking() IS
    'T3. Paciente y medico activos, especialidad del medico y cita dentro de un bloque abierto.';

CREATE TRIGGER tg_appointment_validate_booking
    BEFORE INSERT OR UPDATE OF patient_id, doctor_id, specialty_id, starts_at, ends_at
    ON scheduling.appointment
    FOR EACH ROW EXECUTE FUNCTION scheduling.sp_appointment_validate_booking();

-- ---------------------------------------------------------------------------
-- T4. Proteccion del bloque: no se puede cerrar, acortar, mover ni borrar un
-- bloque si deja fuera citas activas. La aplicacion cancela primero esas citas
-- (con su motivo) en la misma transaccion y despues cambia el bloque.
-- ---------------------------------------------------------------------------

CREATE FUNCTION scheduling.sp_doctor_availability_validate_appointments()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM scheduling.appointment a
        WHERE a.doctor_id = OLD.doctor_id
          AND a.status IN ('pending', 'confirmed')
          AND a.starts_at >= OLD.starts_at
          AND a.ends_at   <= OLD.ends_at
          AND (
               TG_OP = 'DELETE'
            OR NOT (NEW.is_available
                    AND NEW.doctor_id = a.doctor_id
                    AND a.starts_at >= NEW.starts_at
                    AND a.ends_at   <= NEW.ends_at)
          )
    ) THEN
        RAISE EXCEPTION 'Availability block % still has active appointments; cancel them first.', OLD.id
            USING ERRCODE = 'check_violation', CONSTRAINT = 'tg_doctor_availability_active_appointments';
    END IF;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$$;

COMMENT ON FUNCTION scheduling.sp_doctor_availability_validate_appointments() IS
    'T4. Impide cerrar, recortar, mover o borrar un bloque con citas activas dentro.';

CREATE TRIGGER tg_doctor_availability_validate_appointments
    BEFORE UPDATE OF doctor_id, starts_at, ends_at, is_available OR DELETE
    ON scheduling.doctor_availability
    FOR EACH ROW EXECUTE FUNCTION scheduling.sp_doctor_availability_validate_appointments();

-- ---------------------------------------------------------------------------
-- T5. Un registro clinico solo se crea para una cita confirmed o completed
-- cuya hora de inicio ya llego.
-- ---------------------------------------------------------------------------

CREATE FUNCTION clinical.sp_medical_record_validate_appointment()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_status     varchar(20);
    v_starts_at  timestamptz;
BEGIN
    SELECT a.status, a.starts_at
    INTO v_status, v_starts_at
    FROM scheduling.appointment a
    WHERE a.id = NEW.appointment_id;

    IF v_status NOT IN ('confirmed', 'completed') OR v_starts_at > now() THEN
        RAISE EXCEPTION 'Appointment % must be confirmed or completed and already started (status %, starts %).',
                        NEW.appointment_id, v_status, v_starts_at
            USING ERRCODE = 'check_violation', CONSTRAINT = 'tg_medical_record_appointment_started';
    END IF;

    RETURN NEW;
END;
$$;

COMMENT ON FUNCTION clinical.sp_medical_record_validate_appointment() IS
    'T5. Registro clinico solo desde la hora de inicio de una cita confirmed o completed.';

CREATE TRIGGER tg_medical_record_validate_appointment
    BEFORE INSERT ON clinical.medical_record
    FOR EACH ROW EXECUTE FUNCTION clinical.sp_medical_record_validate_appointment();

-- ---------------------------------------------------------------------------
-- T6. La cuenta de un perfil tiene el rol de ese perfil, y no se cambia el rol
-- de una cuenta que ya tiene perfil.
-- ---------------------------------------------------------------------------

-- TG_ARGV[0] = nombre del rol que exige el perfil.
CREATE FUNCTION people.sp_profile_validate_role()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM app."user" u
        JOIN app.role r ON r.id = u.role_id
        WHERE u.id = NEW.user_id
          AND r.name = TG_ARGV[0]
    ) THEN
        RAISE EXCEPTION 'User % does not have the % role required by %.%.',
                        NEW.user_id, TG_ARGV[0], TG_TABLE_SCHEMA, TG_TABLE_NAME
            USING ERRCODE = 'check_violation', CONSTRAINT = 'tg_' || TG_TABLE_NAME || '_validate_role';
    END IF;
    RETURN NEW;
END;
$$;

COMMENT ON FUNCTION people.sp_profile_validate_role() IS
    'T6. El user_id de patient, doctor o administrator tiene el rol correspondiente.';

CREATE TRIGGER tg_patient_validate_role
    BEFORE INSERT OR UPDATE OF user_id ON people.patient
    FOR EACH ROW EXECUTE FUNCTION people.sp_profile_validate_role('Patient');

CREATE TRIGGER tg_doctor_validate_role
    BEFORE INSERT OR UPDATE OF user_id ON people.doctor
    FOR EACH ROW EXECUTE FUNCTION people.sp_profile_validate_role('Doctor');

CREATE TRIGGER tg_administrator_validate_role
    BEFORE INSERT OR UPDATE OF user_id ON people.administrator
    FOR EACH ROW EXECUTE FUNCTION people.sp_profile_validate_role('Administrator');

CREATE FUNCTION app.sp_user_validate_role_change()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.role_id IS DISTINCT FROM OLD.role_id AND (
           EXISTS (SELECT 1 FROM people.patient       WHERE user_id = NEW.id)
        OR EXISTS (SELECT 1 FROM people.doctor        WHERE user_id = NEW.id)
        OR EXISTS (SELECT 1 FROM people.administrator WHERE user_id = NEW.id)
    ) THEN
        RAISE EXCEPTION 'User % already has a profile; its role cannot change.', NEW.id
            USING ERRCODE = 'check_violation', CONSTRAINT = 'tg_user_validate_role_change';
    END IF;
    RETURN NEW;
END;
$$;

COMMENT ON FUNCTION app.sp_user_validate_role_change() IS
    'T6. Impide cambiar el rol de una cuenta que ya tiene perfil.';

CREATE TRIGGER tg_user_validate_role_change
    BEFORE UPDATE OF role_id ON app."user"
    FOR EACH ROW EXECUTE FUNCTION app.sp_user_validate_role_change();

-- ---------------------------------------------------------------------------
-- T7. Al cancelar o rechazar una cita, sus recordatorios pendientes pasan a
-- cancelled. updated_by es quien cancelo (NULL si fue el Worker).
-- ---------------------------------------------------------------------------

CREATE FUNCTION scheduling.sp_appointment_cancel_reminders()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE messaging.reminder
    SET delivery_status = 'cancelled',
        updated_at      = now(),
        updated_by      = NEW.updated_by
    WHERE appointment_id  = NEW.id
      AND delivery_status = 'pending';
    RETURN NULL;
END;
$$;

COMMENT ON FUNCTION scheduling.sp_appointment_cancel_reminders() IS
    'T7. Cancela los recordatorios pendientes de una cita cancelada o rechazada.';

CREATE TRIGGER tg_appointment_cancel_reminders
    AFTER UPDATE OF status ON scheduling.appointment
    FOR EACH ROW
    WHEN (NEW.status IN ('cancelled', 'rejected') AND OLD.status IS DISTINCT FROM NEW.status)
    EXECUTE FUNCTION scheduling.sp_appointment_cancel_reminders();

-- ---------------------------------------------------------------------------
-- T8. Al desactivar una cuenta se revocan todas sus sesiones activas.
-- ---------------------------------------------------------------------------

CREATE FUNCTION app.sp_user_revoke_sessions()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE app.user_session
    SET revoked_at        = now(),
        revocation_reason = 'account_deactivated',
        updated_at        = now(),
        updated_by        = NEW.updated_by
    WHERE user_id = NEW.id
      AND revoked_at IS NULL;
    RETURN NULL;
END;
$$;

COMMENT ON FUNCTION app.sp_user_revoke_sessions() IS
    'T8. Revoca las sesiones activas de una cuenta desactivada (account_deactivated).';

CREATE TRIGGER tg_user_revoke_sessions
    AFTER UPDATE OF is_active ON app."user"
    FOR EACH ROW
    WHEN (OLD.is_active AND NOT NEW.is_active)
    EXECUTE FUNCTION app.sp_user_revoke_sessions();

-- >>> 06_seed/seed.sql
-- =============================================================================
-- 06_seed/seed.sql
-- Datos semilla. created_by en NULL: los carga el sistema.
-- No incluye usuarios: los administradores de la aplicacion los crea el seeder
-- de la API desde la configuracion, y las cuentas de medico el programa externo.
-- =============================================================================

-- Roles de la aplicacion.
INSERT INTO app.role (name) VALUES
    ('Patient'),
    ('Doctor'),
    ('Administrator');

-- Tipos de token.
INSERT INTO app.token_type (name) VALUES
    ('EmailConfirmation'),
    ('PasswordReset');

-- Plantillas globales de recordatorio (doctor_id NULL). La aplicacion usa
-- 'Recordatorio de cita' para el recordatorio automatico 24 horas antes.
INSERT INTO messaging.reminder_template (doctor_id, name, subject, body_template) VALUES
    (NULL,
     'Recordatorio de cita',
     'Recordatorio: su cita de {SpecialtyName} es manana',
     'Hola {PatientName}, le recordamos su cita de {SpecialtyName} con {DoctorName} el {AppointmentDate} a las {StartTime}. Si no puede asistir, cancele o reprograme la cita desde la aplicacion.'),
    (NULL,
     'Indicaciones previas',
     'Indicaciones para su cita del {AppointmentDate}',
     'Hola {PatientName}, antes de su cita con {DoctorName} el {AppointmentDate} a las {StartTime}, tenga en cuenta las siguientes indicaciones: '),
    (NULL,
     'Seguimiento',
     'Seguimiento de su consulta de {SpecialtyName}',
     'Hola {PatientName}, {DoctorName} le escribe para dar seguimiento a su consulta de {SpecialtyName} del {AppointmentDate}: ');

-- Provincias de Republica Dominicana (32: 31 provincias y el Distrito Nacional).
INSERT INTO people.province (name) VALUES
    ('Azua'),
    ('Bahoruco'),
    ('Barahona'),
    ('Dajabón'),
    ('Distrito Nacional'),
    ('Duarte'),
    ('El Seibo'),
    ('Elías Piña'),
    ('Espaillat'),
    ('Hato Mayor'),
    ('Hermanas Mirabal'),
    ('Independencia'),
    ('La Altagracia'),
    ('La Romana'),
    ('La Vega'),
    ('María Trinidad Sánchez'),
    ('Monseñor Nouel'),
    ('Monte Cristi'),
    ('Monte Plata'),
    ('Pedernales'),
    ('Peravia'),
    ('Puerto Plata'),
    ('Samaná'),
    ('San Cristóbal'),
    ('San José de Ocoa'),
    ('San Juan'),
    ('San Pedro de Macorís'),
    ('Sánchez Ramírez'),
    ('Santiago'),
    ('Santiago Rodríguez'),
    ('Santo Domingo'),
    ('Valverde');

-- Municipios (158), cada uno con su provincia.
INSERT INTO people.municipality (province_id, name)
SELECT p.id, v.municipality
FROM (VALUES
    ('Azua', 'Azua'),
    ('Azua', 'Estebanía'),
    ('Azua', 'Guayabal'),
    ('Azua', 'Las Charcas'),
    ('Azua', 'Las Yayas de Viajama'),
    ('Azua', 'Padre Las Casas'),
    ('Azua', 'Peralta'),
    ('Azua', 'Pueblo Viejo'),
    ('Azua', 'Sabana Yegua'),
    ('Azua', 'Tábara Arriba'),
    ('Bahoruco', 'Galván'),
    ('Bahoruco', 'Los Ríos'),
    ('Bahoruco', 'Neiba'),
    ('Bahoruco', 'Tamayo'),
    ('Bahoruco', 'Villa Jaragua'),
    ('Barahona', 'Barahona'),
    ('Barahona', 'Cabral'),
    ('Barahona', 'El Peñón'),
    ('Barahona', 'Enriquillo'),
    ('Barahona', 'Fundación'),
    ('Barahona', 'Jaquimeyes'),
    ('Barahona', 'La Ciénaga'),
    ('Barahona', 'Las Salinas'),
    ('Barahona', 'Paraíso'),
    ('Barahona', 'Polo'),
    ('Barahona', 'Vicente Noble'),
    ('Dajabón', 'Dajabón'),
    ('Dajabón', 'El Pino'),
    ('Dajabón', 'Loma de Cabrera'),
    ('Dajabón', 'Partido'),
    ('Dajabón', 'Restauración'),
    ('Distrito Nacional', 'Santo Domingo de Guzmán'),
    ('Duarte', 'Arenoso'),
    ('Duarte', 'Castillo'),
    ('Duarte', 'Hostos'),
    ('Duarte', 'Las Guáranas'),
    ('Duarte', 'Pimentel'),
    ('Duarte', 'San Francisco de Macorís'),
    ('Duarte', 'Villa Riva'),
    ('El Seibo', 'El Seibo'),
    ('El Seibo', 'Miches'),
    ('Elías Piña', 'Bánica'),
    ('Elías Piña', 'Comendador'),
    ('Elías Piña', 'El Llano'),
    ('Elías Piña', 'Hondo Valle'),
    ('Elías Piña', 'Juan Santiago'),
    ('Elías Piña', 'Pedro Santana'),
    ('Espaillat', 'Cayetano Germosén'),
    ('Espaillat', 'Gaspar Hernández'),
    ('Espaillat', 'Jamao al Norte'),
    ('Espaillat', 'Moca'),
    ('Espaillat', 'San Víctor'),
    ('Hato Mayor', 'El Valle'),
    ('Hato Mayor', 'Hato Mayor del Rey'),
    ('Hato Mayor', 'Sabana de la Mar'),
    ('Hermanas Mirabal', 'Salcedo'),
    ('Hermanas Mirabal', 'Tenares'),
    ('Hermanas Mirabal', 'Villa Tapia'),
    ('Independencia', 'Cristóbal'),
    ('Independencia', 'Duvergé'),
    ('Independencia', 'Jimaní'),
    ('Independencia', 'La Descubierta'),
    ('Independencia', 'Mella'),
    ('Independencia', 'Postrer Río'),
    ('La Altagracia', 'Higüey'),
    ('La Altagracia', 'San Rafael del Yuma'),
    ('La Romana', 'Guaymate'),
    ('La Romana', 'La Romana'),
    ('La Romana', 'Villa Hermosa'),
    ('La Vega', 'Constanza'),
    ('La Vega', 'Jarabacoa'),
    ('La Vega', 'Jima Abajo'),
    ('La Vega', 'La Vega'),
    ('María Trinidad Sánchez', 'Cabrera'),
    ('María Trinidad Sánchez', 'El Factor'),
    ('María Trinidad Sánchez', 'Nagua'),
    ('María Trinidad Sánchez', 'Río San Juan'),
    ('Monseñor Nouel', 'Bonao'),
    ('Monseñor Nouel', 'Maimón'),
    ('Monseñor Nouel', 'Piedra Blanca'),
    ('Monte Cristi', 'Castañuelas'),
    ('Monte Cristi', 'Guayubín'),
    ('Monte Cristi', 'Las Matas de Santa Cruz'),
    ('Monte Cristi', 'Monte Cristi'),
    ('Monte Cristi', 'Pepillo Salcedo'),
    ('Monte Cristi', 'Villa Vásquez'),
    ('Monte Plata', 'Bayaguana'),
    ('Monte Plata', 'Monte Plata'),
    ('Monte Plata', 'Peralvillo'),
    ('Monte Plata', 'Sabana Grande de Boyá'),
    ('Monte Plata', 'Yamasá'),
    ('Pedernales', 'Oviedo'),
    ('Pedernales', 'Pedernales'),
    ('Peravia', 'Baní'),
    ('Peravia', 'Matanzas'),
    ('Peravia', 'Nizao'),
    ('Puerto Plata', 'Altamira'),
    ('Puerto Plata', 'Guananico'),
    ('Puerto Plata', 'Imbert'),
    ('Puerto Plata', 'Los Hidalgos'),
    ('Puerto Plata', 'Luperón'),
    ('Puerto Plata', 'San Felipe de Puerto Plata'),
    ('Puerto Plata', 'Sosúa'),
    ('Puerto Plata', 'Villa Isabela'),
    ('Puerto Plata', 'Villa Montellano'),
    ('Samaná', 'Las Terrenas'),
    ('Samaná', 'Samaná'),
    ('Samaná', 'Sánchez'),
    ('San Cristóbal', 'Bajos de Haina'),
    ('San Cristóbal', 'Cambita Garabitos'),
    ('San Cristóbal', 'Los Cacaos'),
    ('San Cristóbal', 'Sabana Grande de Palenque'),
    ('San Cristóbal', 'San Cristóbal'),
    ('San Cristóbal', 'San Gregorio de Nigua'),
    ('San Cristóbal', 'Villa Altagracia'),
    ('San Cristóbal', 'Yaguate'),
    ('San José de Ocoa', 'Rancho Arriba'),
    ('San José de Ocoa', 'Sabana Larga'),
    ('San José de Ocoa', 'San José de Ocoa'),
    ('San Juan', 'Bohechío'),
    ('San Juan', 'El Cercado'),
    ('San Juan', 'Juan de Herrera'),
    ('San Juan', 'Las Matas de Farfán'),
    ('San Juan', 'San Juan de la Maguana'),
    ('San Juan', 'Vallejuelo'),
    ('San Pedro de Macorís', 'Consuelo'),
    ('San Pedro de Macorís', 'Guayacanes'),
    ('San Pedro de Macorís', 'Quisqueya'),
    ('San Pedro de Macorís', 'Ramón Santana'),
    ('San Pedro de Macorís', 'San José de los Llanos'),
    ('San Pedro de Macorís', 'San Pedro de Macorís'),
    ('Sánchez Ramírez', 'Cevicos'),
    ('Sánchez Ramírez', 'Cotuí'),
    ('Sánchez Ramírez', 'Fantino'),
    ('Sánchez Ramírez', 'La Mata'),
    ('Santiago', 'Baitoa'),
    ('Santiago', 'Bisonó'),
    ('Santiago', 'Jánico'),
    ('Santiago', 'Licey al Medio'),
    ('Santiago', 'Puñal'),
    ('Santiago', 'Sabana Iglesia'),
    ('Santiago', 'San José de las Matas'),
    ('Santiago', 'Santiago de los Caballeros'),
    ('Santiago', 'Tamboril'),
    ('Santiago', 'Villa González'),
    ('Santiago Rodríguez', 'Monción'),
    ('Santiago Rodríguez', 'Sabaneta'),
    ('Santiago Rodríguez', 'Villa Los Almácigos'),
    ('Santo Domingo', 'Boca Chica'),
    ('Santo Domingo', 'Los Alcarrizos'),
    ('Santo Domingo', 'Pedro Brand'),
    ('Santo Domingo', 'San Antonio de Guerra'),
    ('Santo Domingo', 'Santo Domingo Este'),
    ('Santo Domingo', 'Santo Domingo Norte'),
    ('Santo Domingo', 'Santo Domingo Oeste'),
    ('Valverde', 'Esperanza'),
    ('Valverde', 'Laguna Salada'),
    ('Valverde', 'Mao')
) AS v(province, municipality)
JOIN people.province p ON p.name = v.province;

-- >>> 00_setup/05_permissions.sql
-- =============================================================================
-- 00_setup/05_permissions.sql
-- Permisos por esquema. Se ejecuta al final, como sgcm_owner, cuando ya existen
-- todos los objetos.
-- =============================================================================

-- Nadie fuera de los roles del sistema entra a los esquemas. Incluye los roles
-- anon y authenticated de Supabase, que no reciben ningun permiso.
REVOKE ALL ON SCHEMA app, people, scheduling, clinical, messaging FROM PUBLIC;
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA app, people, scheduling, clinical, messaging FROM PUBLIC;

-- ---------------------------------------------------------------------------
-- sgcm_app: API y Worker
-- ---------------------------------------------------------------------------
GRANT USAGE ON SCHEMA app, people, scheduling, clinical, messaging TO sgcm_app;

GRANT SELECT, INSERT, UPDATE, DELETE
    ON ALL TABLES IN SCHEMA app, people, scheduling, clinical, messaging
    TO sgcm_app;

-- Tablas de solo insercion: el motor impide modificarlas o borrarlas.
REVOKE UPDATE, DELETE ON
    clinical.medical_record,
    clinical.medical_record_access_log,
    scheduling.appointment_report_log
FROM sgcm_app;

GRANT EXECUTE ON FUNCTION
    people.sp_text_normalize(text),
    scheduling.sp_availability_get_free_slots(timestamptz, timestamptz, interval, integer, integer, integer, integer, integer),
    scheduling.sp_doctor_pick_random(integer, timestamptz, timestamptz, integer, integer, integer)
TO sgcm_app;

-- ---------------------------------------------------------------------------
-- sgcm_reporting: solo lectura, sin acceso a app ni a messaging
-- ---------------------------------------------------------------------------
GRANT USAGE ON SCHEMA people, scheduling, clinical TO sgcm_reporting;
GRANT SELECT ON ALL TABLES IN SCHEMA people, scheduling, clinical TO sgcm_reporting;
GRANT EXECUTE ON FUNCTION people.sp_text_normalize(text) TO sgcm_reporting;

-- ---------------------------------------------------------------------------
-- Objetos que se agreguen despues heredan los mismos permisos.
-- ---------------------------------------------------------------------------
ALTER DEFAULT PRIVILEGES IN SCHEMA app, people, scheduling, clinical, messaging
    REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;
ALTER DEFAULT PRIVILEGES IN SCHEMA app, people, scheduling, clinical, messaging
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO sgcm_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA people, scheduling, clinical
    GRANT SELECT ON TABLES TO sgcm_reporting;


RESET ROLE;

COMMIT;

\echo 'Base de datos SGCM creada correctamente.'
