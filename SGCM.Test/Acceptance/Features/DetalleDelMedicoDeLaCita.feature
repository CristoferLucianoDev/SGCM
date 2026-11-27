# language: es
@Integration @Patients
Característica: Visualizar los detalles del doctor asignado
  Como paciente, quiero ver en el detalle de mi cita el perfil del médico que me atenderá, para saber quién es y
  dónde consulta; solo puedo ver mis propias citas.

  Escenario: El detalle de mi cita muestra el perfil público del médico y la especialidad de la cita
    Dado que inicié sesión como paciente para ver el médico de mi cita
    Y que tengo una cita "pending" con el médico asignado dentro de 3 días
    Cuando abro el detalle de mi cita con el médico asignado
    Entonces veo el perfil público del médico, igual al de su perfil
    Y veo la especialidad de la cita

  Escenario: No puedo ver el detalle de la cita de otro paciente
    Dado que inicié sesión como paciente para ver el médico de mi cita
    Y que otro paciente tiene una cita con el médico asignado dentro de 3 días
    Cuando abro el detalle de la cita del otro paciente con el médico asignado
    Entonces el detalle de la cita no es accesible

  Escenario: En una cita pasada con un médico desactivado veo su nombre y la especialidad sin error
    Dado que inicié sesión como paciente para ver el médico de mi cita
    Y que tuve una cita "completed" con el médico asignado hace 5 días
    Y que el médico asignado fue desactivado
    Cuando abro el detalle de mi cita con el médico asignado
    Entonces veo el nombre "Luis Pérez" del médico y la especialidad de la cita, sin su perfil público
