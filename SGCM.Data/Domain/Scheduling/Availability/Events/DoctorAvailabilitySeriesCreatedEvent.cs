using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.Scheduling.Availability.Events;

/// <summary>
/// Se creó una serie semanal de disponibilidad. Se despacha después de guardar, cuando la
/// serie ya tiene su Id, para que su manejador genere los bloques hasta el horizonte.
/// </summary>
/// <param name="Series">La serie creada.</param>
public sealed record DoctorAvailabilitySeriesCreatedEvent(DoctorAvailabilitySeries Series) : IDomainEvent;
