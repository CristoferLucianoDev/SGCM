# language: es
@Integration @Appointments
Característica: Confirmar o rechazar cita
  Como médico, quiero confirmar o rechazar las solicitudes de cita, para decidir qué atiendo.

  Escenario: Confirmo una solicitud pendiente y el paciente recibe el correo con los datos de la cita
    Dado que inicié sesión como médico para decidir solicitudes de cita
    Y que en mi agenda hay una cita "pendiente" de mi paciente para dentro de 10 días a las "10:00"
    Cuando decido confirmar la solicitud
    Entonces la solicitud de mi agenda queda "confirmada"
    Y el paciente recibe un correo de confirmación con los datos de la cita a las "10:00" y yo no recibo correo

  Escenario: Rechazo una solicitud con motivo, el horario queda libre y el paciente recibe el correo con el motivo
    Dado que inicié sesión como médico para decidir solicitudes de cita
    Y que en mi agenda hay una cita "pendiente" de mi paciente para dentro de 10 días a las "10:00"
    Cuando decido rechazar la solicitud indicando el motivo "Agenda completa"
    Entonces la solicitud de mi agenda queda "rechazada"
    Y el horario de esa solicitud queda libre
    Y el paciente recibe un correo de rechazo con el motivo "Agenda completa" y yo no recibo correo

  Escenario: Rechazar sin motivo se rechaza
    Dado que inicié sesión como médico para decidir solicitudes de cita
    Y que en mi agenda hay una cita "pendiente" de mi paciente para dentro de 10 días a las "10:00"
    Cuando decido rechazar la solicitud sin indicar un motivo
    Entonces mi decisión sobre la solicitud se rechaza con el código "Appointment.StatusReasonRequired"
    Y la solicitud de mi agenda sigue "pendiente"
    Y no se envía ningún correo por mi decisión

  Escenario: Con menos de 48 horas para el inicio no puedo confirmar
    Dado que inicié sesión como médico para decidir solicitudes de cita
    Y que en mi agenda hay una cita "pendiente" de mi paciente que empieza dentro de 47 horas
    Cuando decido confirmar la solicitud
    Entonces mi decisión sobre la solicitud se rechaza con el código "Appointment.ConfirmationDeadlinePassed"
    Y la solicitud de mi agenda sigue "pendiente"

  Esquema del escenario: No puedo confirmar una cita <estado>
    Dado que inicié sesión como médico para decidir solicitudes de cita
    Y que en mi agenda hay una cita "<estado>" de mi paciente para dentro de 10 días a las "10:00"
    Cuando decido confirmar la solicitud
    Entonces mi decisión sobre la solicitud se rechaza con el código "Appointment.NotPending"
    Y la solicitud de mi agenda sigue "<estado>"

    Ejemplos:
      | estado     |
      | confirmada |
      | rechazada  |
      | cancelada  |
      | completada |

  Escenario: No puedo confirmar la cita de otro médico
    Dado que inicié sesión como médico para decidir solicitudes de cita
    Y que otro médico tiene en su agenda una cita "pendiente" para dentro de 10 días a las "10:00"
    Cuando decido confirmar esa cita ajena
    Entonces mi decisión sobre la solicitud se rechaza con el código "Appointment.NotFound"
    Y la cita de ese médico sigue "pendiente"
