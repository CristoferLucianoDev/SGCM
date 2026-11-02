# language: es
@Integration @Specialties @SpecialtyUpdating
Característica: Actualización de especialidades médicas
  Como Administrator, quiero actualizar el nombre y la descripción de las especialidades,
  conservando su identidad y estado y evitando nombres duplicados.

  Esquema del escenario: Actualizar campos presentes conserva los omitidos y permite especialidades inactivas
    Dado que estoy autenticado para actualizar especialidades como "Administrator"
    Y que preparo la especialidad "Neurología" "<estado>" para actualizarla
    Cuando actualizo los campos "<campos>" de la especialidad
    Entonces la API de especialidades responde HTTP 204
    Y la actualización persiste nombre "<nombre>" y descripción "<descripción>" conservando ID y estado

    Ejemplos:
      | estado   | campos      | nombre     | descripción            |
      | activa   | nombre      | Pediatría  | Atención neurológica.   |
      | activa   | descripción | Neurología | Atención  MÉDICA.       |
      | activa   | ambos       | Pediatría  | Atención  MÉDICA.       |
      | inactiva | nombre      | Pediatría  | Atención neurológica.   |
      | inactiva | descripción | Neurología | Atención  MÉDICA.       |
      | inactiva | ambos       | Pediatría  | Atención  MÉDICA.       |

  Esquema del escenario: Rechazar nulos explícitos vacíos espacios y solicitudes sin campos sin guardar cambios parciales
    Dado que estoy autenticado para actualizar especialidades como "Administrator"
    Y que preparo la especialidad "Neurología" "inactiva" para actualizarla
    Cuando actualizo la especialidad con el campo "<campo>" en estado "<estado>"
    Entonces la API de especialidades responde HTTP 400
    Y el error de actualización tiene código "<código>"
    Y ninguna especialidad cambió después de intentar actualizar

    Ejemplos:
      | campo       | estado   | código                        |
      | ninguno     | ausente  | Specialty.NoFieldsProvided    |
      | name        | nulo     | Specialty.NameRequired        |
      | name        | vacío    | Specialty.NameRequired        |
      | name        | espacios | Specialty.NameRequired        |
      | description | nulo     | Specialty.DescriptionRequired |
      | description | vacío    | Specialty.DescriptionRequired |
      | description | espacios | Specialty.DescriptionRequired |

  Escenario: Las longitudes máximas son válidas
    Dado que estoy autenticado para actualizar especialidades como "Administrator"
    Y que preparo la especialidad "Neurología" "activa" para actualizarla
    Cuando actualizo la especialidad con nombre de 100 y descripción de 500 caracteres
    Entonces la API de especialidades responde HTTP 204
    Y la actualización conserva las longitudes de 100 y 500 caracteres y el estado original

  Esquema del escenario: Las longitudes excedidas rechazan la operación completa
    Dado que estoy autenticado para actualizar especialidades como "Administrator"
    Y que preparo la especialidad "Neurología" "activa" para actualizarla
    Cuando actualizo la especialidad con nombre de <nombre> y descripción de <descripción> caracteres
    Entonces la API de especialidades responde HTTP 400
    Y el error de actualización tiene código "<código>"
    Y ninguna especialidad cambió después de intentar actualizar

    Ejemplos:
      | nombre | descripción | código                       |
      | 101    | 50          | Specialty.NameTooLong        |
      | 50     | 501         | Specialty.DescriptionTooLong |

  Escenario: Una especialidad inexistente devuelve 404 y no crea registros
    Dado que estoy autenticado para actualizar especialidades como "Administrator"
    Y que preparo la especialidad "Neurología" "activa" para actualizarla
    Cuando actualizo una especialidad inexistente
    Entonces la API de especialidades responde HTTP 404
    Y el error de actualización tiene código "Specialty.NotFound"
    Y ninguna especialidad cambió después de intentar actualizar

  Esquema del escenario: Los nombres equivalentes de otra especialidad producen conflicto incluso estando inactiva
    Dado que estoy autenticado para actualizar especialidades como "Administrator"
    Y que preparo la especialidad "Neurología" "activa" para actualizarla
    Y que otra especialidad con nombre "Cardiología" está "<estado>"
    Cuando actualizo únicamente el nombre de la especialidad a "<nombre>"
    Entonces la API de especialidades responde HTTP 409
    Y el error de actualización tiene código "Specialty.NameAlreadyInUse"
    Y ninguna especialidad cambió después de intentar actualizar

    Ejemplos:
      | nombre      | estado   |
      | Cardiología | activa   |
      | cardiologia | activa   |
      | CARDIOLOGÍA | inactiva |
      | CARDIOLOGIA | inactiva |

  Esquema del escenario: El propio ID queda excluido de la comprobación de duplicados
    Dado que estoy autenticado para actualizar especialidades como "Administrator"
    Y que preparo la especialidad "Cardiología" "inactiva" para actualizarla
    Cuando actualizo únicamente el nombre de la especialidad a "<nombre>"
    Entonces la API de especialidades responde HTTP 204
    Y la actualización persiste nombre "<guardado>" y descripción "Atención neurológica." conservando ID y estado

    Ejemplos:
      | nombre      | guardado    |
      | Cardiología | Cardiología |
      | cardiologia | Cardiologia |
      | CARDIOLOGÍA | Cardiología |
      | CARDIOLOGIA | Cardiologia |

  Esquema del escenario: Los JWT reales de Patient y Doctor no autorizan la actualización
    Dado que estoy autenticado para actualizar especialidades como "<rol>"
    Y que preparo la especialidad "Neurología" "activa" para actualizarla
    Cuando actualizo los campos "ambos" de la especialidad
    Entonces la API de especialidades responde HTTP 403
    Y ninguna especialidad cambió después de intentar actualizar

    Ejemplos:
      | rol     |
      | Patient |
      | Doctor  |

  Escenario: Un usuario anónimo no puede actualizar una especialidad
    Dado que no estoy autenticado para actualizar especialidades
    Y que preparo la especialidad "Neurología" "activa" para actualizarla
    Cuando actualizo los campos "ambos" de la especialidad
    Entonces la API de especialidades responde HTTP 401
    Y ninguna especialidad cambió después de intentar actualizar
