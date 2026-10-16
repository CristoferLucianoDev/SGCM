using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.Identity.Commands.CreateDoctorAccount;

/// <summary>
/// Pide crear la cuenta de un médico con una contraseña temporal. Solo lo usa la
/// herramienta de alta; no se expone en la API.
/// </summary>
/// <param name="Email">Correo real del médico.</param>
public sealed record CreateDoctorAccountCommand(string Email) : ICommand<CreateDoctorAccountResult>;