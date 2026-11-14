# language: es
@Integration @TemporaryPassword
Característica: Primer acceso del médico con contraseña temporal

  Escenario: El médico con perfil recupera el acceso al cambiar la contraseña desde web
    Dado que tengo un perfil médico, una contraseña temporal y una especialidad activa disponible para asociar
    Cuando inicio sesión desde web con mi contraseña temporal
    Entonces el paso pendiente es "change_password"
    Cuando intento asociar la especialidad
    Entonces la API responde HTTP 403 y la asociación sigue sin existir
    Cuando establezco una contraseña distinta de exactamente 8 caracteres
    Entonces recibo credenciales nuevas sin paso pendiente
    Cuando intento asociar la especialidad con el JWT anterior
    Entonces la API responde HTTP 403 y la asociación sigue sin existir
    Cuando vuelvo a solicitar la misma asociación con el JWT nuevo
    Entonces la API responde HTTP 204 y la asociación queda guardada
    Y la contraseña temporal ya no permite iniciar sesión

  Escenario: El médico sin perfil empieza desde móvil y continúa desde web
    Dado que tengo una contraseña temporal y todavía no tengo perfil médico
    Cuando inicio sesión desde móvil con mi contraseña temporal
    Entonces recibo la indicación de continuar desde web
    Y el paso pendiente es "change_password"
    Cuando inicio sesión desde web y cambio mi contraseña temporal
    Entonces las credenciales nuevas indican el paso pendiente "doctor_profile"
