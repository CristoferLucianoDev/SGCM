# language: es
@Integration @Availability
Característica: Visualizar la agenda del médico
  Como médico, quiero ver mis citas en una lista paginada y en el calendario, filtrarlas y abrir su detalle, para
  organizar mi día; solo veo mis propias citas.

  Escenario: Veo solo mis citas activas con paciente, especialidad, fecha, hora y estado
    Dado que inicié sesión como médico para ver mi agenda
    Y que tengo una cita confirmada de "María Gómez" dentro de 10 días a las "09:00"
    Y que tengo una cita cancelada de "Luis Peña" dentro de 11 días a las "09:00"
    Y que otro médico tiene una cita dentro de 10 días a las "09:00"
    Cuando consulto mi agenda
    Entonces veo 1 cita
    Y la cita es de "María Gómez" a las "09:00" con estado "confirmed"

  Escenario: Combino filtros y ordeno por fecha
    Dado que inicié sesión como médico para ver mi agenda
    Y que tengo una cita confirmada de "María Gómez" dentro de 10 días a las "09:00"
    Y que tengo una cita confirmada de "María Gómez" dentro de 12 días a las "09:00"
    Y que tengo una cita pendiente de "María Gómez" dentro de 11 días a las "09:00"
    Y que tengo una cita confirmada de "Luis Peña" dentro de 10 días a las "13:00"
    Cuando consulto mi agenda con estado "confirmed", paciente "maria" y orden "descending"
    Entonces veo las citas de "María Gómez" de dentro de 12 y 10 días en ese orden

  Escenario: El calendario muestra todas las citas de un mes sin paginar
    Dado que inicié sesión como médico para ver mi agenda
    Y que tengo 25 citas pendientes de "Ana Pérez" en días seguidos desde dentro de 10 días
    Cuando consulto mi calendario de 31 días desde dentro de 10 días
    Entonces el calendario muestra 25 citas
    Cuando consulto mi calendario de 32 días desde dentro de 10 días
    Entonces la consulta de la agenda se rechaza con el código "Appointment.DateRangeTooLong"

  Escenario: El detalle muestra el motivo de consulta y el contacto del paciente
    Dado que inicié sesión como médico para ver mi agenda
    Y que tengo una cita pendiente de "María Gómez" dentro de 10 días a las "09:00" por "Dolor de pecho"
    Cuando abro el detalle de esa cita
    Entonces el detalle muestra el motivo "Dolor de pecho", el teléfono y el correo del paciente

  Escenario: La cita de otro médico no se puede ver
    Dado que inicié sesión como médico para ver mi agenda
    Y que otro médico tiene una cita dentro de 10 días a las "09:00"
    Cuando abro el detalle de esa cita
    Entonces la cita no se encuentra
