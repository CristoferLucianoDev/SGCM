# language: es
@Integration @MedicalRecords
Característica: Registrar información médica
  Como médico, quiero registrar el diagnóstico, el tratamiento y las notas de las citas que atiendo, para que quede
  la historia clínica de mis pacientes, con cada corrección conservada y cada registro en el registro de accesos.

  Escenario: Registro la información clínica de una cita en curso
    Dado que inicié sesión como médico para registrar información clínica
    Y que tengo una cita "confirmada" que empezó hace 1 hora
    Cuando registro el diagnóstico "Faringitis aguda" y el tratamiento "Reposo e hidratación"
    Entonces el registro clínico se crea como primera versión
    Y la cita tiene 1 versión de registro clínico
    Y la versión vigente tiene el diagnóstico "Faringitis aguda"
    Y el registro de accesos tiene una "creación" a mi nombre

  Escenario: Corrijo el registro clínico y la versión anterior se conserva
    Dado que inicié sesión como médico para registrar información clínica
    Y que tengo una cita "completada" que empezó hace 3 horas
    Y que ya registré el diagnóstico "Faringitis aguda" en esa cita
    Cuando registro el diagnóstico "Amigdalitis bacteriana" y el tratamiento "Antibiótico por 7 días"
    Entonces el registro clínico se crea como corrección
    Y la cita tiene 2 versiones de registro clínico
    Y la versión vigente tiene el diagnóstico "Amigdalitis bacteriana"
    Y la primera versión conserva el diagnóstico "Faringitis aguda"
    Y el registro de accesos tiene una "corrección" a mi nombre

  Escenario: Una cita que aún no empieza no admite registro clínico
    Dado que inicié sesión como médico para registrar información clínica
    Y que tengo una cita "confirmada" que empieza dentro de 2 horas
    Cuando registro el diagnóstico "Faringitis aguda" y el tratamiento "Reposo e hidratación"
    Entonces el registro clínico se rechaza con el estado 409 y el código "Appointment.ClinicalRecordNotStarted"
    Y la cita no tiene registro clínico ni accesos registrados

  Esquema del escenario: Una cita <estado> no admite registro clínico
    Dado que inicié sesión como médico para registrar información clínica
    Y que tengo una cita "<estado>" que empezó hace 1 hora
    Cuando registro el diagnóstico "Faringitis aguda" y el tratamiento "Reposo e hidratación"
    Entonces el registro clínico se rechaza con el estado 409 y el código "Appointment.ClinicalRecordInvalidStatus"
    Y la cita no tiene registro clínico ni accesos registrados

    Ejemplos:
      | estado    |
      | pendiente |
      | cancelada |
      | rechazada |

  Escenario: No puedo registrar información clínica en la cita de otro médico
    Dado que inicié sesión como médico para registrar información clínica
    Y que otro médico tiene una cita "confirmada" que empezó hace 1 hora
    Cuando registro el diagnóstico "Faringitis aguda" y el tratamiento "Reposo e hidratación"
    Entonces el registro clínico se rechaza con el estado 404 y el código "Appointment.NotFound"
    Y la cita no tiene registro clínico ni accesos registrados
