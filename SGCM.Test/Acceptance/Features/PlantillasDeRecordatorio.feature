# language: es
@Integration @Notifications
Característica: Crear recordatorios personalizados
  Como médico, quiero crear, editar y desactivar mis propias plantillas de recordatorio, para reutilizar mis mensajes;
  las globales las uso pero no las cambio, y editar una plantilla no cambia los recordatorios ya programados.

  Escenario: El médico crea, edita y desactiva su plantilla
    Dado que inicié sesión como médico para gestionar plantillas
    Cuando creo la plantilla "Indicaciones" con el asunto "Su cita de {SpecialtyName}" y el cuerpo "Hola {PatientName}, traiga sus estudios."
    Entonces mi plantilla aparece activa en mi lista
    Cuando edito mi plantilla con el cuerpo "Hola {PatientName}, venga en ayunas."
    Entonces mi plantilla tiene el cuerpo "Hola {PatientName}, venga en ayunas."
    Cuando desactivo mi plantilla
    Entonces mi plantilla ya no aparece en mi lista

  Escenario: Una variable no permitida se rechaza indicándola
    Dado que inicié sesión como médico para gestionar plantillas
    Cuando creo la plantilla "Indicaciones" con el asunto "Asunto" y el cuerpo "Hola {Paciente}, su cita es el {Fecha}."
    Entonces la operación de plantilla se rechaza con el estado 400 y el código "ReminderTemplate.UnknownVariable"
    Y el mensaje indica la variable "{Paciente}"

  Escenario: Las plantillas globales se ven pero no se editan ni se desactivan
    Dado que inicié sesión como médico para gestionar plantillas
    Entonces la plantilla global "Recordatorio de cita" aparece en mi lista como de solo lectura
    Cuando edito la plantilla global "Recordatorio de cita"
    Entonces la operación de plantilla se rechaza con el estado 403 y el código "ReminderTemplate.GlobalReadOnly"
    Cuando desactivo la plantilla global "Recordatorio de cita"
    Entonces la operación de plantilla se rechaza con el estado 403 y el código "ReminderTemplate.GlobalReadOnly"

  Escenario: La plantilla de otro médico no se ve ni se edita
    Dado que inicié sesión como médico para gestionar plantillas
    Y que otro médico tiene la plantilla "Ajena"
    Entonces la plantilla de otro médico no aparece en mi lista
    Cuando edito la plantilla de otro médico
    Entonces la operación de plantilla se rechaza con el estado 404 y el código "ReminderTemplate.NotFound"

  Escenario: Editar una plantilla no cambia los recordatorios ya programados
    Dado que inicié sesión como médico para gestionar plantillas
    Y que creé la plantilla "Indicaciones" con el asunto "Indicaciones" y el cuerpo "Hola {PatientName}, traiga sus estudios."
    Y que programé un recordatorio con mi plantilla para una cita confirmada
    Cuando edito mi plantilla con el cuerpo "Hola {PatientName}, texto nuevo."
    Entonces el recordatorio programado conserva el texto "Hola Ana Pérez, traiga sus estudios."
