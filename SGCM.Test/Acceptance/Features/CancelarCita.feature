# language: es
@Integration @Appointments
Característica: Cancelar cita
  Como paciente, quiero cancelar una cita, para liberar el horario cuando no puedo asistir.

  Escenario: Cancelar una cita pendiente con motivo la cancela, libera el horario y avisa al médico con el motivo
    Dado que inicié sesión como paciente para cancelar una cita
    Y que tengo una cita "pendiente" con un médico dentro de 10 días a las "10:00"
    Cuando cancelo mi cita indicando el motivo "Viaje"
    Entonces la cita queda cancelada con el motivo "Viaje"
    Y el médico ya no tiene citas activas en ese horario
    Y el médico recibe el aviso de la cancelación con el motivo "Viaje" y el paciente no recibe correo

  Escenario: Cancelar una cita confirmada sin motivo también la cancela
    Dado que inicié sesión como paciente para cancelar una cita
    Y que tengo una cita "confirmada" con un médico dentro de 10 días a las "10:00"
    Cuando cancelo mi cita sin indicar un motivo
    Entonces la cita queda cancelada sin motivo
    Y el médico recibe el aviso de la cancelación sin motivo

  Escenario: Con menos de 24 horas la cancelación se rechaza indicando que debo llamar al médico
    Dado que inicié sesión como paciente para cancelar una cita
    Y que tengo una cita "pendiente" con un médico que empieza dentro de 23 horas
    Cuando cancelo mi cita indicando el motivo "Viaje"
    Entonces la cancelación se rechaza con el código "Appointment.CancellationDeadlinePassed"
    Y el mensaje de la cancelación indica que debo llamar al médico
    Y mi cita sigue "pendiente"

  Esquema del escenario: Una cita <estado> no puede cancelarse
    Dado que inicié sesión como paciente para cancelar una cita
    Y que tengo una cita "<estado>" con un médico dentro de 10 días a las "10:00"
    Cuando cancelo mi cita indicando el motivo "Viaje"
    Entonces la cancelación se rechaza con el código "Appointment.InvalidTransition"
    Y mi cita sigue "<estado>"

    Ejemplos:
      | estado     |
      | rechazada  |
      | cancelada  |
      | completada |

  Escenario: No puedo cancelar la cita de otro paciente
    Dado que inicié sesión como paciente para cancelar una cita
    Y que otro paciente tiene una cita "pendiente" con un médico dentro de 10 días a las "10:00"
    Cuando cancelo esa cita ajena indicando el motivo "Viaje"
    Entonces la cancelación se rechaza con el código "Appointment.NotFound"
    Y la cita ajena sigue "pendiente"
