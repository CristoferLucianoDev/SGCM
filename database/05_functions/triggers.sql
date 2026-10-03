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
