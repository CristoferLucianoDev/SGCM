# language: es
@Integration @InsuranceProviders
Característica: Registro de proveedores de seguros
  Como administrador, quiero registrar proveedores de seguros,
  para que los pacientes puedan asociar su seguro.

  Escenario: El administrador registra un proveedor con todos sus datos
    Dado que estoy autenticado para gestionar proveedores de seguros como "Administrator"
    Cuando registro el proveedor de seguros "Seguros Universal" con todos sus datos
    Entonces la API de proveedores de seguros responde HTTP 201
    Y recibo el ID real del proveedor de seguros registrado
    Y el proveedor de seguros registrado está activo con los datos enviados

  Escenario: El sitio web es opcional
    Dado que estoy autenticado para gestionar proveedores de seguros como "Administrator"
    Cuando registro el proveedor de seguros "Seguros Universal" sin sitio web
    Entonces la API de proveedores de seguros responde HTTP 201
    Y el proveedor de seguros registrado no tiene sitio web

  Esquema del escenario: Un dato inválido se rechaza indicando el campo
    Dado que estoy autenticado para gestionar proveedores de seguros como "Administrator"
    Cuando registro un proveedor de seguros con el campo "<campo>" <estado>
    Entonces la API de proveedores de seguros responde HTTP 400
    Y el error del proveedor de seguros tiene el código "<código>" y el mensaje "<mensaje>"
    Y no se agregó ningún proveedor de seguros

    Ejemplos:
      | campo        | estado                 | código                                 | mensaje                                                                          |
      | Name         | vacío                  | InsuranceProvider.NameRequired         | El nombre del proveedor de seguros es obligatorio.                               |
      | Name         | ausente                | InsuranceProvider.NameRequired         | El nombre del proveedor de seguros es obligatorio.                               |
      | ContactPhone | vacío                  | InsuranceProvider.ContactPhoneRequired | El teléfono de contacto es obligatorio.                                          |
      | ContactEmail | vacío                  | Common.InvalidEmail                    | El correo no tiene un formato válido o supera 256 caracteres.                    |
      | ContactEmail | con formato inválido   | Common.InvalidEmail                    | El correo no tiene un formato válido o supera 256 caracteres.                    |
      | Address      | vacío                  | InsuranceProvider.AddressRequired      | La dirección es obligatoria.                                                     |
      | Name         | que supera su longitud | InsuranceProvider.NameTooLong          | El nombre del proveedor de seguros no puede superar 150 caracteres.              |
      | ContactPhone | que supera su longitud | InsuranceProvider.ContactPhoneTooLong  | El teléfono de contacto no puede superar 20 caracteres.                          |
      | Address      | que supera su longitud | InsuranceProvider.AddressTooLong       | La dirección no puede superar 250 caracteres.                                    |
      | Website      | que supera su longitud | InsuranceProvider.WebsiteTooLong       | El sitio web no puede superar 250 caracteres.                                    |
      | Website      | con formato inválido   | InsuranceProvider.WebsiteInvalid       | El sitio web debe ser una dirección completa que empiece con http:// o https://. |

  Esquema del escenario: No se registra un nombre que ya existe
    Dado que estoy autenticado para gestionar proveedores de seguros como "Administrator"
    Y que existe el proveedor de seguros <estado> "Seguros Universal"
    Cuando registro el proveedor de seguros "Seguros Universal" con todos sus datos
    Entonces la API de proveedores de seguros responde HTTP 409
    Y el error del proveedor de seguros tiene el código "InsuranceProvider.NameAlreadyInUse" y el mensaje "Ya existe un proveedor de seguros con ese nombre."
    Y no se agregó ningún proveedor de seguros

    Ejemplos:
      | estado   |
      | activo   |
      | inactivo |

  Esquema del escenario: Los roles sin permiso no pueden registrar proveedores
    Dado que estoy autenticado para gestionar proveedores de seguros como "<rol>"
    Cuando registro el proveedor de seguros "Seguros Universal" con todos sus datos
    Entonces la API de proveedores de seguros responde HTTP 403
    Y no se agregó ningún proveedor de seguros

    Ejemplos:
      | rol     |
      | Patient |
      | Doctor  |

  Escenario: Un usuario anónimo no puede registrar proveedores
    Dado que no estoy autenticado para gestionar proveedores de seguros
    Cuando registro el proveedor de seguros "Seguros Universal" con todos sus datos
    Entonces la API de proveedores de seguros responde HTTP 401
    Y no se agregó ningún proveedor de seguros
