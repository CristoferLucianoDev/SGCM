# language: es
@Integration
Característica: Gestión de sesiones
  Como usuario, quiero que mi sesión se renueve sola y poder cerrarla,
  para no iniciar sesión a cada rato y mantener mi cuenta segura en cada dispositivo.

  Antecedentes:
    Dado que existe un paciente confirmado con el correo "paciente@correo.com" y la contraseña "Secreta123"

  Escenario: Renovar la sesión entrega un par de tokens nuevo e invalida el refresh token anterior
    Dado que inicié sesión en el dispositivo "A" con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Cuando renuevo la sesión del dispositivo "A"
    Entonces recibo un par de tokens nuevo para el dispositivo "A"
    Cuando presento el refresh token anterior del dispositivo "A"
    Entonces la renovación es rechazada
    Y la sesión del dispositivo "A" sigue activa

  Escenario: Presentar un refresh token ya rotado revoca todas las sesiones
    Dado que inicié sesión en el dispositivo "A" con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y que inicié sesión en el dispositivo "B" con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y que renové la sesión del dispositivo "A"
    Y que pasó el margen de gracia de la renovación
    Cuando presento el refresh token anterior del dispositivo "A"
    Entonces la renovación es rechazada
    Y todas las sesiones de "paciente@correo.com" quedaron revocadas por "reutilización del refresh token"

  Escenario: Cerrar sesión en un dispositivo revoca solo esa sesión
    Dado que inicié sesión en el dispositivo "A" con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y que inicié sesión en el dispositivo "B" con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Cuando cierro la sesión del dispositivo "A"
    Entonces el cierre de sesión es aceptado
    Y la sesión del dispositivo "A" quedó revocada por "cierre de sesión"
    Y la sesión del dispositivo "B" sigue activa

  Escenario: Al iniciar sesión en un sexto dispositivo se revoca la sesión usada hace más tiempo
    Dado que inicié sesión en 5 dispositivos con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Y que renové la sesión del dispositivo "1"
    Cuando inicio sesión en el dispositivo "6" con el correo "paciente@correo.com" y la contraseña "Secreta123"
    Entonces la sesión del dispositivo "2" quedó revocada por "límite de sesiones"
    Y la sesión del dispositivo "1" sigue activa
    Y la sesión del dispositivo "6" sigue activa
    Y "paciente@correo.com" tiene 5 sesiones activas
