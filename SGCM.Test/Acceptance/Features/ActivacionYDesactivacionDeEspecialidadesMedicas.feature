# language: es
@Integration @Specialties @SpecialtyActivation
Característica: Activación y desactivación de especialidades médicas
  Como Administrator quiero cambiar el estado de una especialidad sin perder sus datos
  ni las asociaciones de los médicos, respetando su visibilidad pública.

  Esquema del escenario: Cambiar el estado conserva identidad y datos en PostgreSQL
    Dado que estoy autenticado para cambiar el estado de especialidades como "Administrator"
    Y que preparo una especialidad "<inicial>" para activar o desactivar
    Cuando solicito "<operación>" para la especialidad preparada
    Entonces la API de especialidades responde HTTP 204
    Y la especialidad preparada queda "<final>" en PostgreSQL conservando sus datos

    Ejemplos:
      | inicial  | operación  | final    |
      | activa   | desactivar | inactiva |
      | inactiva | reactivar  | activa   |

  Esquema del escenario: Repetir la operación devuelve conflicto sin cambios
    Dado que estoy autenticado para cambiar el estado de especialidades como "Administrator"
    Y que preparo una especialidad "<inicial>" para activar o desactivar
    Cuando solicito "<operación>" para la especialidad preparada
    Entonces la API de especialidades responde HTTP 204
    Y la especialidad preparada queda "<final>" en PostgreSQL conservando sus datos
    Cuando solicito "<operación>" para la especialidad preparada
    Entonces la API de especialidades responde HTTP 409
    Y el error de cambio de estado tiene código "<código>"
    Y el intento de cambio de estado no altera datos ni relaciones

    Ejemplos:
      | inicial  | operación  | final    | código                    |
      | activa   | desactivar | inactiva | Specialty.AlreadyInactive |
      | inactiva | reactivar  | activa   | Specialty.AlreadyActive   |

  Esquema del escenario: Patient y Doctor sin pasos pendientes no pueden cambiar el estado
    Dado que estoy autenticado para cambiar el estado de especialidades como "<rol>"
    Y que preparo una especialidad "<inicial>" para activar o desactivar
    Cuando solicito "<operación>" para la especialidad preparada
    Entonces la API de especialidades responde HTTP 403
    Y el intento de cambio de estado no altera datos ni relaciones

    Ejemplos:
      | rol     | inicial  | operación  |
      | Patient | activa   | desactivar |
      | Doctor  | activa   | desactivar |
      | Patient | inactiva | reactivar  |
      | Doctor  | inactiva | reactivar  |

  Esquema del escenario: Una persona anónima no puede cambiar el estado
    Dado que no estoy autenticado para cambiar el estado de especialidades
    Y que preparo una especialidad "<inicial>" para activar o desactivar
    Cuando solicito "<operación>" para la especialidad preparada
    Entonces la API de especialidades responde HTTP 401
    Y el intento de cambio de estado no altera datos ni relaciones

    Ejemplos:
      | inicial  | operación  |
      | activa   | desactivar |
      | inactiva | reactivar  |

  Escenario: Dos médicos conservan las asociaciones y el directorio filtra solo la especialidad
    Dado que estoy autenticado para cambiar el estado de especialidades como "Administrator"
    Y que preparo una especialidad "activa" para activar o desactivar
    Y que dos médicos tienen esa especialidad y una asociación de control
    Entonces el directorio médico muestra la especialidad preparada "activa" y conserva la de control
    Cuando solicito "desactivar" para la especialidad preparada
    Entonces la API de especialidades responde HTTP 204
    Y la especialidad preparada queda "inactiva" en PostgreSQL conservando sus datos
    Y los dos médicos y sus asociaciones permanecen intactos
    Y el directorio médico muestra la especialidad preparada "inactiva" y conserva la de control
    Cuando solicito "reactivar" para la especialidad preparada
    Entonces la API de especialidades responde HTTP 204
    Y la especialidad preparada queda "activa" en PostgreSQL conservando sus datos
    Y los dos médicos y sus asociaciones permanecen intactos
    Y el directorio médico muestra la especialidad preparada "activa" y conserva la de control

  Escenario: El catálogo y la búsqueda públicos excluyen y recuperan el mismo registro
    Dado que estoy autenticado para cambiar el estado de especialidades como "Administrator"
    Y que preparo una especialidad "activa" para activar o desactivar
    Y que preparo una especialidad de control para las consultas
    Entonces el catálogo y la búsqueda reflejan la especialidad preparada "activa" según el consumidor
    Cuando solicito "desactivar" para la especialidad preparada
    Entonces la API de especialidades responde HTTP 204
    Y la especialidad preparada queda "inactiva" en PostgreSQL conservando sus datos
    Y el catálogo y la búsqueda reflejan la especialidad preparada "inactiva" según el consumidor
    Cuando solicito "reactivar" para la especialidad preparada
    Entonces la API de especialidades responde HTTP 204
    Y la especialidad preparada queda "activa" en PostgreSQL conservando sus datos
    Y el catálogo y la búsqueda reflejan la especialidad preparada "activa" según el consumidor
