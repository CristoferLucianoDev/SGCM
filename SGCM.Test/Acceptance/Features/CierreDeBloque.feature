# language: es
@Integration @Availability
Característica: Cerrar bloque de disponibilidad
  Como médico, quiero cerrar un bloque de mi horario en cualquier semana, para dejar de ofrecer esas horas; las citas
  activas se cancelan con mi motivo y cada paciente recibe un correo.

  Escenario: Cerrar un bloque de la semana actual sin citas lo deja sin horarios de inmediato
    Dado que inicié sesión como médico para cerrar un bloque
    Y que tengo un bloque que empieza dentro de 1 hora y dura 6 horas
    Cuando cierro ese bloque sin motivo
    Entonces el bloque queda cerrado y se cancelaron 0 citas

  Escenario: Cerrar un bloque con citas activas las cancela con el motivo y avisa a cada paciente
    Dado que inicié sesión como médico para cerrar un bloque
    Y que tengo un bloque dentro de 10 días de "08:00" a "14:00"
    Y que dos pacientes tienen cita en ese bloque a las "08:00" y a las "11:00"
    Cuando cierro ese bloque con el motivo "Congreso médico"
    Entonces el bloque queda cerrado y se cancelaron 2 citas
    Y cada paciente recibe un correo con su cita cancelada y el motivo "Congreso médico"

  Escenario: Cerrar un bloque con citas activas sin motivo se rechaza
    Dado que inicié sesión como médico para cerrar un bloque
    Y que tengo un bloque dentro de 10 días de "08:00" a "14:00"
    Y que dos pacientes tienen cita en ese bloque a las "08:00" y a las "11:00"
    Cuando cierro ese bloque sin motivo
    Entonces el cierre se rechaza con el código "Appointment.StatusReasonRequired"
    Y el bloque sigue abierto y las citas siguen activas

  Escenario: Con una cita a menos de 6 horas el sistema advierte y exige un motivo de al menos 30 palabras
    Dado que inicié sesión como médico para cerrar un bloque
    Y que tengo un bloque que empieza dentro de 1 hora y dura 6 horas
    Y que un paciente tiene cita al inicio de ese bloque
    Cuando consulto las citas afectadas por cerrar ese bloque
    Entonces el aviso indica 1 cita y la advertencia de 30 palabras
    Cuando cierro ese bloque con el motivo "Congreso médico"
    Entonces el cierre se rechaza con el código "Appointment.ShortNoticeReasonTooShort"
    Cuando cierro ese bloque con un motivo de 30 palabras
    Entonces el bloque queda cerrado y se cancelaron 1 citas
