# SGCM - Sistema de Gestión de Citas Médicas

## Paquetes NuGet

Las versiones de los paquetes NuGet se definen solo en `Directory.Packages.props`. Para agregar o actualizar un paquete: añadir o cambiar su `PackageVersion` ahí, poner el `PackageReference` sin versión en el `.csproj`, ejecutar `dotnet restore` y subir el `packages.lock.json` actualizado junto con el cambio. No se actualizan paquetes desde el administrador de NuGet de Visual Studio sin revisar este archivo.
