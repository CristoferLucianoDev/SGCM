# language: es
@Integration @Availability
Característica: Editar la serie de disponibilidad
  Como médico, quiero cambiar los días y el horario de mi serie, para ajustar mi agenda; el cambio aplica desde la
  primera semana que se puede reprogramar, respeta los días que edité a mano y cancela con mi motivo las citas que
  quedan fuera. También puedo consultar mis bloques de hasta 31 días.

  Escenario: Un martes la serie cambia desde el lunes siguiente y la semana actual no cambia
    Dado que inicié sesión como médico para editar una serie
    Y que hoy es martes
    Y que tengo una serie los lunes, martes y jueves de "08:00" a "14:00" desde hoy
    Cuando edito la serie a los lunes, martes y miércoles de "10:00" a "16:00" sin motivo
    Entonces la serie cambia desde el lunes siguiente y se cancelaron 0 citas
    Y el bloque de dentro de 2 días sigue de "08:00" a "14:00"
    Y la semana siguiente tiene bloques solo el lunes, martes y miércoles de "10:00" a "16:00"

  Escenario: Un martes editado a mano conserva su horario
    Dado que inicié sesión como médico para editar una serie
    Y que hoy es martes
    Y que tengo una serie los lunes, martes y jueves de "08:00" a "14:00" desde hoy
    Y que edité a mano el martes de la semana siguiente de "09:00" a "15:00"
    Cuando edito la serie a los lunes, martes y miércoles de "10:00" a "16:00" sin motivo
    Entonces el martes de la semana siguiente sigue de "09:00" a "15:00"

  Escenario: Las citas que quedan fuera se cancelan con el motivo y se avisa al paciente
    Dado que inicié sesión como médico para editar una serie
    Y que hoy es martes
    Y que tengo una serie los lunes, martes y jueves de "08:00" a "14:00" desde hoy
    Y que un paciente tiene cita el jueves de la semana siguiente a las "08:00"
    Cuando edito la serie a los lunes, martes y miércoles de "10:00" a "16:00" con el motivo "Cambio de consultorio"
    Entonces la serie cambia desde el lunes siguiente y se cancelaron 1 citas
    Y el paciente de la serie recibe un correo con su cita cancelada y el motivo "Cambio de consultorio"

  Escenario: Si quedan citas fuera y no hay motivo la edición se rechaza
    Dado que inicié sesión como médico para editar una serie
    Y que hoy es martes
    Y que tengo una serie los lunes, martes y jueves de "08:00" a "14:00" desde hoy
    Y que un paciente tiene cita el jueves de la semana siguiente a las "08:00"
    Cuando edito la serie a los lunes, martes y miércoles de "10:00" a "16:00" sin motivo
    Entonces la edición de la serie se rechaza con el código "Appointment.StatusReasonRequired"
    Y la cita del paciente de la serie sigue activa

  Escenario: Consulto mis bloques de hasta 31 días
    Dado que inicié sesión como médico para editar una serie
    Y que hoy es martes
    Y que tengo una serie los lunes, martes y jueves de "08:00" a "14:00" desde hoy
    Cuando consulto mis bloques de 7 días desde el lunes siguiente
    Entonces veo mis bloques del lunes, martes y jueves de esa semana
    Cuando consulto mis bloques de 32 días desde el lunes siguiente
    Entonces la consulta de bloques se rechaza con el código "Availability.DateRangeTooLong"
