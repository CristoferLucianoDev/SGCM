# language: es
@Integration @Availability
Característica: Consultar horarios libres
  Como paciente, quiero ver los horarios libres de un médico para reservar una cita;
  como médico, quiero ver los horarios libres de mi propia agenda.

  Escenario: El paciente ve las horas libres del bloque sin la cita activa
    Dado que inicié sesión como paciente para consultar horarios libres
    Y que existe un médico con un bloque dentro de 10 días de "08:00" a "16:00"
    Y que el médico tiene una cita confirmada ese día de "10:00" a "12:00"
    Cuando consulto los horarios libres de ese médico
    Entonces recibo los horarios libres a las "08:00, 12:00, 13:00, 14:00"

  Escenario: El paciente no ve los días con menos de 4 días de antelación
    Dado que inicié sesión como paciente para consultar horarios libres
    Y que existe un médico con un bloque dentro de 3 días de "08:00" a "16:00"
    Y que el médico también tiene un bloque dentro de 4 días de "08:00" a "16:00"
    Cuando consulto los horarios libres de ese médico
    Entonces solo recibo horarios del día dentro de 4 días

  Escenario: Un bloque cerrado no ofrece horas
    Dado que inicié sesión como paciente para consultar horarios libres
    Y que existe un médico con un bloque cerrado dentro de 10 días de "08:00" a "16:00"
    Cuando consulto los horarios libres de ese médico
    Entonces no recibo horarios libres

  Escenario: El médico consulta su agenda desde la hora actual
    Dado que inicié sesión como médico para consultar mis horarios libres
    Y que tengo un bloque hoy de "08:00" a "16:00"
    Y que ya son las "09:30" de hoy
    Cuando consulto mis horarios libres
    Entonces recibo los horarios libres a las "10:00, 11:00, 12:00, 13:00, 14:00"

  Escenario: Una cita cancelada libera la hora
    Dado que inicié sesión como paciente para consultar horarios libres
    Y que existe un médico con un bloque dentro de 10 días de "08:00" a "16:00"
    Y que el médico tuvo una cita confirmada ese día de "10:00" a "12:00" y fue cancelada
    Cuando consulto los horarios libres de ese médico
    Entonces recibo los horarios libres a las "08:00, 09:00, 10:00, 11:00, 12:00, 13:00, 14:00"
