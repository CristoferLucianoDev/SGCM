# language: es
@Integration @DoctorProfile
Característica: Actualizar el perfil del médico

  Antecedentes:
    Dado que tengo un perfil médico completo con especialidades asociadas
    Y que inicié sesión con mi cuenta médica

  Escenario: El médico actualiza sus datos profesionales y el directorio refleja el cambio
    Cuando guardo mis nuevos datos profesionales como médico
    Entonces se guardan mis datos y el directorio refleja de inmediato los cambios

  Escenario: Una ubicación incompatible conserva el perfil y la información del directorio
    Cuando intento guardar mis datos profesionales con un municipio de otra provincia
    Entonces se rechaza mi ubicación y el perfil y el directorio conservan sus datos
