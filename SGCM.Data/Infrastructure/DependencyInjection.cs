using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Reporting;
using SGCM.Data.Domain.Clinical.MedicalRecords;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Messaging.Reminders;
using SGCM.Data.Domain.Messaging.ReminderTemplates;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.InsuranceProviders;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Notifications;
using SGCM.Data.Infrastructure.Notifications.Channels;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Interceptors;
using SGCM.Data.Infrastructure.Persistence.Readers;
using SGCM.Data.Infrastructure.Persistence.Repositories.Clinical;
using SGCM.Data.Infrastructure.Persistence.Repositories.Identity;
using SGCM.Data.Infrastructure.Persistence.Repositories.Messaging;
using SGCM.Data.Infrastructure.Persistence.Repositories.People;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Infrastructure.Security;
using SGCM.Data.Infrastructure.Services;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Data.Domain.Scheduling.Reports;
using SGCM.Data.Infrastructure.Persistence.Repositories.Scheduling;
using SGCM.Data.Infrastructure.Reporting;

namespace SGCM.Data.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddPersistence();
        services.AddNotifications();
        services.AddSecurity();

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<IRandomNumberGenerator, SystemRandomNumberGenerator>();

        return services;
    }

    private static IServiceCollection AddNotifications(this IServiceCollection services)
    {
        services.AddSingleton(_ => new NotificationTemplateStore());
        services.AddSingleton<ITemplateRenderer, TemplateRenderer>();

        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationChannel, EmailNotificationChannel>();
        services.AddScoped<IEmailTransport, SmtpEmailTransport>();

        return services;
    }

    private static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddScoped<DomainEventsQueue>();
        services.AddScoped<DomainEventDispatcher>();
        services.AddScoped<DomainEventsInterceptor>();

        services.AddDbContext<AppDbContext>((provider, options) => options
            .UseNpgsql(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.SgcmDatabase)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<DomainEventsInterceptor>()));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ISpecialtyRepository, SpecialtyRepository>();
        services.AddScoped<IPatientRepository, PatientRepository>();
        services.AddScoped<IDoctorRepository, DoctorRepository>();
        services.AddScoped<IInsuranceProviderRepository, InsuranceProviderRepository>();
        services.AddScoped<IDoctorAvailabilitySeriesRepository, DoctorAvailabilitySeriesRepository>();
        services.AddScoped<IDoctorAvailabilityRepository, DoctorAvailabilityRepository>();
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<IAppointmentReportLogRepository, AppointmentReportLogRepository>();
        services.AddScoped<IMedicalRecordRepository, MedicalRecordRepository>();
        services.AddScoped<IMedicalRecordAccessLogRepository, MedicalRecordAccessLogRepository>();
        services.AddScoped<IReminderRepository, ReminderRepository>();
        services.AddScoped<IReminderTemplateRepository, ReminderTemplateRepository>();
        // Un exportador por formato: el caso de uso recibe todos y elige el suyo por ReportFormat.
        services.AddSingleton<IReportExporter, PdfReportExporter>();
        services.AddSingleton<IReportExporter, ExcelReportExporter>();
        services.AddSingleton<IReportExporter, WordReportExporter>();
        services.AddScoped<IUserProfileReader, UserProfileReader>();
        services.AddScoped<IDoctorProfileCatalogReader, DoctorProfileCatalogReader>();
        services.AddScoped<ILocationCatalogReader, LocationCatalogReader>();
        services.AddScoped<ITwoFactorStatusReader, TwoFactorStatusReader>();
        services.AddScoped<ISpecialtyListReader, SpecialtyListReader>();
        services.AddScoped<IInsuranceProviderListReader, InsuranceProviderListReader>();
        services.AddScoped<IDoctorSearchReader, DoctorSearchReader>();
        services.AddScoped<IDoctorProfileReader, DoctorProfileReader>();
        services.AddScoped<IInsuranceProviderReader, InsuranceProviderReader>();
        services.AddScoped<IActiveDoctorReader, ActiveDoctorReader>();
        services.AddScoped<IActiveAppointmentReader, ActiveAppointmentReader>();
        services.AddScoped<IAppointmentNotificationReader, AppointmentNotificationReader>();
        services.AddScoped<IPatientAppointmentHistoryReader, PatientAppointmentHistoryReader>();
        services.AddScoped<IAppointmentReportReader, AppointmentReportReader>();
        services.AddScoped<IDoctorScheduleReader, DoctorScheduleReader>();

        return services;
    }

    private static IServiceCollection AddSecurity(this IServiceCollection services)
    {
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ISecureTokenGenerator, SecureTokenGenerator>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddSingleton<IAttemptLimiter, MemoryAttemptLimiter>();
        services.AddSingleton<ITemporaryPasswordGenerator, TemporaryPasswordGenerator>();

        return services;
    }
}
