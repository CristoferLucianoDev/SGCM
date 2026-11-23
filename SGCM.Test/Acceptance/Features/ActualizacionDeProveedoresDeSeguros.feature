# language: es
@Integration @InsuranceProviders @InsuranceProviderUpdating
Característica: Actualización de proveedores de seguros
  Como administrador, quiero cambiar solo los datos que necesite de un proveedor de seguros,
  para mantenerlo al día sin perder los demás datos ni repetir el nombre de otro proveedor.

  Esquema del escenario: Actualizar campos presentes conserva los omitidos y funciona con proveedores inactivos
    Dado que estoy autenticado para gestionar proveedores de seguros como "Administrator"
    Y que preparo el proveedor de seguros "Seguros Universal" "<estado>" para actualizarlo
    Cuando actualizo los campos "<campos>" del proveedor de seguros
    Entonces la API de proveedores de seguros responde HTTP 204
    Y el proveedor de seguros persiste los campos "<campos>" conservando ID, estado y los demás datos

    Ejemplos:
      | estado   | campos     |
      | activo   | nombre     |
      | activo   | teléfono   |
      | activo   | correo     |
      | activo   | dirección  |
      | activo   | sitio web  |
      | activo   | todos      |
      | inactivo | todos      |

  Esquema del escenario: El sitio web enviado como nulo, vacío o espacios se quita y conserva el resto
    Dado que estoy autenticado para gestionar proveedores de seguros como "Administrator"
    Y que preparo el proveedor de seguros "Seguros Universal" "activo" para actualizarlo
    Cuando actualizo el proveedor de seguros con el campo "Website" en estado "<estado>"
    Entonces la API de proveedores de seguros responde HTTP 204
    Y el sitio web del proveedor de seguros fue quitado conservando los demás datos

    Ejemplos:
      | estado   |
      | nulo     |
      | vacío    |
      | espacios |

  Esquema del escenario: Dejar vacío un campo obligatorio se rechaza indicando el campo
    Dado que estoy autenticado para gestionar proveedores de seguros como "Administrator"
    Y que preparo el proveedor de seguros "Seguros Universal" "inactivo" para actualizarlo
    Cuando actualizo el proveedor de seguros con el campo "<campo>" en estado "<estado>"
    Entonces la API de proveedores de seguros responde HTTP 400
    Y el error del proveedor de seguros tiene el código "<código>" y el mensaje "<mensaje>"
    Y ningún proveedor de seguros cambió después de intentar actualizar

    Ejemplos:
      | campo        | estado   | código                                 | mensaje                                                       |
      | Name         | nulo     | InsuranceProvider.NameRequired         | El nombre del proveedor de seguros es obligatorio.            |
      | Name         | vacío    | InsuranceProvider.NameRequired         | El nombre del proveedor de seguros es obligatorio.            |
      | Name         | espacios | InsuranceProvider.NameRequired         | El nombre del proveedor de seguros es obligatorio.            |
      | ContactPhone | nulo     | InsuranceProvider.ContactPhoneRequired | El teléfono de contacto es obligatorio.                       |
      | ContactPhone | vacío    | InsuranceProvider.ContactPhoneRequired | El teléfono de contacto es obligatorio.                       |
      | ContactPhone | espacios | InsuranceProvider.ContactPhoneRequired | El teléfono de contacto es obligatorio.                       |
      | ContactEmail | nulo     | Common.InvalidEmail                    | El correo no tiene un formato válido o supera 256 caracteres. |
      | ContactEmail | vacío    | Common.InvalidEmail                    | El correo no tiene un formato válido o supera 256 caracteres. |
      | ContactEmail | espacios | Common.InvalidEmail                    | El correo no tiene un formato válido o supera 256 caracteres. |
      | Address      | nulo     | InsuranceProvider.AddressRequired      | La dirección es obligatoria.                                  |
      | Address      | vacío    | InsuranceProvider.AddressRequired      | La dirección es obligatoria.                                  |
      | Address      | espacios | InsuranceProvider.AddressRequired      | La dirección es obligatoria.                                  |

  Escenario: Una solicitud sin campos se rechaza
    Dado que estoy autenticado para gestionar proveedores de seguros como "Administrator"
    Y que preparo el proveedor de seguros "Seguros Universal" "activo" para actualizarlo
    Cuando actualizo el proveedor de seguros sin enviar ningún campo
    Entonces la API de proveedores de seguros responde HTTP 400
    Y el error del proveedor de seguros tiene el código "InsuranceProvider.NoFieldsProvided" y el mensaje "Debe proporcionar al menos un dato del proveedor de seguros para actualizarlo."
    Y ningún proveedor de seguros cambió después de intentar actualizar

  Esquema del escenario: Las longitudes excedidas rechazan la actualización completa
    Dado que estoy autenticado para gestionar proveedores de seguros como "Administrator"
    Y que preparo el proveedor de seguros "Seguros Universal" "activo" para actualizarlo
    Cuando actualizo el proveedor de seguros con el campo "<campo>" que supera su longitud
    Entonces la API de proveedores de seguros responde HTTP 400
    Y el error del proveedor de seguros tiene el código "<código>" y el mensaje "<mensaje>"
    Y ningún proveedor de seguros cambió después de intentar actualizar

    Ejemplos:
      | campo        | código                                 | mensaje                                                             |
      | Name         | InsuranceProvider.NameTooLong          | El nombre del proveedor de seguros no puede superar 150 caracteres. |
      | ContactPhone | InsuranceProvider.ContactPhoneTooLong  | El teléfono de contacto no puede superar 20 caracteres.             |
      | Address      | InsuranceProvider.AddressTooLong       | La dirección no puede superar 250 caracteres.                       |
      | Website      | InsuranceProvider.WebsiteTooLong       | El sitio web no puede superar 250 caracteres.                       |

  Esquema del escenario: Los formatos inválidos rechazan la actualización completa
    Dado que estoy autenticado para gestionar proveedores de seguros como "Administrator"
    Y que preparo el proveedor de seguros "Seguros Universal" "activo" para actualizarlo
    Cuando actualizo el proveedor de seguros con el campo "<campo>" con formato inválido
    Entonces la API de proveedores de seguros responde HTTP 400
    Y el error del proveedor de seguros tiene el código "<código>" y el mensaje "<mensaje>"
    Y ningún proveedor de seguros cambió después de intentar actualizar

    Ejemplos:
      | campo        | código                           | mensaje                                                                          |
      | ContactEmail | Common.InvalidEmail              | El correo no tiene un formato válido o supera 256 caracteres.                    |
      | Website      | InsuranceProvider.WebsiteInvalid | El sitio web debe ser una dirección completa que empiece con http:// o https://. |

  Escenario: Las longitudes máximas son válidas
    Dado que estoy autenticado para gestionar proveedores de seguros como "Administrator"
    Y que preparo el proveedor de seguros "Seguros Universal" "activo" para actualizarlo
    Cuando actualizo el proveedor de seguros con los campos en su longitud máxima
    Entonces la API de proveedores de seguros responde HTTP 204
    Y el proveedor de seguros persiste los campos en su longitud máxima conservando ID, estado y correo

  Esquema del escenario: El nombre de otro proveedor produce conflicto aunque esté inactivo
    Dado que estoy autenticado para gestionar proveedores de seguros como "Administrator"
    Y que preparo el proveedor de seguros "Seguros Universal" "activo" para actualizarlo
    Y que otro proveedor de seguros con nombre "Seguros Nacional" está "<estado>"
    Cuando actualizo el nombre del proveedor de seguros a "Seguros Nacional" y el teléfono a "809-555-0999"
    Entonces la API de proveedores de seguros responde HTTP 409
    Y el error del proveedor de seguros tiene el código "InsuranceProvider.NameAlreadyInUse" y el mensaje "Ya existe un proveedor de seguros con ese nombre."
    Y ningún proveedor de seguros cambió después de intentar actualizar

    Ejemplos:
      | estado   |
      | activo   |
      | inactivo |

  Esquema del escenario: El nombre de otro proveedor con espacios en los extremos también produce conflicto
    Dado que estoy autenticado para gestionar proveedores de seguros como "Administrator"
    Y que preparo el proveedor de seguros "Seguros Universal" "inactivo" para actualizarlo
    Y que otro proveedor de seguros con nombre "Seguros Nacional" está "<estado>"
    Cuando actualizo el nombre del proveedor de seguros a "   Seguros Nacional   " y el teléfono a "809-555-0999"
    Entonces la API de proveedores de seguros responde HTTP 409
    Y el error del proveedor de seguros tiene el código "InsuranceProvider.NameAlreadyInUse" y el mensaje "Ya existe un proveedor de seguros con ese nombre."
    Y ningún proveedor de seguros cambió después de intentar actualizar

    Ejemplos:
      | estado   |
      | activo   |
      | inactivo |

  Escenario: El propio nombre no cuenta como duplicado
    Dado que estoy autenticado para gestionar proveedores de seguros como "Administrator"
    Y que preparo el proveedor de seguros "Seguros Universal" "inactivo" para actualizarlo
    Y que otro proveedor de seguros con nombre "Seguros Nacional" está "activo"
    Cuando actualizo únicamente el nombre del proveedor de seguros a "Seguros Universal"
    Entonces la API de proveedores de seguros responde HTTP 204
    Y ningún proveedor de seguros cambió después de intentar actualizar

  Escenario: El propio nombre con espacios en los extremos tampoco cuenta como duplicado
    Dado que estoy autenticado para gestionar proveedores de seguros como "Administrator"
    Y que preparo el proveedor de seguros "Seguros Universal" "activo" para actualizarlo
    Cuando actualizo únicamente el nombre del proveedor de seguros a "   Seguros Universal   "
    Entonces la API de proveedores de seguros responde HTTP 204
    Y ningún proveedor de seguros cambió después de intentar actualizar

  Escenario: Un proveedor inexistente devuelve 404 y no crea registros
    Dado que estoy autenticado para gestionar proveedores de seguros como "Administrator"
    Y que preparo el proveedor de seguros "Seguros Universal" "activo" para actualizarlo
    Cuando actualizo un proveedor de seguros inexistente
    Entonces la API de proveedores de seguros responde HTTP 404
    Y el error del proveedor de seguros tiene el código "InsuranceProvider.NotFound" y el mensaje "El proveedor de seguros no existe."
    Y ningún proveedor de seguros cambió después de intentar actualizar

  Esquema del escenario: Los JWT reales de Patient y Doctor no autorizan la actualización
    Dado que estoy autenticado para gestionar proveedores de seguros como "<rol>"
    Y que preparo el proveedor de seguros "Seguros Universal" "activo" para actualizarlo
    Cuando actualizo los campos "todos" del proveedor de seguros
    Entonces la API de proveedores de seguros responde HTTP 403
    Y ningún proveedor de seguros cambió después de intentar actualizar

    Ejemplos:
      | rol     |
      | Patient |
      | Doctor  |

  Escenario: Un usuario anónimo no puede actualizar un proveedor de seguros
    Dado que no estoy autenticado para gestionar proveedores de seguros
    Y que preparo el proveedor de seguros "Seguros Universal" "activo" para actualizarlo
    Cuando actualizo los campos "todos" del proveedor de seguros
    Entonces la API de proveedores de seguros responde HTTP 401
    Y ningún proveedor de seguros cambió después de intentar actualizar
