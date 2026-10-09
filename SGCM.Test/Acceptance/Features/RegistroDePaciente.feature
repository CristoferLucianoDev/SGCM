# language: es
@Integration
Característica: Registro de paciente
  Como paciente, quiero crear mi cuenta con mi correo y una contraseña,
  para empezar a usar el sistema de citas.

  Escenario: Registro con un correo sin cuenta
    Cuando me registro con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Entonces el registro es aceptado
    Y existe una cuenta pendiente de confirmación con rol Paciente para "paciente@correo.com"
    Y se enviaron 1 correos de confirmación a "paciente@correo.com"

  Escenario: Registro con el correo de una cuenta confirmada
    Dado que existe una cuenta confirmada con el correo "paciente@correo.com"
    Cuando me registro con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Entonces el registro es rechazado con el error "User.EmailAlreadyInUse"
    Y no se envió ningún correo

  Escenario: Registro con el correo de un registro pendiente
    Dado que me registré con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Cuando me registro con el correo "paciente@correo.com" y la contraseña "OtraClave456"
    Entonces el registro es aceptado
    Y solo el último enlace de confirmación de "paciente@correo.com" es válido
    Y se enviaron 2 correos de confirmación a "paciente@correo.com"

  Esquema del escenario: Registro con datos inválidos
    Cuando me registro con el correo "<correo>" y la contraseña "<contraseña>"
    Entonces el registro es rechazado con el error "<error>"
    Y no se envió ningún correo

    Ejemplos:
      | correo              | contraseña | error                 |
      | no-es-un-correo     | Secreta123 | Common.InvalidEmail   |
      | paciente@correo.com | corta      | User.PasswordTooShort |

  Escenario: Registro con un correo de más de 256 caracteres
    Cuando me registro con un correo de 257 caracteres
    Entonces el registro es rechazado con el error "Common.InvalidEmail"

  Escenario: Registro que indica otro rol
    Cuando me registro con el correo "paciente@correo.com", la contraseña "Secreta123" y el rol 3
    Entonces el registro es aceptado
    Y existe una cuenta pendiente de confirmación con rol Paciente para "paciente@correo.com"