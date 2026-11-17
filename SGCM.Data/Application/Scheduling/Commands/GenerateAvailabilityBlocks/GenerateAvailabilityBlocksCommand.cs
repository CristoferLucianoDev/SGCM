using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.Scheduling.Commands.GenerateAvailabilityBlocks;

/// <summary>Completa hasta el horizonte los bloques de todas las series activas. Lo ejecuta el job diario.</summary>
public sealed record GenerateAvailabilityBlocksCommand : ICommand<int>;
