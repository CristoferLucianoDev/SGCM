using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.People.Commands.UpdatePatientProfile;

/// <summary>
/// Reemplaza los datos editables del perfil del paciente autenticado. El paciente no viaja en
/// el comando: el handler lo toma de <see cref="ICurrentUser"/>, así que nadie puede modificar
/// el perfil de otro. Nombre, apellido y fecha de nacimiento no se incluyen: solo el
/// administrador los corrige.
/// </summary>
/// <param name="PhoneNumber">Teléfono.</param>
/// <param name="Address">Dirección; <c>null</c> la quita.</param>
/// <param name="EmergencyContactName">Nombre del contacto de emergencia; va junto con su teléfono.</param>
/// <param name="EmergencyContactPhone">Teléfono del contacto de emergencia; va junto con su nombre.</param>
/// <param name="InsuranceProviderId">Proveedor de seguros; <c>null</c> quita el seguro.</param>
/// <param name="PolicyNumber">Número de póliza; obligatorio si hay proveedor.</param>
public sealed record UpdatePatientProfileCommand(
    string PhoneNumber,
    string? Address,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    int? InsuranceProviderId,
    string? PolicyNumber) : ICommand<Unit>;
