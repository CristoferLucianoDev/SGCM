# language: es
@Integration @Reports
Característica: Exportación de reportes de citas
  Como médico, quiero exportar un reporte, para guardarlo, imprimirlo o compartirlo.

  Esquema del escenario: Exporto el reporte en pantalla, con sus filtros y orden, y queda registrado
    Dado que inicié sesión como médico para exportar reportes de citas
    Y que para exportar mis reportes tengo una cita "pendiente" de "Bruno Díaz" dentro de 1 día
    Y que para exportar mis reportes tengo una cita "confirmada" de "Ana Pérez" dentro de 2 días
    Y que para exportar mis reportes tengo una cita "completada" de "Carla Gómez" dentro de 3 días
    Cuando exporto en "<formato>" el reporte de citas "todas" entre el día 1 y el día 10 ordenado por paciente descendente
    Entonces recibo un archivo "<formato>" válido
    Y queda registrada una exportación en formato "<formato>" con 3 filas

    Ejemplos:
      | formato |
      | pdf     |
      | xlsx    |
      | docx    |

  Esquema del escenario: El archivo tiene las mismas filas y en el mismo orden que la pantalla
    Dado que inicié sesión como médico para exportar reportes de citas
    Y que para exportar mis reportes tengo una cita "pendiente" de "Bruno Díaz" dentro de 1 día
    Y que para exportar mis reportes tengo una cita "confirmada" de "Ana Pérez" dentro de 2 días
    Y que para exportar mis reportes tengo una cita "completada" de "Carla Gómez" dentro de 3 días
    Cuando exporto en "<formato>" el reporte de citas "todas" entre el día 1 y el día 10 ordenado por paciente descendente
    Entonces la tabla del archivo exportado es de los pacientes "Carla Gómez, Bruno Díaz, Ana Pérez" en ese orden
    Y la tabla del archivo exportado no tiene identificadores internos

    Ejemplos:
      | formato |
      | xlsx    |
      | docx    |

  Escenario: Un reporte sin datos no se puede exportar y se muestra un mensaje
    Dado que inicié sesión como médico para exportar reportes de citas
    Y que para exportar mis reportes tengo una cita "pendiente" de "Ana Pérez" dentro de 1 día
    Cuando exporto en "pdf" el reporte de citas "realizadas" entre el día 1 y el día 10 ordenado por paciente descendente
    Entonces la exportación se rechaza con el código "Report.EmptyReport" y un mensaje
    Y no queda ninguna exportación registrada
