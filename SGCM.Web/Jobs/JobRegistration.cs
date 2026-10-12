namespace SGCM.Web.Jobs;

/// <summary>
/// Registro de los procesos automáticos en el contenedor de dependencias.
/// </summary>
public static class JobRegistration
{
    /// <summary>
    /// Registra todos los procesos automáticos de la aplicación. Cada proceso nuevo se agrega aquí.
    /// </summary>
    /// <param name="services">Contenedor de servicios.</param>
    /// <returns>El mismo contenedor, para encadenar llamadas.</returns>
    public static IServiceCollection AddSgcmJobs(this IServiceCollection services)
    {
        services.AddRecurringJob<PurgeUnconfirmedAccountsJob>();

        return services;
    }

    /// <summary>
    /// Registra un proceso automático para que el host lo arranque y lo detenga con la aplicación.
    /// </summary>
    /// <typeparam name="TJob">Proceso a registrar.</typeparam>
    /// <param name="services">Contenedor de servicios.</param>
    /// <returns>El mismo contenedor, para encadenar llamadas.</returns>
    public static IServiceCollection AddRecurringJob<TJob>(this IServiceCollection services)
        where TJob : class, IHostedService
        => services.AddHostedService<TJob>();
}
