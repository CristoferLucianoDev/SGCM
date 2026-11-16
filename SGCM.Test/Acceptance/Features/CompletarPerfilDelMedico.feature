# language: es
@Integration @DoctorProfile
Característica: Completar el perfil del médico en el primer acceso
  Como médico con una cuenta nueva, quiero registrar mis datos profesionales
  para poder utilizar las funciones de mi cuenta en los siguientes inicios de sesión.

  Esquema del escenario: El médico completa su perfil y recupera el acceso en el siguiente inicio de sesión
    Dado que tengo una cuenta médica nueva y datos profesionales válidos
    Cuando inicio mi primer acceso médico desde web
    Entonces debo cambiar mi contraseña temporal
    Cuando establezco mi contraseña definitiva para el primer acceso
    Entonces debo completar mi perfil médico
    Cuando envío los datos de mi perfil médico
    Entonces mi perfil médico queda registrado y puedo continuar
    Cuando cierro la sesión que recibí al completar el perfil médico
    Y vuelvo a iniciar sesión como médico desde "<cliente>"
    Entonces puedo continuar sin pasos pendientes
    Cuando asocio una segunda especialidad con mi nueva sesión
    Entonces ambas especialidades quedan asociadas a mi perfil médico

    Ejemplos:
      | cliente |
      | web     |
      | mobile  |

  Escenario: El médico selecciona un municipio de otra provincia
    Dado que tengo pendiente completar mi perfil médico
    Cuando envío mi perfil médico con un municipio de otra provincia
    Entonces se rechaza el perfil médico por la ubicación seleccionada
    Y puedo renovar mi sesión y aún debo completar mi perfil médico

  Escenario: Otro médico ya tiene registrada la licencia profesional
    Dado que tengo pendiente completar mi perfil médico
    Y otro médico ya tiene registrada la licencia que voy a enviar
    Cuando envío los datos de mi perfil médico
    Entonces se rechaza mi licencia duplicada y se conserva el perfil del titular
    Y puedo renovar mi sesión y aún debo completar mi perfil médico
