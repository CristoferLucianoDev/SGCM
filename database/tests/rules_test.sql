-- =============================================================================
-- tests/rules_test.sql
-- Pruebas de las reglas de la base de datos (restricciones, triggers, vistas,
-- funciones y permisos). Se ejecuta como sgcm_app sobre una base recien creada;
-- todo ocurre dentro de una transaccion que se revierte al final.
--   psql "<conexion como sgcm_app>" -f tests/rules_test.sql
-- Cada regla imprime OK; la primera que falle detiene el script con FAIL.
-- =============================================================================
\set ON_ERROR_STOP on
\encoding UTF8
SET client_min_messages = warning;
BEGIN;

CREATE FUNCTION pg_temp.expect(p_sql text, p_expected text) RETURNS void LANGUAGE plpgsql AS $$
DECLARE v_c text; v_s text;
BEGIN
    BEGIN
        EXECUTE p_sql;
    EXCEPTION WHEN OTHERS THEN
        GET STACKED DIAGNOSTICS v_c = CONSTRAINT_NAME, v_s = RETURNED_SQLSTATE;
        IF coalesce(v_c,'') = p_expected OR v_s = p_expected THEN
            RAISE NOTICE 'OK   %  (%)', p_expected, v_s; RETURN;
        END IF;
        RAISE EXCEPTION 'FAIL esperado %, obtenido % / % : %', p_expected, v_s, v_c, SQLERRM;
    END;
    RAISE EXCEPTION 'FAIL esperado %, pero la sentencia funciono: %', p_expected, p_sql;
END $$;
CREATE FUNCTION pg_temp.ok(p_cond boolean, p_name text) RETURNS void LANGUAGE plpgsql AS $$
BEGIN IF p_cond THEN RAISE NOTICE 'OK   %', p_name; ELSE RAISE EXCEPTION 'FAIL %', p_name; END IF; END $$;
SET client_min_messages = notice;

-- Datos base
INSERT INTO app."user"(email, password_hash, role_id, is_email_confirmed)
SELECT 'pac@test.com','h',id,true FROM app.role WHERE name='Patient';
INSERT INTO app."user"(email, password_hash, role_id, is_email_confirmed)
SELECT 'doc@test.com','h',id,true FROM app.role WHERE name='Doctor';
INSERT INTO app."user"(email, password_hash, role_id, is_email_confirmed)
SELECT 'doc2@test.com','h',id,true FROM app.role WHERE name='Doctor';
INSERT INTO people.patient(user_id, first_name, last_name, birth_date, phone_number)
SELECT id,'Ana','Perez','1990-01-01','8095550000' FROM app."user" WHERE email='pac@test.com';
INSERT INTO people.doctor(user_id, first_name, last_name, license_number, phone_number, province_id, municipality_id, address_line, consultation_fee, years_of_experience, biography)
SELECT u.id,'Luis','Gomez','LIC-1','8095551111',m.province_id,m.id,'Calle 1',1500,10,'Bio'
FROM app."user" u, people.municipality m WHERE u.email='doc@test.com' AND m.name='Santiago de los Caballeros';
INSERT INTO people.doctor(user_id, first_name, last_name, license_number, phone_number, province_id, municipality_id, address_line, consultation_fee, years_of_experience, biography)
SELECT u.id,'Maria','Diaz','LIC-2','8095552222',m.province_id,m.id,'Calle 2',2000,5,'Bio'
FROM app."user" u, people.municipality m WHERE u.email='doc2@test.com' AND m.name='Santo Domingo Este';
INSERT INTO people.specialty(name, description) VALUES ('Cardiología','Corazon'),('Pediatría','Ninos');
INSERT INTO people.doctor_specialty(doctor_id, specialty_id)
SELECT d.id, s.id FROM people.doctor d, people.specialty s WHERE d.license_number IN ('LIC-1','LIC-2') AND s.name='Cardiología';

CREATE TEMP TABLE ctx AS
SELECT (SELECT id FROM people.patient LIMIT 1) pat,
       (SELECT id FROM people.doctor WHERE license_number='LIC-1') doc,
       (SELECT id FROM people.doctor WHERE license_number='LIC-2') doc2,
       (SELECT id FROM people.specialty WHERE name='Cardiología') card,
       (SELECT id FROM people.specialty WHERE name='Pediatría') ped,
       (date_trunc('day', now()) + interval '5 days') d5;

