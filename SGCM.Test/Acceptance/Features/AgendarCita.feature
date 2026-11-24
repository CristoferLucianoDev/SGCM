# language: es
@Integration @Appointments
Característica: Agendar cita
  Como paciente con mi perfil completo, quiero agendar una cita eligiendo la especialidad, un horario libre y,
  si quiero, el médico, para que el médico reciba mi solicitud.

  Escenario: Agendar con un médico elegido crea una cita pendiente de 2 horas y avisa solo al médico
    Dado que inicié sesión como paciente para agendar una cita
    Y que existe un médico para agendar con un bloque dentro de 10 días de "08:00" a "14:00"
    Cuando solicito una cita con el primer médico ese día a las "09:00"
    Entonces la cita queda pendiente de 2 horas con el primer médico
    Y el médico recibe la notificación de la solicitud y el paciente no recibe correo

  Escenario: Agendar solo con la especialidad muestra el médico asignado y su tarifa antes de enviar
    Dado que inicié sesión como paciente para agendar una cita
    Y que existe un médico para agendar con un bloque dentro de 10 días de "08:00" a "14:00"
    Cuando pido el resumen de la especialidad para ese día a las "10:00"
    Entonces el resumen muestra al primer médico con una tarifa de 1500
    Cuando envío la solicitud con el médico del resumen
    Entonces la cita queda pendiente de 2 horas con el primer médico

  Escenario: Si el médico asignado pierde el horario antes de enviar se asigna otro libre y se informa
    Dado que inicié sesión como paciente para agendar una cita
    Y que existe un médico para agendar con un bloque dentro de 10 días de "08:00" a "14:00"
    Y que existe otro médico de la misma especialidad con un bloque ese día de "08:00" a "14:00"
    Y que el primer médico recibió otra cita ese día a las "10:00"
    Cuando solicito una cita con el primer médico como asignado ese día a las "10:00"
    Entonces la cita queda pendiente con el otro médico y se informa el cambio de médico

  Escenario: Si el médico asignado pierde el horario y no queda ninguno libre se rechaza
    Dado que inicié sesión como paciente para agendar una cita
    Y que existe un médico para agendar con un bloque dentro de 10 días de "08:00" a "14:00"
    Y que el primer médico recibió otra cita ese día a las "10:00"
    Cuando solicito una cita con el primer médico como asignado ese día a las "10:00"
    Entonces la solicitud se rechaza con el código "Appointment.SlotUnavailable"

  Escenario: Si el paciente eligió al médico y otro paciente reservó ese horario antes se rechaza
    Dado que inicié sesión como paciente para agendar una cita
    Y que existe un médico para agendar con un bloque dentro de 10 días de "08:00" a "14:00"
    Y que el primer médico recibió otra cita ese día a las "10:00"
    Cuando solicito una cita con el primer médico ese día a las "11:00"
    Entonces la solicitud se rechaza con el código "Appointment.SlotUnavailable"

  Escenario: Un horario a menos de 4 días se rechaza indicando que debe llamar al médico
    Dado que inicié sesión como paciente para agendar una cita
    Y que existe un médico para agendar con un bloque dentro de 3 días de "08:00" a "14:00"
    Cuando solicito una cita con el primer médico ese día a las "10:00"
    Entonces la solicitud se rechaza con el código "Appointment.TooSoon"
    Y el mensaje indica que debo llamar al médico

  Escenario: La reserva se rechaza si se traslapa con otra cita activa del paciente
    Dado que inicié sesión como paciente para agendar una cita
    Y que existe un médico para agendar con un bloque dentro de 10 días de "08:00" a "14:00"
    Y que existe otro médico de la misma especialidad con un bloque ese día de "08:00" a "14:00"
    Y que ya tengo una cita con el primer médico ese día a las "09:00"
    Cuando solicito una cita con el otro médico ese día a las "10:00"
    Entonces la solicitud se rechaza con el código "Appointment.PatientOverlap"

  Escenario: Dos reservas simultáneas del mismo horario se resuelven con una sola cita
    Dado que existe un médico para agendar con un bloque dentro de 10 días de "08:00" a "14:00"
    Cuando dos pacientes solicitan a la vez el primer médico ese día a las "10:00"
    Entonces una solicitud crea la cita y la otra se rechaza con el código "Appointment.SlotUnavailable"
    Y el médico tiene una sola cita activa
