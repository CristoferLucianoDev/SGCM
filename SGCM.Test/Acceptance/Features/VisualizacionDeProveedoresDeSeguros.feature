# language: es
@Integration @InsuranceProviders @InsuranceProviderListing
Característica: Visualización de proveedores de seguros
  Como persona que necesita conocer los seguros que se aceptan, quiero consultar los proveedores de seguros activos,
  y como administrador quiero poder revisar también los proveedores inactivos.

  Antecedentes:
    Dado que no estoy autenticado para consultar proveedores de seguros
    Y que el catálogo de proveedores de seguros contiene:
      | Nombre            | Teléfono   | Correo                 | Dirección         | Sitio web                 | Estado   |
      | Seguros Universal | 8095550201 | contacto@universal.com | Av. Churchill 100 | https://www.universal.com | Activo   |
      | Seguros Médicos   | 8095550202 | contacto@medicos.com   | Calle Duarte 5    |                           | Activo   |
      | Seguros Nacional  | 8095550203 | contacto@nacional.com  | Av. Lincoln 20    | https://www.nacional.com  | Inactivo |

  Escenario: Una persona sin sesión consulta una página de proveedores activos con todos sus datos
    Cuando consulto la página 2 de tamaño 1 de proveedores de seguros con estado "All"
    Entonces la API de proveedores de seguros responde HTTP 200
    Y recibo el proveedor de seguros "Seguros Universal" con todos sus datos
    Y la página de proveedores recibida es 2 y su tamaño es 1
    Y el total de proveedores de seguros disponibles es 2
    Y no se expone el estado de los proveedores de seguros

  Escenario: Un proveedor sin sitio web se muestra sin sitio
    Cuando consulto la página 1 de tamaño 1 de proveedores de seguros con estado "Active"
    Entonces la API de proveedores de seguros responde HTTP 200
    Y recibo el proveedor de seguros "Seguros Médicos" sin sitio web

  Esquema del escenario: La búsqueda por nombre encuentra coincidencias parciales normalizadas
    Cuando busco proveedores de seguros por "<búsqueda>"
    Entonces la API de proveedores de seguros responde HTTP 200
    Y recibo únicamente el proveedor de seguros "<proveedor>"

    Ejemplos:
      | búsqueda  | proveedor         |
      | univ      | Seguros Universal |
      | UNIVERSÁL | Seguros Universal |
      | medicos   | Seguros Médicos   |
      | MÉDICOS   | Seguros Médicos   |

  Esquema del escenario: Administrator filtra y ve también los proveedores inactivos
    Dado que estoy autenticado para consultar proveedores de seguros como "Administrator"
    Cuando consulto los proveedores de seguros con estado "<filtro>"
    Entonces la API de proveedores de seguros responde HTTP 200
    Y recibo <cantidad> proveedores de seguros correspondientes al filtro "<filtro>"

    Ejemplos:
      | filtro   | cantidad |
      | Active   | 2        |
      | Inactive | 1        |
      | All      | 3        |

  Esquema del escenario: Los otros roles conservan únicamente los proveedores activos
    Dado que estoy autenticado para consultar proveedores de seguros como "<rol>"
    Cuando consulto los proveedores de seguros con estado "<filtro>"
    Entonces la API de proveedores de seguros responde HTTP 200
    Y recibo únicamente los 2 proveedores de seguros activos
    Y no se expone el estado de los proveedores de seguros

    Ejemplos:
      | rol     | filtro   |
      | Patient | All      |
      | Doctor  | Inactive |

  Escenario: Una búsqueda sin coincidencias informa que no existen registros de proveedores de seguros
    Cuando busco proveedores de seguros por "ProveedorInexistente"
    Entonces la API de proveedores de seguros responde HTTP 200
    Y recibo una lista vacía de proveedores de seguros con total 0
    Y recibo el mensaje de proveedores de seguros "No existen registros de proveedores de seguros"
