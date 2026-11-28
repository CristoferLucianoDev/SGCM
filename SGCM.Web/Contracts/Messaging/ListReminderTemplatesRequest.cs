namespace SGCM.Web.Contracts.Messaging;

/// <summary>Paginación de la lista de plantillas.</summary>
/// <param name="PageNumber">Página, desde 1.</param>
/// <param name="PageSize">Tamaño de página, de 1 a 100.</param>
public sealed record ListReminderTemplatesRequest(int PageNumber = 1, int PageSize = 20);