-- Bloques de 6 horas (08:00-14:00 UTC del dia +5) para ambos medicos, y uno pasado
INSERT INTO scheduling.doctor_availability(doctor_id, starts_at, ends_at)
SELECT doc,  d5 + interval '8 hours', d5 + interval '14 hours' FROM ctx UNION ALL
SELECT doc2, d5 + interval '8 hours', d5 + interval '14 hours' FROM ctx UNION ALL
SELECT doc, date_trunc('hour', now()) - interval '3 hours', date_trunc('hour', now()) + interval '3 hours' FROM ctx;

-- ---------------- Restricciones ----------------
SELECT pg_temp.expect($$INSERT INTO app."user"(email,password_hash,role_id) VALUES ('X@test.com','h',1)$$, 'ck_user_email_lowercase');
SELECT pg_temp.expect($$INSERT INTO people.specialty(name, description) VALUES (' cardiologia ','dup')$$, 'uq_specialty_name');
SELECT pg_temp.expect($$INSERT INTO scheduling.doctor_availability(doctor_id, starts_at, ends_at) SELECT doc, d5 + interval '10 hours', d5 + interval '16 hours' FROM ctx$$, 'ex_doctor_availability_no_overlap');
SELECT pg_temp.expect($$INSERT INTO scheduling.doctor_availability(doctor_id, starts_at, ends_at) SELECT doc, d5 + interval '20 hours 30 minutes', d5 + interval '23 hours' FROM ctx$$, 'ck_doctor_availability_on_hour');
SELECT pg_temp.expect($$UPDATE people.doctor SET municipality_id = (SELECT id FROM people.municipality WHERE name='Higüey') WHERE license_number='LIC-1'$$, 'fk_doctor_municipality');
SELECT pg_temp.expect($$INSERT INTO messaging.reminder_template(name, subject, body_template) VALUES ('t','Hola {PatientName}','Cita el {Fecha}')$$, 'ck_reminder_template_variables');
SELECT pg_temp.ok((SELECT count(*) FROM people.province)=32 AND (SELECT count(*) FROM people.municipality)=158, 'semilla 32 provincias y 158 municipios');

-- ---------------- T6 rol ----------------
SELECT pg_temp.expect($$INSERT INTO people.patient(user_id, first_name, last_name, birth_date, phone_number) SELECT id,'X','Y','2000-01-01','1' FROM app."user" WHERE email='doc2@test.com'$$, 'tg_patient_validate_role');
SELECT pg_temp.expect($$UPDATE app."user" SET role_id = (SELECT id FROM app.role WHERE name='Doctor') WHERE email='pac@test.com'$$, 'tg_user_validate_role_change');

