# language: es
@Integration @Appointments
Característica: Vencimiento de citas no confirmadas
  Como paciente, quiero saber a tiempo si mi solicitud no fue atendida, para buscar otro horario.

  Escenario: Una solicitud sin confirmar que pasa el plazo de 48 horas se cancela y las dos partes reciben el aviso
    Dado que existe una solicitud "pendiente" con su médico para dentro de 10 días a las "10:00"
    Cuando pasa el tiempo hasta 47 horas antes del inicio de la cita
    Y se ejecuta el vencimiento de citas no confirmadas
    Entonces el vencimiento procesó 1 cita
    Y el estado de la cita tras el vencimiento es "cancelada"
    Y el motivo de cancelación de esa cita es "No confirmada por el médico a tiempo"
    Y el paciente y el médico reciben el aviso de vencimiento de esa cita

  Escenario: Una cita confirmada no se ve afectada por el vencimiento
    Dado que existe una solicitud "confirmada" con su médico para dentro de 10 días a las "10:00"
    Cuando pasa el tiempo hasta 47 horas antes del inicio de la cita
    Y se ejecuta el vencimiento de citas no confirmadas
    Entonces el vencimiento procesó 0 citas
    Y el estado de la cita tras el vencimiento es "confirmada"
    Y no se envía ningún aviso de vencimiento

  Escenario: Una solicitud que todavía está en plazo no vence
    Dado que existe una solicitud "pendiente" con su médico para dentro de 10 días a las "10:00"
    Cuando pasa el tiempo hasta 72 horas antes del inicio de la cita
    Y se ejecuta el vencimiento de citas no confirmadas
    Entonces el vencimiento procesó 0 citas
    Y el estado de la cita tras el vencimiento es "pendiente"
    Y no se envía ningún aviso de vencimiento
