# language: es
@Integration @DoctorPublicProfile
Característica: Visualizar información del doctor
  Como visitante o paciente
  Quiero consultar la ficha profesional de un médico
  Para decidir si agendo una cita

  Esquema del escenario: Consultar la ficha sin acceso a los datos privados
    Dado que se publica un perfil médico con especialidades activas e inactivas
    Y consulto fichas médicas como "<Solicitante>"
    Cuando solicito la ficha del primer médico
    Entonces recibo todos sus datos públicos y solo sus especialidades activas en orden
    Y la ficha pública omite los datos privados en todo el documento

    Ejemplos:
      | Solicitante |
      | Visitante   |
      | Patient     |
      | Doctor      |

  Escenario: Consultar los datos reservados con una sesión administrativa vigente
    Dado que se publica un perfil médico con especialidades activas e inactivas
    Y se publica otro perfil médico con datos distintos
    Y consulto fichas médicas como "Administrator"
    Cuando solicito la ficha del primer médico
    Entonces recibo su perfil completo con su propio teléfono y licencia
    Y un visitante consulta la misma ficha y recibe solo su parte pública

  Esquema del escenario: Consultar una ficha médica que no está disponible
    Dado un perfil médico "<Disponibilidad>"
    Y consulto fichas médicas como "Administrator"
    Cuando un visitante y el administrador solicitan esa misma ficha médica
    Entonces ambos reciben el aviso de perfil no encontrado sin datos del médico

    Ejemplos:
      | Disponibilidad |
      | inexistente    |
      | inactivo       |

  Escenario: Ver de inmediato los cambios de una ficha médica
    Dado que se publica un perfil médico con especialidades activas e inactivas
    Y un visitante consulta la ficha antes de la actualización
    Cuando el titular inicia sesión y guarda sus siete nuevos datos profesionales
    Entonces la actualización se confirma sin contenido
    Y el mismo visitante ve de inmediato los cambios en la ficha pública
