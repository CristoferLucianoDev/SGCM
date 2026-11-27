# language: es
@Integration @Notifications
Característica: Recordatorio automático de cita
  Como paciente, quiero recibir un recordatorio 24 horas antes de mi cita confirmada, para no olvidarla; si la cita
  se cancela no lo recibo y si se reprograma llega con la hora nueva.

  Escenario: Al confirmarse una cita se programa un recordatorio 24 horas antes
    Dado que inicié sesión como médico con una cita pendiente dentro de 10 días a las "10:00"
    Cuando confirmo la cita del recordatorio
    Entonces queda programado un recordatorio 24 horas antes de la cita con la plantilla global

  Escenario: Llegada la hora el paciente recibe el recordatorio y queda registrado como enviado
    Dado que inicié sesión como médico con una cita pendiente dentro de 10 días a las "10:00"
    Y que confirmé la cita del recordatorio
    Cuando llega la hora del recordatorio y corre el envío de recordatorios
    Entonces el paciente recibe el recordatorio con el texto programado
    Y el recordatorio queda registrado como "sent"

  Escenario: Si la cita se cancela el recordatorio se anula
    Dado que inicié sesión como médico con una cita pendiente dentro de 10 días a las "10:00"
    Y que confirmé la cita del recordatorio
    Cuando cancelo la cita del recordatorio con el motivo "Viaje al congreso"
    Entonces el recordatorio queda registrado como "cancelled"

  Escenario: Si el médico reprograma la cita el recordatorio se recalcula
    Dado que inicié sesión como médico con una cita pendiente dentro de 10 días a las "10:00"
    Y que confirmé la cita del recordatorio
    Cuando reprogramo la cita del recordatorio a dentro de 12 días a las "13:00"
    Entonces el recordatorio anterior queda anulado y hay uno nuevo 24 horas antes de la hora nueva

  Escenario: Si la cita queda a menos de 24 horas no se programa el recordatorio
    Dado que inicié sesión como médico con una cita pendiente dentro de 10 días a las "10:00"
    Y que confirmé la cita del recordatorio
    Cuando reprogramo la cita del recordatorio a dentro de 5 horas
    Entonces la cita no tiene recordatorios pendientes