-- ---------------- Citas: T2, T3, EXCLUDE ----------------
INSERT INTO scheduling.appointment(patient_id, doctor_id, specialty_id, starts_at, ends_at)
SELECT pat, doc, card, d5 + interval '8 hours', d5 + interval '10 hours' FROM ctx;
SELECT pg_temp.ok(true, 'cita valida dentro del bloque');
SELECT pg_temp.expect($$INSERT INTO scheduling.appointment(patient_id, doctor_id, specialty_id, starts_at, ends_at, status) SELECT pat, doc, card, d5 + interval '10 hours', d5 + interval '12 hours','completed' FROM ctx$$, 'tg_appointment_initial_status');
SELECT pg_temp.expect($$INSERT INTO scheduling.appointment(patient_id, doctor_id, specialty_id, starts_at, ends_at) SELECT pat, doc, card, d5 + interval '13 hours', d5 + interval '15 hours' FROM ctx$$, 'tg_appointment_within_block');
SELECT pg_temp.expect($$INSERT INTO scheduling.appointment(patient_id, doctor_id, specialty_id, starts_at, ends_at) SELECT pat, doc, ped, d5 + interval '10 hours', d5 + interval '12 hours' FROM ctx$$, 'tg_appointment_doctor_specialty');
SELECT pg_temp.expect($$INSERT INTO scheduling.appointment(patient_id, doctor_id, specialty_id, starts_at, ends_at) SELECT pat, doc2, card, d5 + interval '9 hours', d5 + interval '11 hours' FROM ctx$$, 'ex_appointment_patient_no_overlap');
SELECT pg_temp.expect($$INSERT INTO scheduling.appointment(patient_id, doctor_id, specialty_id, starts_at, ends_at, status) SELECT pat, doc, card, d5 + interval '9 hours', d5 + interval '11 hours', 'confirmed' FROM ctx$$, 'ex_appointment_doctor_no_overlap');
SELECT pg_temp.expect($$INSERT INTO scheduling.appointment(patient_id, doctor_id, specialty_id, starts_at, ends_at) SELECT pat, doc, card, d5 + interval '10 hours 30 minutes', d5 + interval '12 hours' FROM ctx$$, 'ck_appointment_on_hour');
SELECT pg_temp.expect($$UPDATE scheduling.appointment SET status='rejected' WHERE starts_at = (SELECT d5 + interval '8 hours' FROM ctx)$$, 'ck_appointment_rejected_reason');
SELECT pg_temp.expect($$UPDATE scheduling.appointment SET status='completed' WHERE starts_at = (SELECT d5 + interval '8 hours' FROM ctx)$$, 'tg_appointment_status_transition');

-- Confirmar, programar recordatorio, cancelar => T7
UPDATE scheduling.appointment SET status='confirmed' WHERE starts_at = (SELECT d5 + interval '8 hours' FROM ctx);
SELECT pg_temp.ok((SELECT updated_at IS NOT NULL FROM scheduling.appointment WHERE starts_at = (SELECT d5 + interval '8 hours' FROM ctx)), 'T1 updated_at automatico');
INSERT INTO messaging.reminder(appointment_id, template_id, message_body, scheduled_at)
SELECT a.id, t.id, 'Hola', a.starts_at - interval '24 hours' FROM scheduling.appointment a, messaging.reminder_template t
WHERE t.name='Recordatorio de cita' AND a.starts_at = (SELECT d5 + interval '8 hours' FROM ctx);

-- T4: no cerrar el bloque con la cita activa
SELECT pg_temp.expect($$UPDATE scheduling.doctor_availability SET is_available=false WHERE doctor_id=(SELECT doc FROM ctx) AND starts_at=(SELECT d5 + interval '8 hours' FROM ctx)$$, 'tg_doctor_availability_active_appointments');
SELECT pg_temp.expect($$UPDATE scheduling.doctor_availability SET starts_at = starts_at + interval '1 hour' WHERE doctor_id=(SELECT doc FROM ctx) AND starts_at=(SELECT d5 + interval '8 hours' FROM ctx)$$, 'tg_doctor_availability_active_appointments');
-- Extender el bloque si se permite
UPDATE scheduling.doctor_availability SET ends_at = ends_at + interval '1 hour' WHERE doctor_id=(SELECT doc FROM ctx) AND starts_at=(SELECT d5 + interval '8 hours' FROM ctx);
SELECT pg_temp.ok(true, 'T4 permite ampliar el bloque');

UPDATE scheduling.appointment SET status='cancelled', status_reason='Cierre de bloque' WHERE starts_at = (SELECT d5 + interval '8 hours' FROM ctx);
SELECT pg_temp.ok((SELECT delivery_status FROM messaging.reminder LIMIT 1)='cancelled', 'T7 recordatorio cancelado');
SELECT pg_temp.expect($$UPDATE scheduling.appointment SET status_reason='otro' WHERE starts_at = (SELECT d5 + interval '8 hours' FROM ctx)$$, 'tg_appointment_final_status');
UPDATE scheduling.doctor_availability SET is_available=false WHERE doctor_id=(SELECT doc FROM ctx) AND starts_at=(SELECT d5 + interval '8 hours' FROM ctx);
SELECT pg_temp.ok(true, 'T4 permite cerrar el bloque sin citas activas');

