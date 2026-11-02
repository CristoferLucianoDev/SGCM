using Microsoft.EntityFrameworkCore;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Domain.Identity.Users;
using System.Text.Json;

namespace SGCM.Test.Common.Database;

/// <summary>
/// Inserta con SQL los datos que todavía no tienen una operación de dominio que los cree,
/// para que las pruebas los preparen siempre de la misma forma.
/// </summary>
internal static class TestDataSeeder
{
    private static readonly DateOnly PatientBirthDate = new(1990, 5, 20);

    public static async Task<int> InsertDoctorProfileAsync(AppDbContext context, int userId)
    {
        var license = $"TASK226-{Guid.NewGuid():N}";
        return (await context.Database.SqlQuery<int>(
            $"""
            INSERT INTO people.doctor
                (user_id, first_name, last_name, license_number, phone_number,
                 province_id, municipality_id, address_line, consultation_fee, years_of_experience, biography)
            SELECT {userId}, 'Luis', 'Pérez', {license}, '8095550200',
                   m.province_id, m.id, 'Calle de pruebas 226', 1500, 10, 'Perfil médico de prueba.'
            FROM people.municipality m
            ORDER BY m.id
            LIMIT 1
            RETURNING id AS "Value"
            """).ToListAsync()).Single();
    }

    public static async Task InsertProfileAsync(AppDbContext context, User user)
    {
        switch (user.Role)
        {
            case UserRole.Patient:
                await InsertPatientProfileAsync(context, user.Id);
                break;
            case UserRole.Doctor:
                await InsertDoctorProfileAsync(context, user.Id);
                break;
            case UserRole.Administrator:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(user));
        }
    }

    public static Task<int> InsertDoctorSpecialtyAsync(AppDbContext context, int doctorId, int specialtyId) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO people.doctor_specialty (doctor_id, specialty_id) VALUES ({doctorId}, {specialtyId})");

    // Los snapshots incluyen auditoría para detectar UPDATEs o recreaciones de relaciones.
    public static async Task<string[]> ReadDoctorSpecialtiesAsync(AppDbContext context, int[] doctorIds) =>
        (await context.Database.SqlQuery<string>(
            $"""
            SELECT to_jsonb(ds)::text AS "Value" FROM people.doctor_specialty ds
            WHERE ds.doctor_id = ANY({doctorIds}) ORDER BY ds.doctor_id, ds.specialty_id
            """).ToListAsync()).ToArray();

    public static async Task<string[]> ReadDoctorsAsync(AppDbContext context, int[] doctorIds) =>
        (await context.Database.SqlQuery<string>(
            $"""
            SELECT to_jsonb(d)::text AS "Value" FROM people.doctor d
            WHERE d.id = ANY({doctorIds}) ORDER BY d.id
            """).ToListAsync()).ToArray();

    public static async Task<(int[] Ids, string[] Names)> ReadDirectorySpecialtiesAsync(AppDbContext context, int doctorId)
    {
        var row = await context.Database.SqlQuery<string>(
            $"SELECT to_jsonb(d)::text AS \"Value\" FROM people.vw_doctor_directory d WHERE d.id = {doctorId}")
            .SingleAsync();
        using var document = JsonDocument.Parse(row);
        return (
            document.RootElement.GetProperty("specialty_ids").EnumerateArray().Select(value => value.GetInt32()).ToArray(),
            document.RootElement.GetProperty("specialty_names").EnumerateArray().Select(value => value.GetString()!).ToArray());
    }

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
    /// Crea un proveedor de seguros activo, solo con las columnas obligatorias.
    /// </summary>
    /// <param name="context">Contexto con el que se ejecuta el SQL.</param>
    /// <param name="name">Nombre del proveedor, que debe ser único.</param>
    /// <returns>El id del proveedor creado.</returns>
    public static async Task<int> InsertInsuranceProviderAsync(AppDbContext context, string name = "Seguros Universal")
    {
        return (await context.Database.SqlQuery<int>(
            $"""
            INSERT INTO people.insurance_provider (name, contact_phone, contact_email, address)
            VALUES ({name}, '8095550200', 'contacto@seguros.com', 'Av. Winston Churchill 100')
            RETURNING id AS "Value"
            """).ToListAsync()).Single();
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
