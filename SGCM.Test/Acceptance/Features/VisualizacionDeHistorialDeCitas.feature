# language: es
@Integration @Appointments
Característica: Visualización de historial de citas
  Como paciente, quiero ver todas mis citas pasadas y futuras, para tener el registro completo de mi atención.

  Escenario: Veo mis citas pasadas y futuras con fecha, hora, médico, especialidad y estado
    Dado que inicié sesión como paciente para revisar mi historial de citas
    Y que en mi historial hay una cita "completada" de hace 5 días a las "10:00"
    Y que en mi historial hay una cita "pendiente" para dentro de 3 días a las "10:00"
    Y que otro paciente tiene en su historial una cita "pendiente" para dentro de 4 días a las "10:00"
    Cuando reviso mi historial completo de citas
    Entonces el listado de mi historial contiene 2 citas
    Y el listado ordena sus citas de la más reciente a la más antigua
    Y la primera cita del listado tiene estado "pendiente"
    Y la primera cita del listado empieza a las "10:00" y termina a las "12:00"
    Y la primera cita del listado es del médico "Luis Pérez" y de la especialidad sembrada

  Escenario: Filtro mis citas por estado
    Dado que inicié sesión como paciente para revisar mi historial de citas
    Y que en mi historial hay una cita "cancelada" de hace 5 días a las "10:00"
    Y que en mi historial hay una cita "completada" de hace 2 días a las "10:00"
    Y que en mi historial hay una cita "pendiente" para dentro de 3 días a las "10:00"
    Cuando reviso mi historial de citas filtrando por el estado "cancelada"
    Entonces el listado de mi historial contiene 1 cita
    Y todas las citas del listado tienen estado "cancelada"

  Escenario: Filtro mis citas por rango de fechas
    Dado que inicié sesión como paciente para revisar mi historial de citas
    Y que en mi historial hay una cita "pendiente" para dentro de 1 día a las "10:00"
    Y que en mi historial hay una cita "pendiente" para dentro de 2 días a las "00:00"
    Y que en mi historial hay una cita "pendiente" para dentro de 3 días a las "10:00"
    Y que en mi historial hay una cita "pendiente" para dentro de 4 días a las "22:00"
    Y que en mi historial hay una cita "pendiente" para dentro de 5 días a las "10:00"
    Cuando reviso mi historial de citas entre el día 2 y el día 4 desde hoy
    Entonces el listado solo incluye las citas de los días 2 a 4 desde hoy

  Escenario: El historial es paginado
    Dado que inicié sesión como paciente para revisar mi historial de citas
    Y que en mi historial hay una cita "pendiente" para dentro de 1 día a las "10:00"
    Y que en mi historial hay una cita "pendiente" para dentro de 2 días a las "10:00"
    Y que en mi historial hay una cita "pendiente" para dentro de 3 días a las "10:00"
    Y que en mi historial hay una cita "pendiente" para dentro de 4 días a las "10:00"
    Y que en mi historial hay una cita "pendiente" para dentro de 5 días a las "10:00"
    Cuando reviso la página 2 de mi historial con 2 citas por página
    Entonces el listado informa la página 2, el tamaño 2 y un total de 5 citas
    Y el listado devuelve la cita número 3 y la número 4 de mi historial ordenado de la más reciente

  Escenario: El detalle muestra el motivo de consulta y el motivo de cancelación
    Dado que inicié sesión como paciente para revisar mi historial de citas
    Y que en mi historial hay una cita "cancelada" para dentro de 3 días a las "10:00" con motivo de consulta "Dolor de cabeza" y motivo de estado "Viaje"
    Cuando abro el detalle de esa cita en mi historial
    Entonces el detalle de mi historial tiene estado "cancelada"
    Y el detalle indica el motivo de consulta "Dolor de cabeza"
    Y el detalle indica el motivo de estado "Viaje"

  Escenario: No puedo consultar la cita de otro paciente
    Dado que inicié sesión como paciente para revisar mi historial de citas
    Y que otro paciente tiene en su historial una cita "pendiente" para dentro de 3 días a las "10:00"
    Cuando abro el detalle de la cita del otro paciente
    Entonces la consulta del historial se rechaza con el código "Appointment.NotFound"
