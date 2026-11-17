# language: es
@Integration @Availability
Característica: Configurar horario semanal
  Como médico, quiero declarar mi horario una sola vez para los días que atiendo,
  para no crear un bloque por cada día.

  Antecedentes:
    Dado que inicié sesión como médico para configurar mi horario

  Escenario: El médico crea su serie y se genera un bloque por día hasta 90 días adelante
    Cuando creo una serie los días "lunes, miércoles, viernes" de "08:00" a "14:00" desde hoy
    Entonces la serie es creada
    Y tengo un bloque de "08:00" a "14:00" cada "lunes, miércoles, viernes" desde hoy hasta 90 días adelante

  Escenario: Con fecha hasta, los bloques terminan ese día
    Cuando creo una serie todos los días de "08:00" a "14:00" desde hoy hasta dentro de 9 días
    Entonces la serie es creada
    Y tengo 10 bloques y el último es dentro de 9 días

  Escenario: Al avanzar un día se agrega el bloque nuevo
    Dado que creé una serie todos los días de "08:00" a "14:00" desde hoy
    Cuando pasa un día y se ejecuta la generación de bloques
    Entonces se agregó solo el bloque del día 91

  Esquema del escenario: Un horario inválido se rechaza
    Cuando creo una serie los días "lunes" de "<inicio>" a "<fin>" desde hoy
    Entonces la serie es rechazada con el error "<código>"
    Y no tengo series ni bloques

    Ejemplos:
      | inicio | fin   | código                             |
      | 08:30  | 14:00 | AvailabilitySeries.TimeNotOnHour    |
      | 08:00  | 13:00 | AvailabilitySeries.DurationTooShort |
      | 14:00  | 08:00 | AvailabilitySeries.InvalidTimeRange |

  Escenario: La fecha desde no puede ser pasada
    Cuando creo una serie los días "lunes" de "08:00" a "14:00" desde ayer
    Entonces la serie es rechazada con el error "AvailabilitySeries.StartDateInPast"
    Y no tengo series ni bloques

  Escenario: Una serie que se traslapa con otra serie del médico se rechaza
    Dado que creé una serie todos los días de "08:00" a "14:00" desde hoy
    Cuando creo una serie los días "lunes" de "10:00" a "16:00" desde hoy
    Entonces la serie es rechazada por traslape
    Y sigo teniendo una sola serie y sus bloques

  Escenario: Una serie que se traslapa con un bloque existente se rechaza
    Dado que tengo un bloque suelto dentro de 5 días de "09:00" a "15:00"
    Cuando creo una serie todos los días de "08:00" a "14:00" desde hoy
    Entonces la serie es rechazada por traslape
    Y solo tengo el bloque suelto

  Escenario: Una serie contigua a otra se acepta
    Dado que creé una serie todos los días de "08:00" a "14:00" desde hoy
    Cuando creo una serie todos los días de "14:00" a "20:00" desde hoy
    Entonces la serie es creada
    Y tengo dos series con sus bloques
