# language: es
@Integration @Appointments
Característica: Reprogramar cita por el médico
  Como médico, quiero mover una cita a otro de mis horarios, para resolver un imprevisto sin cancelarla.

  Escenario: Muevo una cita pendiente a un horario libre de mis bloques y el paciente recibe el correo con la nueva fecha
    Dado que inicié sesión como médico para reprogramar mis citas
    Y que tengo en mi agenda una cita "pendiente" de un paciente para dentro de 10 días a las "10:00"
    Y que tengo un horario libre en mis bloques dentro de 1 día a las "10:00"
    Cuando muevo esa cita a dentro de 1 día a las "10:00"
    Entonces el cambio de horario de la cita se acepta
    Y la cita movida queda marcada como "confirmada"
    Y la cita movida empieza dentro de 1 día a las "10:00"
    Y el paciente recibe un solo correo con la nueva fecha de dentro de 1 día a las "10:00"

  Escenario: Puedo mover una cita aunque falten menos de 48 horas
    Dado que inicié sesión como médico para reprogramar mis citas
    Y que tengo en mi agenda una cita "confirmada" de un paciente que empieza dentro de 23 horas
    Y que tengo un horario libre en mis bloques que empieza dentro de 30 horas
    Cuando muevo esa cita a la hora que empieza dentro de 30 horas
    Entonces el cambio de horario de la cita se acepta
    Y la cita movida queda marcada como "confirmada"

  Escenario: Un horario ocupado se rechaza
    Dado que inicié sesión como médico para reprogramar mis citas
    Y que tengo en mi agenda una cita "confirmada" de un paciente para dentro de 10 días a las "10:00"
    Y que otro paciente ya ocupa mi horario dentro de 1 día a las "10:00"
    Cuando muevo esa cita a dentro de 1 día a las "10:00"
    Entonces el cambio de horario de la cita se rechaza con el código "Appointment.SlotUnavailable"
    Y la cita que quise mover sigue en su horario original

  Escenario: Una hora pasada se rechaza
    Dado que inicié sesión como médico para reprogramar mis citas
    Y que tengo en mi agenda una cita "confirmada" de un paciente para dentro de 10 días a las "10:00"
    Cuando muevo esa cita a la hora que empezó hace 1 hora
    Entonces el cambio de horario de la cita se rechaza con el código "Appointment.NewStartNotInFuture"
    Y la cita que quise mover sigue en su horario original
