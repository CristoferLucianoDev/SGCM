# language: es
@Integration
Característica: Inicio de sesión
  Como usuario registrado, quiero iniciar sesión con mi correo y contraseña,
  para acceder a las funciones de mi rol.

  Escenario: Iniciar sesión con credenciales válidas entrega los tokens
    Dado que existe un paciente confirmado con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Cuando inicio sesión con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Entonces el inicio de sesión es aceptado y recibo los tokens

  Escenario: Un correo inexistente recibe la respuesta de credenciales inválidas
    Cuando inicio sesión con el correo "nadie@correo.com" y la contraseña "Secreta123"
    Entonces el inicio de sesión es rechazado con el error "User.InvalidCredentials"

  Escenario: Una contraseña incorrecta recibe la respuesta de credenciales inválidas
    Dado que existe un paciente confirmado con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Cuando inicio sesión con el correo "paciente@correo.com" y la contraseña "OtraClave456"
    Entonces el inicio de sesión es rechazado con el error "User.InvalidCredentials"

  Escenario: Un registro sin confirmar recibe la respuesta de credenciales inválidas
    Dado que me registré con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Cuando inicio sesión con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Entonces el inicio de sesión es rechazado con el error "User.InvalidCredentials"

  Escenario: Una cuenta inactiva recibe la respuesta de credenciales inválidas
    Dado que existe un paciente confirmado con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y que la cuenta de "paciente@correo.com" está desactivada
    Cuando inicio sesión con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Entonces el inicio de sesión es rechazado con el error "User.InvalidCredentials"

  Escenario: Con el segundo factor activo se pide el código antes de entregar los tokens
    Dado que existe un paciente confirmado con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y que "paciente@correo.com" tiene el segundo factor activo
    Cuando inicio sesión con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Entonces se pide el segundo factor sin entregar tokens

  Escenario: Un paciente sin perfil debe completarlo
    Dado que existe un paciente confirmado con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Cuando inicio sesión con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Entonces el paso pendiente es "patient_profile"

  Escenario: Un paciente con perfil no tiene pasos pendientes
    Dado que existe un paciente confirmado con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y que "paciente@correo.com" completó su perfil de paciente
    Cuando inicio sesión con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Entonces no hay paso pendiente

  Escenario: Un médico con contraseña temporal debe cambiarla
    Dado que existe un médico con contraseña temporal con el correo "medico@correo.com" y la contraseña "Temporal123"
    Cuando inicio sesión con el correo "medico@correo.com" y la contraseña "Temporal123"
    Entonces el paso pendiente es "change_password"

  Escenario: Un médico sin perfil debe completarlo
    Dado que existe un médico con el correo "medico@correo.com" y la contraseña "Secreta123"
    Cuando inicio sesión con el correo "medico@correo.com" y la contraseña "Secreta123"
    Entonces el paso pendiente es "doctor_profile"

  Escenario: Superado el límite de intentos por correo el inicio de sesión se rechaza temporalmente
    Dado que existe un paciente confirmado con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y que fallé 5 veces el inicio de sesión con el correo "paciente@correo.com"
    Cuando inicio sesión con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Entonces el inicio de sesión es rechazado con el error "User.TooManyLoginAttempts"
    Cuando pasan 15 minutos
    Y inicio sesión con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Entonces el inicio de sesión es aceptado y recibo los tokens
