# language: es
@Integration
Característica: Confirmación de cuenta por correo
  Como paciente recién registrado, quiero confirmar mi correo con un enlace,
  para completar mi registro y poder iniciar sesión.

  Escenario: Abrir un enlace vigente confirma la cuenta
    Dado que me registré con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Cuando abro el último enlace de confirmación de "paciente@correo.com"
    Entonces la confirmación es aceptada
    Y la cuenta de "paciente@correo.com" está confirmada
    Y el último enlace de confirmación de "paciente@correo.com" queda marcado como usado

  Escenario: Abrir un enlace vencido indica que debe registrarse de nuevo
    Dado que me registré con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y pasan 15 minutos
    Cuando abro el último enlace de confirmación de "paciente@correo.com"
    Entonces la confirmación es rechazada con el error "User.RegistrationExpired"
    Y la cuenta de "paciente@correo.com" sigue sin confirmar

  Escenario: Un enlace ya usado es rechazado
    Dado que me registré con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y que abrí el último enlace de confirmación de "paciente@correo.com"
    Cuando abro el último enlace de confirmación de "paciente@correo.com"
    Entonces la confirmación es rechazada con el error "User.TokenInvalid"

  Escenario: Un enlace reemplazado por un nuevo registro es rechazado
    Dado que me registré con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y que me registré con el correo "paciente@correo.com" y la contraseña "OtraClave456"
    Cuando abro el primer enlace de confirmación de "paciente@correo.com"
    Entonces la confirmación es rechazada con el error "User.TokenInvalid"
    Y la cuenta de "paciente@correo.com" sigue sin confirmar

  Escenario: La limpieza borra el registro vencido y deja el correo libre
    Dado que me registré con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y pasan 15 minutos
    Cuando se ejecuta la limpieza de registros pendientes
    Entonces no existe una cuenta para "paciente@correo.com"
    Cuando me registro con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Entonces el registro es aceptado

  Escenario: La limpieza nunca borra un registro cuyo último enlace sigue vigente
    Dado que me registré con el correo "vencido@correo.com" y la contraseña "Secreta123"
    Y que me registré con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y pasan 10 minutos
    Y que me registré con el correo "paciente@correo.com" y la contraseña "OtraClave456"
    Y pasan 5 minutos
    Cuando se ejecuta la limpieza de registros pendientes
    Entonces no existe una cuenta para "vencido@correo.com"
    Y existe una cuenta pendiente de confirmación con rol Paciente para "paciente@correo.com"
