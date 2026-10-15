using Microsoft.EntityFrameworkCore;
using SGCM.Data.Infrastructure.Persistence;

namespace SGCM.Test.Common.Database;

/// <summary>
/// Inserta con SQL los datos que todavía no tienen una operación de dominio que los cree,
/// para que las pruebas los preparen siempre de la misma forma.
/// </summary>
internal static class TestDataSeeder
{
    private static readonly DateOnly PatientBirthDate = new(1990, 5, 20);

    /// <summary>
    /// Crea el perfil de paciente del usuario, solo con las columnas obligatorias.
    /// </summary>
    /// <param name="context">Contexto con el que se ejecuta el SQL.</param>
    /// <param name="userId">Id del usuario, que debe tener el rol de paciente.</param>
    /// <returns>Una tarea que termina cuando el perfil queda guardado.</returns>
    public static async Task InsertPatientProfileAsync(AppDbContext context, int userId)
    {
        // El trigger de rol de la tabla exige que la cuenta sea de paciente.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO people.patient (user_id, first_name, last_name, birth_date, phone_number)
            VALUES ({userId}, 'Ana', 'Pérez', {PatientBirthDate}, '8095550100')
            """);
    }

    /// <summary>
    /// Configura el segundo factor del usuario, solo con las columnas obligatorias.
    /// </summary>
    /// <param name="context">Contexto con el que se ejecuta el SQL.</param>
    /// <param name="userId">Id del usuario.</param>
    /// <param name="isEnabled">
    /// Indica si el segundo factor queda activo; en <c>false</c> la fila existe pero no se exige.
    /// </param>
    /// <returns>Una tarea que termina cuando el segundo factor queda guardado.</returns>
    public static async Task EnableTwoFactorAsync(AppDbContext context, int userId, bool isEnabled = true)
    {
        // El secreto no se lee en las pruebas, así que basta un valor de relleno.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO app.user_two_factor (user_id, secret_key, is_enabled)
            VALUES ({userId}, 'secreto-cifrado', {isEnabled})
            """);
    }
}
