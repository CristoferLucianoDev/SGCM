# language: es
@Integration @Appointments
Característica: Cancelar cita por el médico
  Como médico, quiero cancelar una cita, para no dejar esperando a un paciente que no podré atender.

  Escenario: Cancelo una cita con motivo, la cita queda cancelada y el paciente recibe el correo con el motivo
    Dado que inicié sesión como médico para cancelar una cita
    Y que mi paciente tiene una cita "pendiente" conmigo dentro de 10 días a las "10:00"
    Cuando como médico cancelo la cita indicando el motivo "Emergencia familiar"
    Entonces la cita queda cancelada por el médico con el motivo "Emergencia familiar"
    Y el paciente recibe el aviso de la cancelación con el motivo dado y yo no recibo correo

  Escenario: Cancelar sin motivo se rechaza
    Dado que inicié sesión como médico para cancelar una cita
    Y que mi paciente tiene una cita "pendiente" conmigo dentro de 10 días a las "10:00"
    Cuando como médico cancelo la cita sin indicar un motivo
    Entonces la cancelación del médico se rechaza con el código "Appointment.StatusReasonRequired"
    Y la cita del médico sigue "pendiente"

  Escenario: Con menos de 6 horas un motivo de menos de 30 palabras se rechaza
    Dado que inicié sesión como médico para cancelar una cita
    Y que mi paciente tiene una cita "pendiente" conmigo que empieza dentro de 5 horas
    Cuando como médico cancelo la cita con un motivo de 29 palabras
    Entonces la cancelación del médico se rechaza con el código "Appointment.ShortNoticeReasonTooShort"
    Y la cita del médico sigue "pendiente"

  Escenario: Con menos de 6 horas un motivo de 30 palabras cancela la cita
    Dado que inicié sesión como médico para cancelar una cita
    Y que mi paciente tiene una cita "pendiente" conmigo que empieza dentro de 5 horas
    Cuando como médico cancelo la cita con un motivo de 30 palabras
    Entonces la cita del médico queda cancelada

  Escenario: No puedo cancelar la cita de otro médico
    Dado que inicié sesión como médico para cancelar una cita
    Y que otro médico tiene una cita "pendiente" con un paciente dentro de 10 días a las "10:00"
    Cuando como médico cancelo esa cita de otro médico indicando el motivo "Emergencia familiar"
    Entonces la cancelación del médico se rechaza con el código "Appointment.NotFound"
    Y esa cita de otro médico sigue "pendiente"
