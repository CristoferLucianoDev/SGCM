# language: es
@Integration
Característica: Recuperación de contraseña
  Como usuario que olvidó su contraseña, quiero restablecerla desde mi correo,
  para recuperar el acceso a mi cuenta.

  Escenario: Una cuenta confirmada recibe el enlace de recuperación
    Dado que existe un paciente confirmado con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Cuando solicito recuperar la contraseña de "paciente@correo.com"
    Entonces la solicitud de recuperación es aceptada
    Y "paciente@correo.com" recibe un enlace de recuperación

  Escenario: Un correo inexistente recibe la misma respuesta sin enlace
    Cuando solicito recuperar la contraseña de "nadie@correo.com"
    Entonces la solicitud de recuperación es aceptada
    Y "nadie@correo.com" no recibe ningún enlace de recuperación

  Escenario: Un registro sin confirmar recibe la misma respuesta sin enlace
    Dado que me registré con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Cuando solicito recuperar la contraseña de "paciente@correo.com"
    Entonces la solicitud de recuperación es aceptada
    Y "paciente@correo.com" no recibe ningún enlace de recuperación

  Escenario: Una cuenta inactiva recibe la misma respuesta sin enlace
    Dado que existe un paciente confirmado con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y que la cuenta de "paciente@correo.com" está desactivada
    Cuando solicito recuperar la contraseña de "paciente@correo.com"
    Entonces la solicitud de recuperación es aceptada
    Y "paciente@correo.com" no recibe ningún enlace de recuperación

  Escenario: Un enlace vigente permite establecer una contraseña nueva
    Dado que existe un paciente confirmado con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y que solicité recuperar la contraseña de "paciente@correo.com"
    Cuando restablezco la contraseña con el último enlace de "paciente@correo.com" y la contraseña "NuevaClave456"
    Entonces la contraseña se restablece
    Cuando inicio sesión con el correo "paciente@correo.com" y la contraseña "NuevaClave456"
    Entonces el inicio de sesión es aceptado y recibo los tokens
    Cuando inicio sesión con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Entonces el inicio de sesión es rechazado con el error "User.InvalidCredentials"

  Escenario: Restablecer la contraseña cierra todas las sesiones
    Dado que existe un paciente confirmado con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y que inicié sesión 2 veces con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y que solicité recuperar la contraseña de "paciente@correo.com"
    Cuando restablezco la contraseña con el último enlace de "paciente@correo.com" y la contraseña "NuevaClave456"
    Entonces la contraseña se restablece
    Y todas las sesiones de "paciente@correo.com" quedan cerradas

  Escenario: Una contraseña de menos de 8 caracteres se rechaza
    Dado que existe un paciente confirmado con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y que solicité recuperar la contraseña de "paciente@correo.com"
    Cuando restablezco la contraseña con el último enlace de "paciente@correo.com" y la contraseña "Corta12"
    Entonces el restablecimiento es rechazado con el error "User.PasswordTooShort"

  Escenario: Un enlace con más de 10 minutos se rechaza
    Dado que existe un paciente confirmado con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y que solicité recuperar la contraseña de "paciente@correo.com"
    Y pasan 11 minutos
    Cuando restablezco la contraseña con el último enlace de "paciente@correo.com" y la contraseña "NuevaClave456"
    Entonces el restablecimiento es rechazado con el error "User.TokenExpired"

  Escenario: Un enlace ya usado se rechaza
    Dado que existe un paciente confirmado con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y que solicité recuperar la contraseña de "paciente@correo.com"
    Y que restablecí la contraseña con el último enlace de "paciente@correo.com" y la contraseña "NuevaClave456"
    Cuando restablezco la contraseña con el último enlace de "paciente@correo.com" y la contraseña "OtraClave789"
    Entonces el restablecimiento es rechazado con el error "User.TokenInvalid"

  Escenario: Un enlace reemplazado por una solicitud nueva se rechaza
    Dado que existe un paciente confirmado con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y que solicité recuperar la contraseña de "paciente@correo.com"
    Y pasan 2 minutos
    Y que solicité recuperar la contraseña de "paciente@correo.com"
    Cuando restablezco la contraseña con el primer enlace de "paciente@correo.com" y la contraseña "NuevaClave456"
    Entonces el restablecimiento es rechazado con el error "User.TokenInvalid"

  Escenario: Superado el límite de solicitudes por correo la solicitud se rechaza temporalmente
    Dado que existe un paciente confirmado con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y que solicité 3 veces recuperar la contraseña de "paciente@correo.com"
    Cuando solicito recuperar la contraseña de "paciente@correo.com"
    Entonces la solicitud de recuperación es rechazada con el error "User.TooManyPasswordResetRequests"
    Cuando pasan 15 minutos
    Y solicito recuperar la contraseña de "paciente@correo.com"
    Entonces la solicitud de recuperación es aceptada
