namespace SGCM.Web.Contracts.Messaging;

/// <summary>Página de plantillas que el médico puede usar: las suyas activas y las globales, de solo lectura.</summary>
public sealed record ListReminderTemplatesResponse(
    IReadOnlyList<ReminderTemplateResponse> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
