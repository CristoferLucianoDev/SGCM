using Serilog;
using SGCM.Data.Infrastructure;
using SGCM.Web.Hosting;

namespace SGCM.Web;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddSgcmLogging(builder.Configuration);
        builder.Services.AddSgcmOptions(builder.Configuration);
        builder.Services.AddInfrastructure();
        builder.Services.AddSgcmApiDocumentation();

        builder.Services.AddControllersWithViews();

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

        app.UseAuthorization();

        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");

        app.Run();
    }
}