-- ---------------- Funciones de lectura ----------------
SELECT pg_temp.ok((SELECT count(*) FROM scheduling.sp_availability_get_free_slots((SELECT d5 FROM ctx), (SELECT d5 + interval '1 day' FROM ctx), interval '2 hours', NULL, (SELECT card FROM ctx)))=5,
                  'free slots: solo doc2 (bloque de doc cerrado), 08..12 = 5 inicios');
SELECT pg_temp.ok((SELECT scheduling.sp_doctor_pick_random((SELECT card FROM ctx), (SELECT d5 + interval '9 hours' FROM ctx), (SELECT d5 + interval '11 hours' FROM ctx))) = (SELECT doc2 FROM ctx), 'pick_random devuelve doc2');
SELECT pg_temp.ok((SELECT scheduling.sp_doctor_pick_random((SELECT card FROM ctx), (SELECT d5 + interval '9 hours' FROM ctx), (SELECT d5 + interval '11 hours' FROM ctx), (SELECT province_id FROM people.doctor WHERE license_number='LIC-1'))) IS NULL, 'pick_random con filtro de provincia sin medicos libres = NULL');
SELECT pg_temp.ok((SELECT cardinality(specialty_ids) FROM people.vw_doctor_directory WHERE id=(SELECT doc FROM ctx))=1, 'vw_doctor_directory');

-- ---------------- T5 y solo insercion ----------------
INSERT INTO scheduling.appointment(patient_id, doctor_id, specialty_id, starts_at, ends_at, status)
SELECT pat, doc, card, date_trunc('hour', now()) - interval '1 hour', date_trunc('hour', now()) + interval '1 hour', 'confirmed' FROM ctx;
SELECT pg_temp.expect($$INSERT INTO clinical.medical_record(appointment_id, patient_id, diagnosis) SELECT a.id, a.patient_id, 'x' FROM scheduling.appointment a WHERE a.status='cancelled'$$, 'tg_medical_record_appointment_started');
INSERT INTO clinical.medical_record(appointment_id, patient_id, diagnosis)
SELECT a.id, a.patient_id, 'v1' FROM scheduling.appointment a WHERE a.status='confirmed';
INSERT INTO clinical.medical_record(appointment_id, patient_id, diagnosis, created_at)
SELECT a.id, a.patient_id, 'v2', now() + interval '1 second' FROM scheduling.appointment a WHERE a.status='confirmed';
SELECT pg_temp.ok((SELECT diagnosis='v2' AND version_count=2 FROM clinical.vw_medical_record_current), 'vw_medical_record_current version vigente');
SELECT pg_temp.expect($$UPDATE clinical.medical_record SET diagnosis='x'$$, '42501');
SELECT pg_temp.expect($$DELETE FROM clinical.medical_record$$, '42501');

-- ---------------- T8 sesiones ----------------
INSERT INTO app.user_session(user_id, refresh_token_hash, expires_at, last_used_at)
SELECT id, 'rt1', now() + interval '7 days', now() FROM app."user" WHERE email='pac@test.com';
SELECT pg_temp.expect($$INSERT INTO app.user_session(user_id, refresh_token_hash, expires_at, last_used_at, revoked_at) SELECT id, 'rt2', now(), now(), now() FROM app."user" WHERE email='pac@test.com'$$, 'ck_user_session_revocation_consistency');
UPDATE app."user" SET is_active=false WHERE email='pac@test.com';
SELECT pg_temp.ok((SELECT revocation_reason FROM app.user_session WHERE refresh_token_hash='rt1')='account_deactivated', 'T8 sesiones revocadas');

-- Token: uno vigente por usuario y tipo
INSERT INTO app.user_token(user_id, token_type_id, token_hash, expires_at) SELECT u.id, t.id, 'a', now() FROM app."user" u, app.token_type t WHERE u.email='doc@test.com' AND t.name='PasswordReset';
SELECT pg_temp.expect($$INSERT INTO app.user_token(user_id, token_type_id, token_hash, expires_at) SELECT u.id, t.id, 'b', now() FROM app."user" u, app.token_type t WHERE u.email='doc@test.com' AND t.name='PasswordReset'$$, 'uq_user_token_user_id_token_type_id_active');

ROLLBACK;
\echo 'PRUEBAS TERMINADAS'
