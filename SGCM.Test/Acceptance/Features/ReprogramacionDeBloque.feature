# language: es
@Integration @Availability
Característica: Reprogramar bloque de disponibilidad
  Como médico, quiero cambiar el horario de un bloque, para ajustar mi agenda; los bloques de la semana actual solo se
  pueden cerrar, los de la semana siguiente se reprograman de lunes a miércoles y las citas que quedan fuera del horario
  nuevo se cancelan con mi motivo.

  Escenario: Un martes se reprograma un bloque de la semana siguiente
    Dado que inicié sesión como médico para reprogramar un bloque
    Y que hoy es martes
    Y que tengo un bloque el miércoles de la semana siguiente de "08:00" a "14:00"
    Cuando reprogramo ese bloque de "10:00" a "16:00" sin motivo
    Entonces el bloque queda de "10:00" a "16:00" y se cancelaron 0 citas

  Escenario: Un jueves un bloque de la semana siguiente ya no se reprograma
    Dado que inicié sesión como médico para reprogramar un bloque
    Y que hoy es jueves
    Y que tengo un bloque el miércoles de la semana siguiente de "08:00" a "14:00"
    Cuando reprogramo ese bloque de "10:00" a "16:00" sin motivo
    Entonces la reprogramación se rechaza con el código "Scheduling.NextWeekLocked"
    Y el bloque sigue de "08:00" a "14:00"

  Escenario: Un bloque de la semana actual solo se puede cerrar
    Dado que inicié sesión como médico para reprogramar un bloque
    Y que hoy es martes
    Y que tengo un bloque el viernes de esta semana de "08:00" a "14:00"
    Cuando reprogramo ese bloque de "10:00" a "16:00" sin motivo
    Entonces la reprogramación se rechaza con el código "Scheduling.CurrentWeekLocked"
    Y el mensaje indica que el bloque solo se puede cerrar

  Escenario: Las citas que quedan fuera del horario nuevo se cancelan con el motivo y se avisa a esos pacientes
    Dado que inicié sesión como médico para reprogramar un bloque
    Y que hoy es jueves
    Y que tengo un bloque el miércoles de dentro de dos semanas de "08:00" a "14:00"
    Y que un paciente tiene cita en ese bloque a las "08:00" y otro a las "12:00"
    Cuando reprogramo ese bloque de "10:00" a "16:00" con el motivo "Cambio de consultorio"
    Entonces el bloque queda de "10:00" a "16:00" y se cancelaron 1 citas
    Y solo el paciente de las "08:00" tiene la cita cancelada y recibe un correo con el motivo "Cambio de consultorio"

  Escenario: Si quedan citas fuera del horario nuevo y no hay motivo se rechaza
    Dado que inicié sesión como médico para reprogramar un bloque
    Y que hoy es martes
    Y que tengo un bloque el miércoles de dentro de dos semanas de "08:00" a "14:00"
    Y que un paciente tiene cita en ese bloque a las "08:00" y otro a las "12:00"
    Cuando reprogramo ese bloque de "10:00" a "16:00" sin motivo
    Entonces la reprogramación se rechaza con el código "Appointment.StatusReasonRequired"
    Y el bloque sigue de "08:00" a "14:00"
