# language: es
@Integration @Appointments
Característica: Gestionar cita en curso
  Como médico, quiero extender o terminar la cita que estoy atendiendo, para que mi agenda refleje la hora real.

  Escenario: Extiendo una cita en curso dentro de mi bloque y su fin avanza
    Dado que inicié sesión como médico para atender citas en curso
    Y que estoy atendiendo una cita confirmada que empezó hace 1 hora y termina dentro de 1 hora
    Cuando extiendo la cita que estoy atendiendo por 1 hora más
    Entonces la extensión de la cita en curso se acepta
    Y la cita en curso termina 1 hora después de lo previsto

  Escenario: Extender una cita en curso que chocaría con la siguiente cita se rechaza
    Dado que inicié sesión como médico para atender citas en curso
    Y que estoy atendiendo una cita confirmada que empezó hace 1 hora y termina dentro de 1 hora
    Y que mi agenda tiene otra cita confirmada que empieza justo cuando termina la que estoy atendiendo
    Cuando extiendo la cita que estoy atendiendo por 1 hora más
    Entonces la extensión de la cita en curso se rechaza con el código "Appointment.ExtensionUnavailable"
    Y la cita en curso conserva su hora de fin prevista

  Escenario: Termino una cita en curso antes de tiempo y queda como completada con la hora real de fin
    Dado que inicié sesión como médico para atender citas en curso
    Y que estoy atendiendo una cita confirmada que empezó hace 1 hora y termina dentro de 1 hora
    Cuando termino la cita que estoy atendiendo antes de lo previsto
    Entonces la terminación de la cita en curso se acepta
    Y la cita en curso queda marcada como "completada"
    Y la hora de fin de la cita en curso es la hora real en que la terminé

  Escenario: Una cita confirmada que ya terminó se completa automáticamente
    Dado que inicié sesión como médico para atender citas en curso
    Y que una cita confirmada de mi agenda empezó hace 3 horas y terminó hace 1 hora sin que yo la cerrara
    Cuando se ejecuta el proceso de completado automático de citas terminadas
    Entonces el proceso de completado automático terminó 1 cita
    Y la cita terminada de mi agenda queda marcada como "completada" con su fin previsto
