# language: es
@Integration @MedicalHistory
Característica: Consultar historial médico
  Como paciente, quiero ver el registro clínico de mis citas, y como médico, el historial de los pacientes que
  atiendo, para dar continuidad a la atención. Cada consulta queda en el registro de accesos.

  Escenario: Como paciente veo la versión vigente del registro de cada una de mis citas
    Dado que inicié sesión como paciente para consultar mi historial médico
    Y que el paciente tuvo una cita completada hace 3 días con el diagnóstico "Faringitis" corregido a "Amigdalitis"
    Y que el paciente tuvo una cita completada hace 10 días sin registro clínico
    Cuando consulto mi historial médico
    Entonces el historial médico tiene 2 citas
    Y la cita de hace 3 días muestra el diagnóstico vigente "Amigdalitis" sin versiones anteriores
    Y la cita de hace 10 días indica que no tiene registro clínico
    Y el registro de accesos tiene 1 consulta a mi nombre

  Escenario: Como médico tratante veo también las versiones anteriores de las citas que atendí
    Dado que inicié sesión como médico para consultar historiales médicos
    Y que atendí al paciente hace 3 días con el diagnóstico "Faringitis" corregido a "Amigdalitis"
    Y que el paciente tuvo una cita completada hace 10 días con el diagnóstico "Gripe" corregido a "Influenza"
    Cuando consulto el historial médico del paciente
    Entonces el historial médico tiene 2 citas
    Y la cita de hace 3 días muestra el diagnóstico vigente "Amigdalitis" con la versión anterior "Faringitis"
    Y la cita de hace 10 días muestra el diagnóstico vigente "Influenza" sin versiones anteriores
    Y el registro de accesos tiene 3 consultas a mi nombre

  Escenario: Un médico sin citas con el paciente no puede consultar su historial
    Dado que inicié sesión como médico para consultar historiales médicos
    Y que el paciente tuvo una cita completada hace 3 días con el diagnóstico "Faringitis" corregido a "Amigdalitis"
    Cuando consulto el historial médico del paciente
    Entonces la consulta del historial se rechaza con el estado 403 y el código "MedicalHistory.NoAppointmentWithPatient"
    Y el registro de accesos tiene 0 consultas a mi nombre
