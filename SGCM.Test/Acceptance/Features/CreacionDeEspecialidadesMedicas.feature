# language: es
@Integration @Specialties
Característica: Creación de especialidades médicas
  Como Administrator, quiero crear especialidades médicas válidas y sin nombres duplicados,
  para disponer de un catálogo activo de especialidades.

  Escenario: Administrator crea una especialidad activa y conserva tildes y ñ
    Dado que estoy autenticado para crear especialidades como "Administrator"
    Cuando solicito crear la especialidad "  ATENCIÓN A LA NIÑEZ  " con descripción "  Atención  MÉDICA.  "
    Entonces la API de especialidades responde HTTP 201
    Y recibo el ID real de la especialidad creada
    Y la especialidad creada está activa con nombre "Atención A La Niñez" y descripción "Atención  MÉDICA."

  Esquema del escenario: Los datos obligatorios identifican el campo inválido
    Dado que estoy autenticado para crear especialidades como "Administrator"
    Cuando solicito crear una especialidad con el campo "<campo>" "<estado>"
    Entonces la API de especialidades responde HTTP 400
    Y el error de especialidad tiene el código "<código>" y el mensaje "<mensaje>"
    Y no se agregó ninguna especialidad

    Ejemplos:
      | campo       | estado  | código                        | mensaje                                         |
      | Name        | vacío   | Specialty.NameRequired        | El nombre de la especialidad es obligatorio.     |
      | Name        | ausente | Specialty.NameRequired        | El nombre de la especialidad es obligatorio.     |
      | Description | vacío   | Specialty.DescriptionRequired | La descripción de la especialidad es obligatoria. |
      | Description | ausente | Specialty.DescriptionRequired | La descripción de la especialidad es obligatoria. |

  Escenario: Se aceptan las longitudes máximas del nombre y la descripción
    Dado que estoy autenticado para crear especialidades como "Administrator"
    Cuando solicito crear una especialidad con nombre de 100 y descripción de 500 caracteres
    Entonces la API de especialidades responde HTTP 201
    Y recibo el ID real de la especialidad creada
    Y la especialidad creada está activa y tiene nombre de 100 y descripción de 500 caracteres

  Esquema del escenario: Las longitudes excedidas identifican el campo inválido
    Dado que estoy autenticado para crear especialidades como "Administrator"
    Cuando solicito crear una especialidad con nombre de <nombre> y descripción de <descripción> caracteres
    Entonces la API de especialidades responde HTTP 400
    Y el error de especialidad tiene el código "<código>" y el mensaje "<mensaje>"
    Y no se agregó ninguna especialidad

    Ejemplos:
      | nombre | descripción | código                       | mensaje                                                               |
      | 101    | 30          | Specialty.NameTooLong        | El nombre de la especialidad no puede superar 100 caracteres.           |
      | 30     | 501         | Specialty.DescriptionTooLong | La descripción de la especialidad no puede superar 500 caracteres.      |

  Escenario: Cardiología impide crear cardiologia
    Dado que estoy autenticado para crear especialidades como "Administrator"
    Y que existe la especialidad "Cardiología"
    Cuando solicito crear la especialidad "cardiologia" con descripción "Otra descripción."
    Entonces la API de especialidades responde HTTP 409
    Y el error de especialidad tiene el código "Specialty.NameAlreadyInUse" y el mensaje "El nombre de la especialidad ya está en uso."
    Y no se agregó ninguna especialidad
    Y solo permanece la especialidad original "Cardiología"

  Esquema del escenario: Los roles sin permiso no pueden crear especialidades
    Dado que estoy autenticado para crear especialidades como "<rol>"
    Cuando solicito crear la especialidad "Cardiología" con descripción "Atención del corazón."
    Entonces la API de especialidades responde HTTP 403
    Y no se agregó ninguna especialidad

    Ejemplos:
      | rol     |
      | Patient |
      | Doctor  |

  Escenario: Un usuario anónimo no puede crear especialidades
    Dado que no estoy autenticado para crear especialidades
    Cuando solicito crear la especialidad "Cardiología" con descripción "Atención del corazón."
    Entonces la API de especialidades responde HTTP 401
    Y no se agregó ninguna especialidad
