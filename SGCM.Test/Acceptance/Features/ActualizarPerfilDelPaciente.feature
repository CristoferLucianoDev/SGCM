# language: es
@Integration @Patients
Característica: Actualizar el perfil del paciente
  Como paciente, quiero actualizar mis datos de contacto, emergencia y seguro,
  para que mi información esté al día.

  Antecedentes:
    Dado que existe un paciente confirmado con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y que inicié sesión con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y que existe el proveedor de seguros activo "Seguros Universal"
    Y que completé mi perfil con el proveedor "Seguros Universal"

  Escenario: El paciente actualiza su teléfono, su dirección y su contacto de emergencia
    Cuando actualizo mi contacto conservando el proveedor "Seguros Universal"
    Entonces la actualización es aceptada
    Y mi perfil tiene los datos de contacto nuevos

  Escenario: El paciente cambia de proveedor de seguros
    Dado que existe el proveedor de seguros activo "Seguros Humano"
    Cuando actualizo mi perfil con el proveedor "Seguros Humano"
    Entonces la actualización es aceptada
    Y mi perfil queda con el proveedor "Seguros Humano" y la póliza nueva

  Escenario: El paciente quita su seguro
    Cuando quito mi seguro
    Entonces la actualización es aceptada
    Y mi perfil queda sin proveedor ni póliza y conserva mis datos de contacto

  Escenario: El paciente conserva un proveedor que ya fue desactivado
    Dado que el proveedor de seguros "Seguros Universal" fue desactivado
    Cuando actualizo mi contacto conservando el proveedor "Seguros Universal"
    Entonces la actualización es aceptada
    Y mi perfil tiene los datos de contacto nuevos

  Escenario: No se puede cambiar a un proveedor inactivo
    Dado que existe el proveedor de seguros inactivo "Seguros Cerrados"
    Cuando actualizo mi perfil con el proveedor "Seguros Cerrados"
    Entonces la actualización es rechazada con el error "Patient.InvalidInsuranceProvider"
    Y mi perfil no cambió

  Escenario: Elegir un proveedor exige el número de póliza
    Dado que existe el proveedor de seguros activo "Seguros Humano"
    Cuando actualizo mi perfil con el proveedor "Seguros Humano" y sin número de póliza
    Entonces la actualización es rechazada con el error "Patient.PolicyNumberRequired"
    Y mi perfil no cambió

  Esquema del escenario: Un dato inválido impide guardar la actualización
    Cuando actualizo mi perfil con <dato>
    Entonces la actualización es rechazada con el error "<código>"
    Y mi perfil no cambió

    Ejemplos:
      | dato                                   | código                             |
      | el teléfono vacío                      | Patient.PhoneRequired              |
      | un teléfono de 21 caracteres           | Patient.PhoneTooLong               |
      | una dirección de 251 caracteres        | Patient.AddressTooLong             |
      | un contacto de emergencia sin teléfono | Patient.IncompleteEmergencyContact |

  Escenario: Nombre, apellido y fecha de nacimiento no se pueden cambiar
    Cuando intento cambiar mi nombre, mi apellido y mi fecha de nacimiento
    Entonces mi nombre, mi apellido y mi fecha de nacimiento no cambiaron

  Escenario: Un paciente no puede modificar el perfil de otro
    Dado que existe un paciente confirmado con el correo "otro@correo.com" y la contraseña "Secreta123"
    Y que "otro@correo.com" completó su perfil de paciente
    Cuando actualizo mi contacto conservando el proveedor "Seguros Universal"
    Entonces la actualización es aceptada
    Y el perfil de "otro@correo.com" no cambió

  Escenario: El paciente cambia su contraseña y se revocan sus demás sesiones
    Dado que también inicié sesión en otro dispositivo con la contraseña "Secreta123"
    Cuando cambio mi contraseña de "Secreta123" a "NuevaClave456"
    Entonces el cambio de contraseña es aceptado
    Y mi sesión actual sigue activa
    Y la sesión del otro dispositivo fue revocada
    Y puedo iniciar sesión con la contraseña "NuevaClave456"
    Y no puedo iniciar sesión con la contraseña "Secreta123"

  Escenario: Una contraseña actual incorrecta rechaza el cambio
    Dado que también inicié sesión en otro dispositivo con la contraseña "Secreta123"
    Cuando cambio mi contraseña de "Equivocada1" a "NuevaClave456"
    Entonces el cambio de contraseña es rechazado con el error "User.CurrentPasswordIncorrect"
    Y la sesión del otro dispositivo sigue activa
    Y puedo iniciar sesión con la contraseña "Secreta123"

  Esquema del escenario: Una contraseña nueva inválida rechaza el cambio
    Cuando cambio mi contraseña de "Secreta123" a "<nueva>"
    Entonces el cambio de contraseña es rechazado con el error "<código>"
    Y puedo iniciar sesión con la contraseña "Secreta123"

    Ejemplos:
      | nueva      | código                        |
      | Corta12    | User.PasswordTooShort         |
      | Secreta123 | User.NewPasswordSameAsCurrent |
