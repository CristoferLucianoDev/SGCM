# language: es
@Integration @DoctorSpecialties
Característica: Asociación y desasociación de especialidades de doctores
  El médico administra sus propias especialidades conservando al menos una,
  sin alterar otros perfiles, el catálogo ni las citas ya agendadas.

  Antecedentes:
    Dado que preparo dos médicos independientes para gestionar sus especialidades

  Esquema del escenario: El médico modifica una asociación propia
    Dado que la especialidad objetivo del médico es "<objetivo>"
    Y que inicio sesión para gestionar asociaciones como "Doctor"
    Cuando solicito "<operación>" la especialidad del perfil médico
    Entonces la API de especialidades responde HTTP 204
    Y PostgreSQL refleja únicamente la operación médica "<operación>"

    Ejemplos:
      | operación   | objetivo |
      | asociar     | nueva    |
      | desasociar  | asociada |

  Esquema del escenario: Una operación rechazada conserva todos los datos
    Dado que el perfil de Doctor A conserva "<cantidad>" especialidades
    Y que la especialidad objetivo del médico es "<objetivo>"
    Y que inicio sesión para gestionar asociaciones como "Doctor"
    Cuando solicito "<operación>" la especialidad del perfil médico
    Entonces la API de especialidades responde HTTP <estado>
    Y el ProblemDetails de asociaciones tiene código "<código>"
    Y los perfiles médicos y todos sus datos persistidos permanecen intactos

    Ejemplos:
      | operación  | cantidad | objetivo    | estado | código                          |
      | asociar    | dos      | asociada    | 409    | Doctor.SpecialtyAlreadyAssociated |
      | asociar    | dos      | inexistente | 404    | Specialty.NotFound              |
      | asociar    | dos      | inactiva    | 409    | Specialty.Inactive              |
      | asociar    | dos      | cero        | 400    | Specialty.InvalidId             |
      | desasociar | dos      | nueva       | 409    | Doctor.SpecialtyNotAssociated   |
      | desasociar | una      | asociada    | 409    | Doctor.CannotRemoveLastSpecialty |
      | desasociar | una      | nueva       | 409    | Doctor.SpecialtyNotAssociated   |
      | desasociar | dos      | negativo    | 400    | Specialty.InvalidId             |

  Escenario: Se puede retirar una especialidad inactiva conservando otra
    Dado que la especialidad objetivo del médico es "asociada inactiva"
    Y que inicio sesión para gestionar asociaciones como "Doctor"
    Cuando solicito "desasociar" la especialidad del perfil médico
    Entonces la API de especialidades responde HTTP 204
    Y PostgreSQL refleja únicamente la operación médica "desasociar"

  Esquema del escenario: El propietario procede del JWT aunque el cliente envíe IDs ajenos
    Dado que la especialidad objetivo del médico es "<objetivo>"
    Y que inicio sesión para gestionar asociaciones como "Doctor"
    Cuando solicito "<operación>" enviando los identificadores del otro médico
    Entonces la API de especialidades responde HTTP 204
    Y PostgreSQL refleja únicamente la operación médica "<operación>"

    Ejemplos:
      | operación  | objetivo |
      | asociar    | nueva    |
      | desasociar | asociada |

  Esquema del escenario: Otros roles no pueden gestionar asociaciones médicas
    Dado que la especialidad objetivo del médico es "asociada"
    Y que inicio sesión para gestionar asociaciones como "<rol>"
    Cuando solicito "<operación>" la especialidad del perfil médico
    Entonces la API de especialidades responde HTTP 403
    Y los perfiles médicos y todos sus datos persistidos permanecen intactos

    Ejemplos:
      | rol           | operación  |
      | Patient       | asociar    |
      | Patient       | desasociar |
      | Administrator | asociar    |
      | Administrator | desasociar |

  Esquema del escenario: Un usuario anónimo no puede gestionar asociaciones médicas
    Dado que la especialidad objetivo del médico es "asociada"
    Cuando solicito "<operación>" la especialidad del perfil médico
    Entonces la API de especialidades responde HTTP 401
    Y los perfiles médicos y todos sus datos persistidos permanecen intactos

    Ejemplos:
      | operación  |
      | asociar    |
      | desasociar |

  Esquema del escenario: Desasociar conserva íntegra una cita previamente agendada
    Dado que la especialidad objetivo del médico es "asociada"
    Y que existe una cita médica "<estado>" para esa especialidad
    Y que inicio sesión para gestionar asociaciones como "Doctor"
    Cuando solicito "desasociar" la especialidad del perfil médico
    Entonces la API de especialidades responde HTTP 204
    Y PostgreSQL refleja únicamente la operación médica "desasociar"
    Y la cita médica conserva exactamente todos sus campos persistidos

    Ejemplos:
      | estado    |
      | pending   |
      | confirmed |
