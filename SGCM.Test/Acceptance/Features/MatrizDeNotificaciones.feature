# language: es
@Integration @Notifications
Característica: Notificaciones automáticas por cambio de estado
  Como paciente o médico, quiero recibir un correo cuando la otra parte o el sistema cambia el estado de una cita,
  para enterarme a tiempo; al solicitar una cita el paciente no recibe correo.

  Esquema del escenario: Cada cambio de estado avisa solo a quien corresponde
    Dado que un paciente y su médico tienen una cita "<estado>" dentro de 10 días a las "10:00"
    Cuando ocurre "<transición>"
    Entonces reciben correo exactamente "<correos>"

    Ejemplos:
      | transición                         | estado    | correos                                                                     |
      | la solicitud del paciente          | ninguna   | médico:appointment-requested                                                |
      | la solicitud con cambio de médico  | ninguna   | otro médico:appointment-requested                                           |
      | la confirmación del médico         | pending   | paciente:appointment-confirmed                                              |
      | el rechazo del médico              | pending   | paciente:appointment-rejected                                               |
      | la cancelación del paciente        | confirmed | médico:appointment-cancelled-by-patient                                     |
      | la cancelación del médico          | confirmed | paciente:appointment-cancelled-for-patient                                  |
      | la reprogramación del paciente     | confirmed | médico:appointment-rescheduled-by-patient                                   |
      | la reprogramación del médico       | confirmed | paciente:appointment-rescheduled-by-doctor                                  |
      | el vencimiento de la solicitud     | pending   | paciente:appointment-expired-for-patient, médico:appointment-expired-for-doctor |
      | el cierre del bloque               | confirmed | paciente:appointment-cancelled-for-patient                                  |
