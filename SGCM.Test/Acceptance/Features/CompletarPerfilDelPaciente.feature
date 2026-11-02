# language: es
@Integration @Patients
Característica: Completar el perfil del paciente
  Como paciente que confirmó su correo, quiero completar mi perfil,
  para poder agendar citas.

  Antecedentes:
    Dado que existe un paciente confirmado con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y que inicié sesión con el correo "paciente@correo.com" y la contraseña "Secreta123"

  Escenario: El paciente completa su perfil con los datos obligatorios y deja de tener paso pendiente
    Cuando envío mi perfil con solo los datos obligatorios
    Entonces mi perfil es aceptado y recibo tokens sin paso pendiente
    Y mi perfil quedó guardado sin dirección, contacto de emergencia ni seguro

  Escenario: El paciente completa su perfil con un proveedor de seguros activo
    Dado que existe el proveedor de seguros activo "Seguros Universal"
    Cuando envío mi perfil con todos los datos y el proveedor "Seguros Universal"
    Entonces mi perfil es aceptado y recibo tokens sin paso pendiente
    Y mi perfil quedó guardado con el proveedor "Seguros Universal" y su número de póliza

  Escenario: Con el paso pendiente no puede usar otra función hasta completar su perfil
    Cuando intento usar otra función de la app
    Entonces la otra función responde HTTP 403
    Cuando envío mi perfil con solo los datos obligatorios
    Y intento usar otra función de la app con los tokens nuevos
    Entonces la otra función responde HTTP 204

  Esquema del escenario: Un dato obligatorio ausente impide guardar el perfil
    Cuando envío mi perfil sin <campo>
    Entonces mi perfil es rechazado con el error "<código>"
    Y no se guardó mi perfil

    Ejemplos:
      | campo                  | código                    |
      | el nombre              | Patient.FirstNameRequired |
      | el apellido            | Patient.LastNameRequired  |
      | el teléfono            | Patient.PhoneRequired     |
      | la fecha de nacimiento | Patient.InvalidBirthDate  |

  Escenario: Una fecha de nacimiento futura impide guardar el perfil
    Cuando envío mi perfil con fecha de nacimiento de mañana
    Entonces mi perfil es rechazado con el error "Patient.InvalidBirthDate"
    Y no se guardó mi perfil

  Esquema del escenario: Un campo que supera su longitud impide guardar el perfil
    Cuando envío mi perfil con <campo> de <longitud> caracteres
    Entonces mi perfil es rechazado con el error "<código>"
    Y no se guardó mi perfil

    Ejemplos:
      | campo        | longitud | código                   |
      | el nombre    | 101      | Patient.FirstNameTooLong |
      | el apellido  | 101      | Patient.LastNameTooLong  |
      | el teléfono  | 21       | Patient.PhoneTooLong     |
      | la dirección | 251      | Patient.AddressTooLong   |

  Escenario: Elegir un proveedor activo exige el número de póliza
    Dado que existe el proveedor de seguros activo "Seguros Universal"
    Cuando envío mi perfil con el proveedor "Seguros Universal" y sin número de póliza
    Entonces mi perfil es rechazado con el error "Patient.PolicyNumberRequired"
    Y no se guardó mi perfil

  Escenario: Un proveedor inactivo no se puede elegir
    Dado que existe el proveedor de seguros inactivo "Seguros Cerrados"
    Cuando envío mi perfil con todos los datos y el proveedor "Seguros Cerrados"
    Entonces mi perfil es rechazado con el error "Patient.InvalidInsuranceProvider"
    Y no se guardó mi perfil

  Escenario: Un perfil ya completado no se puede volver a crear
    Dado que ya completé mi perfil
    Cuando envío mi perfil con solo los datos obligatorios
    Entonces mi perfil es rechazado con el error "Patient.ProfileAlreadyExists"
