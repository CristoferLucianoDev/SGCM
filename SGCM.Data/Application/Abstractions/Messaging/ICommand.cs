namespace SGCM.Data.Application.Abstractions.Messaging;

/// <summary>
/// Caso de uso que modifica datos y devuelve <typeparamref name="TResponse"/>.
/// </summary>
public interface ICommand<TResponse>;