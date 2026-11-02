using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;

namespace SGCM.Data.Application.People.Commands.CompletePatientProfile;

/// <summary>
/// Crea el perfil del paciente autenticado y le abre una sesión nueva, ya sin paso pendiente.
/// El paciente no viaja en el comando: el handler lo toma de <see cref="ICurrentUser"/>.
/// </summary>
/// <param name="FirstName">Nombre. No se puede editar después.</param>
/// <param name="LastName">Apellido. No se puede editar después.</param>
/// <param name="BirthDate">Fecha de nacimiento. No se puede editar después.</param>
/// <param name="PhoneNumber">Teléfono.</param>
/// <param name="Address">Dirección opcional.</param>
/// <param name="EmergencyContactName">Nombre del contacto de emergencia; va junto con su teléfono.</param>
/// <param name="EmergencyContactPhone">Teléfono del contacto de emergencia; va junto con su nombre.</param>
/// <param name="InsuranceProviderId">Proveedor de seguros opcional; debe existir y estar activo.</param>
/// <param name="PolicyNumber">Número de póliza; obligatorio si se elige un proveedor.</param>
/// <param name="DeviceName">Nombre del dispositivo. Lo llena la API a partir de la petición.</param>
/// <param name="IpAddress">Dirección IP del cliente. La llena la API a partir de la petición.</param>
public sealed record CompletePatientProfileCommand(
    string FirstName,
    string LastName,
    DateOnly BirthDate,
    string PhoneNumber,
    string? Address,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    int? InsuranceProviderId,
    string? PolicyNumber,
    string? DeviceName,
    string? IpAddress) : ICommand<SessionTokens>;
