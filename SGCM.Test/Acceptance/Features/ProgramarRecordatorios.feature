# language: es
@Integration @Notifications
Característica: Programar recordatorios a pacientes
  Como médico, quiero programar recordatorios para mis citas confirmadas o completadas, eligiendo una plantilla o
  escribiendo un mensaje libre, para que el paciente reciba el texto que yo decida a la hora que elija.

  Escenario: Al elegir una plantilla el mensaje aparece con los datos reales de la cita y se puede editar
    Dado que inicié sesión como médico con una cita "confirmed" dentro de 10 días a las "10:00"
    Cuando pido la vista previa de la plantilla global para esa cita
    Entonces el mensaje muestra la fecha y la hora reales de la cita sin variables
    Cuando programo el mensaje de la vista previa con el agregado " Traiga sus estudios." para mañana a las "09:00"
    Entonces queda un recordatorio pendiente con el texto editado y la plantilla como referencia

  Escenario: El médico programa un mensaje libre para una cita completada
    Dado que inicié sesión como médico con una cita "completed" dentro de 10 días a las "10:00"
    Cuando programo el mensaje libre "¿Cómo sigue después de su consulta?" para mañana a las "09:00"
    Entonces queda un recordatorio pendiente con el texto "¿Cómo sigue después de su consulta?" sin plantilla

  Escenario: Llegada la hora el paciente recibe el recordatorio y queda registrado como enviado
    Dado que inicié sesión como médico con una cita "confirmed" dentro de 10 días a las "10:00"
    Y que programé el mensaje libre "Recuerde ayunar 8 horas antes." para mañana a las "09:00"
    Cuando llega la hora del recordatorio programado y corre el envío de recordatorios
    Entonces el paciente recibe el recordatorio "Recuerde ayunar 8 horas antes."
    Y el recordatorio programado queda registrado como "sent"

  Escenario: Cambiar la plantilla después no altera el texto guardado
    Dado que inicié sesión como médico con una cita "confirmed" dentro de 10 días a las "10:00"
    Y que tengo una plantilla propia con el texto "Hola {PatientName}, traiga sus estudios."
    Y que programé el mensaje de la vista previa de mi plantilla para mañana a las "09:00"
    Cuando cambio el texto de mi plantilla a "Hola {PatientName}, texto nuevo."
    Y llega la hora del recordatorio programado y corre el envío de recordatorios
    Entonces el paciente recibe el recordatorio con el texto que se guardó al programarlo

  Escenario: Una fecha pasada se rechaza
    Dado que inicié sesión como médico con una cita "confirmed" dentro de 10 días a las "10:00"
    Cuando programo el mensaje libre "Hola" para hace 1 hora
    Entonces la programación se rechaza con el estado 400 y el código "Reminder.ScheduledInPast"

  Escenario: La cita de otro médico se rechaza
    Dado que inicié sesión como médico con una cita "confirmed" dentro de 10 días a las "10:00"
    Y que la cita es de otro médico
    Cuando programo el mensaje libre "Hola" para mañana a las "09:00"
    Entonces la programación se rechaza con el estado 404 y el código "Appointment.NotFound"

  Escenario: Una cita pendiente no admite recordatorios
    Dado que inicié sesión como médico con una cita "pending" dentro de 10 días a las "10:00"
    Cuando programo el mensaje libre "Hola" para mañana a las "09:00"
    Entonces la programación se rechaza con el estado 409 y el código "Reminder.AppointmentNotEligible"
