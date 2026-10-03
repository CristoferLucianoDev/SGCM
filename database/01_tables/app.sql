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
    last_used_at                 timestamptz   NULL,
    revoked_at                   timestamptz   NULL,
    revocation_reason            varchar(50)   NULL,
    created_at                   timestamptz   NOT NULL DEFAULT now(),
    created_by                   integer       NULL,
    updated_at                   timestamptz   NULL,
    updated_by                   integer       NULL,
    CONSTRAINT pk_user_session PRIMARY KEY (id)
);
COMMENT ON TABLE app.user_session IS 'Sesion por dispositivo. Guarda el hash del refresh token; el JWT no se almacena.';
COMMENT ON COLUMN app.user_session.previous_refresh_token_hash IS 'Hash anterior a la ultima rotacion; si se presenta, se asume robo.';
