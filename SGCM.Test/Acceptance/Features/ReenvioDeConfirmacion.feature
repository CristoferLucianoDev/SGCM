# language: es
@Integration
Característica: Reenvío del correo de confirmación
  Como paciente que no recibió el correo, quiero pedir que me lo reenvíen,
  para confirmar mi cuenta sin registrarme otra vez.

  Escenario: El reenvío emite un enlace nuevo e invalida el anterior
    Dado que me registré con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y pasan 30 segundos
    Cuando pido reenviar el correo de confirmación a "paciente@correo.com"
    Entonces el reenvío es aceptado
    Y se enviaron 2 correos de confirmación a "paciente@correo.com"
    Cuando abro el primer enlace de confirmación de "paciente@correo.com"
    Entonces la confirmación es rechazada con el error "User.TokenInvalid"
    Cuando abro el último enlace de confirmación de "paciente@correo.com"
    Entonces la confirmación es aceptada

  Escenario: El enlace reenviado vence a los 15 minutos
    Dado que me registré con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y pasan 30 segundos
    Y que pedí reenviar el correo de confirmación a "paciente@correo.com"
    Y pasan 15 minutos
    Cuando abro el último enlace de confirmación de "paciente@correo.com"
    Entonces la confirmación es rechazada con el error "User.RegistrationExpired"

  Escenario: Pedir un reenvío antes de los 30 segundos se rechaza
    Dado que me registré con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y pasan 29 segundos
    Cuando pido reenviar el correo de confirmación a "paciente@correo.com"
    Entonces el reenvío es rechazado con el error "User.ResendTooSoon"
    Y se enviaron 1 correos de confirmación a "paciente@correo.com"

  Escenario: Pedir un reenvío con el último enlace vencido indica registrarse de nuevo
    Dado que me registré con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y pasan 15 minutos
    Cuando pido reenviar el correo de confirmación a "paciente@correo.com"
    Entonces el reenvío es rechazado con el error "User.RegistrationExpired"
    Y se enviaron 1 correos de confirmación a "paciente@correo.com"

  Escenario: La limpieza no borra una cuenta que recibió un enlace nuevo
    Dado que me registré con el correo "vencido@correo.com" y la contraseña "Secreta123"
    Y que me registré con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y pasan 14 minutos
    Y que pedí reenviar el correo de confirmación a "paciente@correo.com"
    Y pasan 2 minutos
    Cuando se ejecuta la limpieza de registros pendientes
    Entonces no existe una cuenta para "vencido@correo.com"
    Y existe una cuenta pendiente de confirmación con rol Paciente para "paciente@correo.com"
