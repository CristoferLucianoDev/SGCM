# language: es
@Integration @Appointments
Característica: Reprogramar cita
  Como paciente, quiero mover mi cita a otro horario del mismo médico, para adaptarla a mi disponibilidad.

  Escenario: Reprogramo una cita confirmada a otro horario libre y mi médico recibe el aviso con la nueva fecha
    Dado que inicié sesión como paciente para reprogramar mis citas
    Y que tengo una cita "confirmada" para reprogramar con mi médico dentro de 10 días a las "10:00"
    Y que el médico de esa cita tiene un horario libre dentro de 12 días a las "10:00"
    Cuando reprogramo esa cita para dentro de 12 días a las "10:00"
    Entonces la reprogramación se acepta
    Y la cita conserva su identidad y queda en el horario de dentro de 12 días a las "10:00"
    Y la cita reprogramada vuelve a estar "pendiente"
    Y el médico recibe un solo aviso de reprogramación con el horario de dentro de 12 días a las "10:00"

  Escenario: Con menos de 48 horas para el inicio la reprogramación se rechaza
    Dado que inicié sesión como paciente para reprogramar mis citas
    Y que tengo una cita "confirmada" para reprogramar con mi médico que empieza dentro de 47 horas
    Y que el médico de esa cita tiene un horario libre dentro de 12 días a las "10:00"
    Cuando reprogramo esa cita para dentro de 12 días a las "10:00"
    Entonces la reprogramación de la cita se rechaza con el código "Appointment.RescheduleDeadlinePassed"
    Y la cita reprogramable sigue en su horario original

  Escenario: Un nuevo horario a menos de 4 días se rechaza
    Dado que inicié sesión como paciente para reprogramar mis citas
    Y que tengo una cita "confirmada" para reprogramar con mi médico dentro de 10 días a las "10:00"
    Y que el médico de esa cita tiene un horario libre dentro de 3 días a las "10:00"
    Cuando reprogramo esa cita para dentro de 3 días a las "10:00"
    Entonces la reprogramación de la cita se rechaza con el código "Appointment.TooSoon"
    Y la cita reprogramable sigue en su horario original

  Escenario: Un nuevo horario ocupado se rechaza
    Dado que inicié sesión como paciente para reprogramar mis citas
    Y que tengo una cita "confirmada" para reprogramar con mi médico dentro de 10 días a las "10:00"
    Y que otro paciente ya ocupa el horario del médico de esa cita dentro de 12 días a las "10:00"
    Cuando reprogramo esa cita para dentro de 12 días a las "10:00"
    Entonces la reprogramación de la cita se rechaza con el código "Appointment.SlotUnavailable"
    Y la cita reprogramable sigue en su horario original
