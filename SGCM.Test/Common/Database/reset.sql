-- Limpia los datos que crean las pruebas y conserva los datos semilla.
-- Se usa DELETE y no TRUNCATE ... CASCADE: las tablas semilla tienen created_by
-- hacia app."user", y TRUNCATE CASCADE las vaciaría también.
-- Borrar un usuario arrastra sus tokens, sesiones y 2FA (ON DELETE CASCADE).
-- Cada tarea que agregue tablas con datos de prueba agrega aquí su DELETE,
-- en orden de hijas a padres.
-- Los perfiles no tienen cascada desde el usuario (a propósito, para no perder historial clínico), así que se borran antes.
DELETE FROM people.patient;
DELETE FROM people.doctor;
DELETE FROM people.administrator;
DELETE FROM app."user";