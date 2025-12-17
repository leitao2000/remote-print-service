using Scalar.AspNetCore;
using RemotePrintService.Services;

namespace RemotePrintService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();
            
            // Register printer service (使用 Adobe Acrobat DC 打印 PDF)
            builder.Services.AddScoped<IPrinterService, LocalPrinterService>();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

            app.UseAuthorization();

            // Chain multiple documents
            app.MapScalarApiReference(options =>
            {
                options.AddDocument("v1", "Production API", "api/v1/openapi.json")
                       .AddDocument("v2-beta", "Beta API", "api/v2-beta/openapi.json", isDefault: true)
                       .AddDocument("internal", "Internal API", "internal/openapi.json");
            });

            app.MapControllers();

            app.Run();
        }
    }
}