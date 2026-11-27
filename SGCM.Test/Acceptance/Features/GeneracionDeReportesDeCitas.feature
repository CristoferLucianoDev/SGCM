# language: es
@Integration @Reports
Característica: Generación de reportes de citas
  Como médico, quiero generar reportes de mis citas realizadas, canceladas y pendientes, para analizar mi actividad.

  Escenario: Genero el reporte de todas mis citas con los totales por estado y la tabla
    Dado que inicié sesión como médico para generar reportes de citas
    Y que para mis reportes tengo una cita "pendiente" dentro de 1 día a las "10:00"
    Y que para mis reportes tengo una cita "confirmada" dentro de 2 días a las "10:00"
    Y que para mis reportes tengo una cita "cancelada" dentro de 3 días a las "10:00"
    Y que para mis reportes tengo una cita "completada" dentro de 4 días a las "10:00"
    Cuando genero el reporte de citas "todas" entre el día 1 y el día 10 desde hoy
    Entonces el reporte tiene 4 filas
    Y el reporte totaliza 1 cita en estado "pendiente"
    Y el reporte totaliza 1 cita en estado "completada"
    Y el reporte totaliza 0 citas en estado "rechazada"
    Y la primera fila del reporte es del día 1 desde hoy a las "10:00" con el paciente "Ana Pérez", la especialidad sembrada y el estado "pendiente"

  Escenario: El reporte de pendientes incluye las citas confirmadas
    Dado que inicié sesión como médico para generar reportes de citas
    Y que para mis reportes tengo una cita "pendiente" dentro de 1 día a las "10:00"
    Y que para mis reportes tengo una cita "confirmada" dentro de 2 días a las "10:00"
    Y que para mis reportes tengo una cita "cancelada" dentro de 3 días a las "10:00"
    Cuando genero el reporte de citas "pendientes" entre el día 1 y el día 10 desde hoy
    Entonces el reporte tiene 2 filas
    Y las filas del reporte solo tienen los estados "pendiente" y "confirmada"

  Escenario: Solo aparecen las citas del médico autenticado
    Dado que inicié sesión como médico para generar reportes de citas
    Y que para mis reportes tengo una cita "pendiente" dentro de 1 día a las "10:00"
    Y que otro médico tiene una cita "pendiente" para sus reportes dentro de 2 días a las "10:00"
    Cuando genero el reporte de citas "todas" entre el día 1 y el día 10 desde hoy
    Entonces el reporte tiene 1 fila

  Escenario: El reporte no muestra identificadores internos
    Dado que inicié sesión como médico para generar reportes de citas
    Y que para mis reportes tengo una cita "completada" dentro de 1 día a las "10:00"
    Cuando genero el reporte de citas "todas" entre el día 1 y el día 10 desde hoy
    Entonces la respuesta del reporte no contiene identificadores internos

  Escenario: Cada reporte generado queda registrado con su tipo y filtros
    Dado que inicié sesión como médico para generar reportes de citas
    Y que para mis reportes tengo una cita "cancelada" dentro de 2 días a las "10:00"
    Cuando genero el reporte de citas "canceladas" entre el día 1 y el día 10 desde hoy
    Entonces queda registrada una generación de reporte de tipo "canceladas" con 1 fila
    Y el registro del reporte guarda el rango del día 1 al día 10 desde hoy

  Escenario: Sin citas del tipo y rango elegidos se muestra que no hay datos
    Dado que inicié sesión como médico para generar reportes de citas
    Y que para mis reportes tengo una cita "pendiente" dentro de 2 días a las "10:00"
    Cuando genero el reporte de citas "realizadas" entre el día 1 y el día 10 desde hoy
    Entonces el reporte tiene 0 filas
    Y el reporte muestra el mensaje "No hay citas del tipo y el rango de fechas elegidos."
    Y todos los totales del reporte están en cero

  Escenario: El rango de fechas es obligatorio
    Dado que inicié sesión como médico para generar reportes de citas
    Cuando genero el reporte de citas "todas" sin rango de fechas
    Entonces la generación del reporte se rechaza con el código "Report.DateRangeRequired"
