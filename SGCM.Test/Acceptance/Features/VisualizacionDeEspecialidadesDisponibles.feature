# language: es
@Integration @Specialties @SpecialtyListing
Característica: Visualización de especialidades disponibles
  Como persona que necesita atención médica, quiero consultar el catálogo de especialidades,
  y como Administrator quiero poder revisar también las especialidades inactivas.

  Antecedentes:
    Dado que no estoy autenticado para consultar especialidades
    Y que el catálogo de especialidades contiene:
      | Nombre           | Descripción          | Estado   |
      | Ginecología      | Salud ginecológica.  | Activa   |
      | Medicina Interna | Atención de adultos. | Activa   |
      | Neurología       | Sistema nervioso.    | Inactiva |

  Escenario: Una persona sin sesión consulta una página de especialidades activas
    Cuando consulto la página 1 de tamaño 1 con estado "All"
    Entonces la API de especialidades responde HTTP 200
    Y recibo "Ginecología" con descripción "Salud ginecológica."
    Y la página recibida es 1 y su tamaño es 1
    Y el total de especialidades disponibles es 2
    Y no se expone el estado de las especialidades

  Esquema del escenario: La búsqueda encuentra coincidencias parciales normalizadas
    Cuando busco especialidades por "<búsqueda>"
    Entonces la API de especialidades responde HTTP 200
    Y recibo únicamente la especialidad "Ginecología"

    Ejemplos:
      | búsqueda |
      | gineco   |
      | GINÉCO   |

  Esquema del escenario: Administrator consulta los estados permitidos
    Dado que estoy autenticado para consultar especialidades como "Administrator"
    Cuando consulto el catálogo con estado "<filtro>"
    Entonces la API de especialidades responde HTTP 200
    Y recibo <cantidad> especialidades correspondientes al filtro "<filtro>"

    Ejemplos:
      | filtro   | cantidad |
      | Active   | 2        |
      | Inactive | 1        |
      | All      | 3        |

  Esquema del escenario: Los otros roles conservan únicamente el catálogo activo
    Dado que estoy autenticado para consultar especialidades como "<rol>"
    Cuando consulto el catálogo con estado "<filtro>"
    Entonces la API de especialidades responde HTTP 200
    Y recibo únicamente las 2 especialidades activas
    Y no se expone el estado de las especialidades

    Ejemplos:
      | rol     | filtro   |
      | Patient | All      |
      | Doctor  | Inactive |

  Escenario: Una búsqueda sin coincidencias informa que no hay especialidades disponibles
    Cuando busco especialidades por "EspecialidadInexistente"
    Entonces la API de especialidades responde HTTP 200
    Y recibo una lista vacía con total 0
    Y recibo el mensaje "No existen especialidades disponibles"
