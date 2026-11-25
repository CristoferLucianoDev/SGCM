# language: es
@Integration @DoctorSearch
Característica: Buscar doctores
  Como visitante o paciente
  Quiero buscar médicos por especialidad y ubicación
  Para encontrar a quién consultar

  Antecedentes:
    Dado un catálogo de especialidades con:
      | Especialidad | Estado   |
      | Cardiología  | Activa   |
      | Pediatría    | Activa   |
      | Neurología   | Inactiva |
    Y un directorio médico con:
      | Médico | Nombre completo | Provincia | Municipio       | Perfil   | Cuenta   | Especialidades                     | Tarifa  |
      | Alba   | Alba Núñez      | Azua      | Azua            | Activo   | Activa   | Cardiología, Pediatría, Neurología  | 1500.75 |
      | Bruno  | Bruno Méndez    | Azua      | Azua            | Activo   | Activa   | Cardiología                        | 1750.50 |
      | Clara  | Clara Díaz      | Azua      | Padre Las Casas | Activo   | Activa   | Cardiología                        | 1300.00 |
      | Diego  | Diego Pérez     | Azua      | Azua            | Activo   | Activa   | Pediatría                          | 1200.00 |
      | Elena  | Elena Peña      | Dajabón   | Dajabón         | Activo   | Activa   | Cardiología                        | 1600.00 |
      | Fabio  | Fabio Mora      | Azua      | Azua            | Inactivo | Activa   | Cardiología                        | 1100.00 |
      | Gina   | Gina Ramos      | Azua      | Azua            | Activo   | Inactiva | Cardiología                        | 1400.00 |
      | Hugo   | Hugo Mora       | Azua      | Azua            | Activo   | Activa   | Sin especialidades                 | 1000.00 |
      | Inés   | Inés López      | Azua      | Azua            | Activo   | Activa   | Neurología                         | 1900.00 |

  Escenario: Un visitante consulta el directorio sin filtros
    Dado que consulto el directorio sin iniciar sesión
    Cuando busco médicos sin aplicar filtros
    Entonces recibo la página 1 de tamaño 20 con total 5
    Y aparecen únicamente "Alba, Bruno, Clara, Diego, Elena" en ese orden
    Y cada resultado muestra únicamente la información pública de búsqueda

  Esquema del escenario: Un visitante aplica un filtro por separado
    Dado que consulto el directorio sin iniciar sesión
    Cuando busco médicos solo por "<Filtro>" con valor "<Valor>"
    Entonces recibo la página 1 de tamaño 20 con total <Total>
    Y aparecen únicamente "<Médicos>" en ese orden

    Ejemplos:
      | Filtro       | Valor       | Total | Médicos                    |
      | especialidad | Cardiología | 4     | Alba, Bruno, Clara, Elena  |
      | provincia    | Azua        | 4     | Alba, Bruno, Clara, Diego  |
      | municipio    | Azua        | 3     | Alba, Bruno, Diego         |

  Escenario: Un visitante combina la ubicación del catálogo con una especialidad
    Dado que consulto el directorio sin iniciar sesión
    Cuando selecciono en los catálogos la provincia "Azua" y su municipio "Azua"
    Y busco médicos de "Cardiología" en esa ubicación en las páginas 1, 2 y 3 de tamaño 1
    Entonces la primera página contiene únicamente a "Alba"
    Y la segunda página contiene únicamente a "Bruno"
    Y las tres páginas conservan el total de 2 médicos
    Y la tercera página se devuelve correctamente sin médicos
    Y el resultado de "Alba" contiene exactamente "Cardiología, Pediatría" en ese orden

  Esquema del escenario: Una sesión conserva la privacidad del listado público
    Dado que consulto el directorio como "<Actor>"
    Cuando busco médicos sin aplicar filtros
    Entonces recibo la misma página que un visitante al hacer esa búsqueda
    Y cada resultado muestra únicamente la información pública de búsqueda

    Ejemplos:
      | Actor         |
      | Paciente      |
      | Médico        |
      | Administrador |

  Escenario: Los filtros válidos no tienen coincidencias al combinarse
    Dado que consulto el directorio sin iniciar sesión
    Cuando busco "Pediatría" en el municipio "Dajabón" de la provincia "Dajabón"
    Entonces recibo la página 1 de tamaño 20 sin médicos y con total 0

  Escenario: Un visitante encuentra una médica por nombre y abre su ficha
    Dado que consulto el directorio sin iniciar sesión
    Cuando busco médicos por el nombre "ALBA NUNEZ"
    Y abro la ficha de la única coincidencia
    Entonces recibo la ficha pública completa de "Alba Núñez"
    Y la ficha corresponde al mismo médico identificado en el listado
    Y la ficha omite el teléfono y la licencia
