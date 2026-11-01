using Serilog;
using SGCM.Data.Infrastructure;
using SGCM.Web.Hosting;
using SGCM.Data.Application;
using SGCM.Web.Jobs;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Web.Api.Infrastructure;

namespace SGCM.Web;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddSgcmLogging(builder.Configuration);
        builder.Services.AddSgcmOptions(builder.Configuration);
        builder.Services.AddSgcmAuthentication();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
        builder.Services.AddSgcmRateLimiting();
        builder.Services.AddApplication();
        builder.Services.AddInfrastructure();
        builder.Services.AddSgcmJobs();
        builder.Services.AddSgcmApiDocumentation();

        builder.Services.AddControllersWithViews(options =>
            // La validación de datos la hace FluentValidation; así todo error de la API lleva su código.
            options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);

        var app = builder.Build();

        app.UseSerilogRequestLogging();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Home/Error");
            app.UseHsts();
        }

        app.UseSgcmApiDocumentation();

        app.UseHttpsRedirection();
        app.UseStaticFiles();

        app.UseRouting();

        // Después de UseRouting, para que vea la política del endpoint.
        app.UseRateLimiter();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");

        app.Run();
    }
}
