using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Messaging.Reminders;
using SGCM.Data.Domain.Messaging.ReminderTemplates;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Repositories.Messaging;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

/// <summary>
/// Pruebas de integración del mapeo y de los repositorios de recordatorios y plantillas contra PostgreSQL. Las filas
/// se leen con SQL para comprobar lo que realmente quedó en cada columna.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ReminderRepositoryTests(PostgresFixture database) : IntegrationTestBase(database)
{
    private const string Message = "Hola Ana, le recordamos su cita de mañana.";
    private static readonly DateTimeOffset StartsAt = new(2030, 3, 10, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2030, 3, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Guarda_el_recordatorio_en_cada_columna()
    {
        await using var context = CreateContext();
        var seed = await SeedAppointmentAsync(context, StartsAt);
        var template = await GlobalTemplateAsync(context);
        var reminder = Reminder.Schedule(seed.AppointmentId, template.Id, Message, StartsAt.AddHours(-24), Now).Value;

        await AddAsync(context, reminder);

        reminder.Id.ShouldBeGreaterThan(0);
        (await ReadRowAsync(reminder.Id)).ShouldBe(new ReminderRow(
            reminder.Id, seed.AppointmentId, template.Id, Message, StartsAt.AddHours(-24), SentAt: null, "pending",
            CreatedBy: null));
    }

    [Fact]
    public async Task Un_mensaje_libre_guarda_la_plantilla_nula_y_el_medico_que_lo_programo()
    {
        await using var context = CreateContext();
        var seed = await SeedAppointmentAsync(context, StartsAt);
        var reminder = Reminder.Schedule(seed.AppointmentId, null, Message, StartsAt.AddHours(-2), Now, seed.DoctorUserId).Value;

        await AddAsync(context, reminder);

        var row = await ReadRowAsync(reminder.Id);
        row.TemplateId.ShouldBeNull();
        row.CreatedBy.ShouldBe(seed.DoctorUserId);
    }

    [Theory]
    [InlineData("sent")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    public async Task Guarda_y_lee_cada_estado_en_minusculas(string expected)
    {
        await using var context = CreateContext();
        var seed = await SeedAppointmentAsync(context, StartsAt);
        var reminder = Reminder.Schedule(seed.AppointmentId, null, Message, StartsAt.AddHours(-24), Now).Value;
        await AddAsync(context, reminder);

        var result = expected switch
        {
            "sent" => reminder.MarkSent(StartsAt.AddHours(-24)),
            "failed" => reminder.MarkFailed(),
            _ => reminder.Cancel()
        };
        result.IsSuccess.ShouldBeTrue();
        await context.SaveChangesAsync();

        var row = await ReadRowAsync(reminder.Id);
        row.DeliveryStatus.ShouldBe(expected);
        row.SentAt.ShouldBe(expected == "sent" ? StartsAt.AddHours(-24) : null);
        await using var readContext = CreateContext();
        (await readContext.Set<Reminder>().AsNoTracking().SingleAsync(value => value.Id == reminder.Id)).Status
            .ShouldBe(Enum.Parse<ReminderStatus>(expected, true));
    }

    [Fact]
    public async Task Lista_solo_los_recordatorios_pendientes_de_la_cita_en_orden_de_envio()
    {
        await using var context = CreateContext();
        var seed = await SeedAppointmentAsync(context, StartsAt);
        var other = await SeedAppointmentAsync(context, StartsAt.AddDays(1));
        var later = await ScheduleAsync(context, seed.AppointmentId, StartsAt.AddHours(-2));
        var earlier = await ScheduleAsync(context, seed.AppointmentId, StartsAt.AddHours(-24));
        var sent = await ScheduleAsync(context, seed.AppointmentId, StartsAt.AddHours(-12));
        sent.MarkSent(StartsAt.AddHours(-12));
        await context.SaveChangesAsync();
        await ScheduleAsync(context, other.AppointmentId, StartsAt.AddHours(-24));

        await using var readContext = CreateContext();
        var pending = await new ReminderRepository(readContext).ListPendingByAppointmentAsync(seed.AppointmentId, CancellationToken.None);

        pending.Select(reminder => reminder.Id).ShouldBe(new[] { earlier.Id, later.Id });
    }

    [Fact]
    public async Task Lista_los_pendientes_cuya_hora_llego_del_mas_antiguo_al_mas_nuevo_hasta_el_limite()
    {
        await using var context = CreateContext();
        var seed = await SeedAppointmentAsync(context, StartsAt);
        var second = await ScheduleAsync(context, seed.AppointmentId, Now.AddMinutes(10));
        var first = await ScheduleAsync(context, seed.AppointmentId, Now.AddMinutes(5));
        var third = await ScheduleAsync(context, seed.AppointmentId, Now.AddMinutes(20));
        await ScheduleAsync(context, seed.AppointmentId, Now.AddMinutes(40));
        var cancelled = await ScheduleAsync(context, seed.AppointmentId, Now.AddMinutes(1));
        cancelled.Cancel();
        await context.SaveChangesAsync();

        await using var readContext = CreateContext();
        var repository = new ReminderRepository(readContext);
        var due = await repository.ListDueAsync(Now.AddMinutes(30), 10, CancellationToken.None);
        var limited = await repository.ListDueAsync(Now.AddMinutes(30), 2, CancellationToken.None);

        due.Select(reminder => reminder.Id).ShouldBe(new[] { first.Id, second.Id, third.Id });
        limited.Select(reminder => reminder.Id).ShouldBe(new[] { first.Id, second.Id });
    }

    [Fact]
    public async Task Los_recordatorios_listados_quedan_rastreados_para_cambiar_su_estado()
    {
        await using var context = CreateContext();
        var seed = await SeedAppointmentAsync(context, StartsAt);
        var reminder = await ScheduleAsync(context, seed.AppointmentId, Now.AddMinutes(5));

        await using var workContext = CreateContext();
        var due = (await new ReminderRepository(workContext).ListDueAsync(Now.AddMinutes(10), 10, CancellationToken.None))
            .ShouldHaveSingleItem();
        due.MarkSent(Now.AddMinutes(10));
        await workContext.SaveChangesAsync();

        (await ReadRowAsync(reminder.Id)).DeliveryStatus.ShouldBe("sent");
    }

    [Fact]
    public async Task La_plantilla_global_del_recordatorio_automatico_viene_de_los_datos_semilla()
    {
        await using var context = CreateContext();

        var template = await new ReminderTemplateRepository(context)
            .GetActiveGlobalByNameAsync(ReminderTemplate.AutomaticReminderName, CancellationToken.None);

        template.ShouldNotBeNull();
        template.IsGlobal.ShouldBeTrue();
        template.DoctorId.ShouldBeNull();
        template.IsActive.ShouldBeTrue();
        template.Name.ShouldBe(ReminderTemplate.AutomaticReminderName);
        template.Subject.ShouldContain("{SpecialtyName}");
        template.BodyTemplate.ShouldContain("{PatientName}");
    }

    [Fact]
    public async Task Una_plantilla_privada_o_inexistente_no_se_devuelve_como_global()
    {
        await using var context = CreateContext();
        var seed = await SeedAppointmentAsync(context, StartsAt);
        var privateId = await InsertTemplateAsync(context, seed.DoctorId, "Privada del médico");

        var repository = new ReminderTemplateRepository(context);

        (await repository.GetActiveGlobalByNameAsync("Privada del médico", CancellationToken.None)).ShouldBeNull();
        (await repository.GetActiveGlobalByNameAsync("No existe", CancellationToken.None)).ShouldBeNull();
        var byId = await repository.GetByIdAsync(privateId, CancellationToken.None);
        byId.ShouldNotBeNull();
        byId.DoctorId.ShouldBe(seed.DoctorId);
        byId.IsGlobal.ShouldBeFalse();
        (await repository.GetByIdAsync(int.MaxValue, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public void AddInfrastructure_registra_los_repositorios_con_alcance_por_solicitud()
    {
        using var provider = DoctorPersistenceData.Provider("Host=localhost;Database=unused;Username=unused;Password=unused");
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var reminders = first.ServiceProvider.GetRequiredService<IReminderRepository>();
        var templates = first.ServiceProvider.GetRequiredService<IReminderTemplateRepository>();

        reminders.ShouldBeOfType<ReminderRepository>();
        reminders.ShouldBeSameAs(first.ServiceProvider.GetRequiredService<IReminderRepository>());
        reminders.ShouldNotBeSameAs(second.ServiceProvider.GetRequiredService<IReminderRepository>());
        templates.ShouldBeOfType<ReminderTemplateRepository>();
        templates.ShouldNotBeSameAs(second.ServiceProvider.GetRequiredService<IReminderTemplateRepository>());
    }

    private AppDbContext CreateContext() => DoctorPersistenceData.Context(Database.ConnectionString);

    private static async Task AddAsync(AppDbContext context, Reminder reminder)
    {
        new ReminderRepository(context).Add(reminder);
        await context.SaveChangesAsync();
    }

    private static async Task<Reminder> ScheduleAsync(AppDbContext context, int appointmentId, DateTimeOffset scheduledAt)
    {
        var reminder = Reminder.Schedule(appointmentId, null, Message, scheduledAt, Now).Value;
        await AddAsync(context, reminder);
        return reminder;
    }

    private static async Task<ReminderTemplate> GlobalTemplateAsync(AppDbContext context) =>
        (await new ReminderTemplateRepository(context)
            .GetActiveGlobalByNameAsync(ReminderTemplate.AutomaticReminderName, CancellationToken.None)).ShouldNotBeNull();

    private static async Task<int> InsertTemplateAsync(AppDbContext context, int doctorId, string name)
    {
        const string subject = "Asunto";
        const string body = "Hola {PatientName}";
        return (await context.Database.SqlQuery<int>(
            $"""
            INSERT INTO messaging.reminder_template (doctor_id, name, subject, body_template)
            VALUES ({doctorId}, {name}, {subject}, {body})
            RETURNING id AS "Value"
            """).ToListAsync()).Single();
    }

    // El bloque del médico cubre exactamente la cita, como exige el trigger de coherencia de la cita.
    private static async Task<AppointmentSeed> SeedAppointmentAsync(AppDbContext context, DateTimeOffset startsAt)
    {
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var user = UserMother.WithRole(UserRole.Patient, email: Email.Create($"recordatorio-{Guid.NewGuid():N}@correo.com").Value);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        await TestDataSeeder.InsertPatientProfileAsync(context, user.Id);
        var patientId = await context.Patients.Where(patient => patient.UserId == user.Id).Select(patient => patient.Id).SingleAsync();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
            VALUES ({doctor.DoctorId}, NULL, {startsAt}, {startsAt.AddHours(AppointmentMother.DurationHours)}, true)
            """);
        var appointmentId = await TestDataSeeder.InsertAppointmentInStatusAsync(
            context, patientId, doctor.DoctorId, doctor.SpecialtyIds[0], startsAt, AppointmentMother.DurationHours, "confirmed");

        return new AppointmentSeed(appointmentId, doctor.DoctorId, doctor.UserId);
    }

    // Las columnas van en snake_case porque SqlQuery aplica la misma convención de nombres que el contexto.
    private async Task<ReminderRow> ReadRowAsync(int reminderId)
    {
        await using var context = CreateContext();
        return await context.Database.SqlQuery<ReminderRow>(
            $"""
            SELECT id, appointment_id, template_id, message_body, scheduled_at, sent_at, delivery_status, created_by
            FROM messaging.reminder
            WHERE id = {reminderId}
            """).SingleAsync();
    }

    private sealed record AppointmentSeed(int AppointmentId, int DoctorId, int DoctorUserId);

    private sealed record ReminderRow(
        int Id,
        int AppointmentId,
        int? TemplateId,
        string MessageBody,
        DateTimeOffset ScheduledAt,
        DateTimeOffset? SentAt,
        string DeliveryStatus,
        int? CreatedBy);
}
