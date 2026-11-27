# language: es
@Integration @Reports
Característica: Filtrado y orden de reportes de citas
  Como médico, quiero filtrar y ordenar un reporte, para encontrar rápido lo que necesito.

  Escenario: Combino filtros por estado, fechas, paciente y especialidad
    Dado que inicié sesión como médico para filtrar reportes de citas
    Y que para filtrar mis reportes tengo una cita "cancelada" de "José Martínez" en mi segunda especialidad dentro de 2 días
    Y que para filtrar mis reportes tengo una cita "cancelada" de "José Martínez" en mi primera especialidad dentro de 3 días
    Y que para filtrar mis reportes tengo una cita "pendiente" de "José Martínez" en mi segunda especialidad dentro de 4 días
    Y que para filtrar mis reportes tengo una cita "cancelada" de "Ana Pérez" en mi segunda especialidad dentro de 5 días
    Cuando filtro el reporte de citas "todas" entre el día 1 y el día 10 con estado "cancelada", paciente "jose" y mi segunda especialidad
    Entonces el reporte filtrado tiene 1 fila
    Y la fila del reporte filtrado es de "José Martínez" del día 2 desde hoy con estado "cancelada"

  Esquema del escenario: Ordeno el reporte por una columna en los dos sentidos
    Dado que inicié sesión como médico para filtrar reportes de citas
    Y que para filtrar mis reportes tengo una cita "confirmada" de "Carla Gómez" en mi primera especialidad dentro de 1 día
    Y que para filtrar mis reportes tengo una cita "completada" de "Ana Pérez" en mi primera especialidad dentro de 2 días
    Y que para filtrar mis reportes tengo una cita "pendiente" de "Bruno Díaz" en mi primera especialidad dentro de 3 días
    Cuando ordeno el reporte de citas "todas" entre el día 1 y el día 10 por "<columna>" en sentido "<sentido>"
    Entonces las filas del reporte filtrado son de los pacientes "<pacientes>" en ese orden

    Ejemplos:
      | columna  | sentido     | pacientes                                |
      | fecha    | ascendente  | Carla Gómez, Ana Pérez, Bruno Díaz       |
      | fecha    | descendente | Bruno Díaz, Ana Pérez, Carla Gómez       |
      | paciente | ascendente  | Ana Pérez, Bruno Díaz, Carla Gómez       |
      | paciente | descendente | Carla Gómez, Bruno Díaz, Ana Pérez       |
      | estado   | ascendente  | Bruno Díaz, Carla Gómez, Ana Pérez       |
      | estado   | descendente | Ana Pérez, Carla Gómez, Bruno Díaz       |

  Escenario: Filtrar u ordenar no genera un nuevo registro de reporte
    Dado que inicié sesión como médico para filtrar reportes de citas
    Y que para filtrar mis reportes tengo una cita "pendiente" de "Ana Pérez" en mi primera especialidad dentro de 1 día
    Cuando filtro el reporte de citas "todas" entre el día 1 y el día 10 con estado "pendiente"
    Y ordeno el reporte de citas "todas" entre el día 1 y el día 10 por "paciente" en sentido "descendente"
    Entonces no hay filas en el registro de reportes

  Escenario: Si ningún resultado coincide se muestra que no hay datos
    Dado que inicié sesión como médico para filtrar reportes de citas
    Y que para filtrar mis reportes tengo una cita "pendiente" de "Ana Pérez" en mi primera especialidad dentro de 1 día
    Cuando filtro el reporte de citas "todas" entre el día 1 y el día 10 con estado "completada"
    Entonces el reporte filtrado tiene 0 filas
    Y el reporte filtrado muestra el mensaje "No hay citas del tipo y el rango de fechas elegidos."
