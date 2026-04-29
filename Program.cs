using Scalar.AspNetCore;
using RemotePrintService.Services;
using Spectre.Console;

namespace RemotePrintService
{
    /// <summary>
    /// 应用程序入口点类
    /// </summary>
    public class Program
    {
        /// <summary>
        /// 应用程序入口点方法
        /// </summary>
        /// <param name="args">命令行参数</param>
        public static void Main(string[] args)
        {
            // 使用 Spectre.Console 显示启动信息
            AnsiConsole.Write(new FigletText("Remote Print Service").Color(Color.Blue));
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[green]正在启动远程打印服务...[/]");
            AnsiConsole.WriteLine();

            var builder = WebApplication.CreateBuilder(args);

            // 配置 Kestrel 服务器绑定到所有网络接口
            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                serverOptions.ListenAnyIP(5035); // 监听所有网络接口的 5035 端口
                
                // 配置请求体限制，解决文件上传超时问题
                serverOptions.Limits.MaxRequestBodySize = 500 * 1024 * 1024; // 500MB，与文件验证限制一致
                serverOptions.Limits.MinRequestBodyDataRate = null; // 禁用最小数据速率限制，允许慢速上传
                serverOptions.Limits.MinResponseDataRate = null; // 禁用最小响应数据速率限制
                serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(2); // 请求头超时时间：2分钟
                serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2); // Keep-Alive超时时间：2分钟
            });

            // 添加控制器服务
            builder.Services.AddControllers();
            
            // 添加 OpenAPI 支持
            builder.Services.AddOpenApi();
            
            // 注册打印机服务
            builder.Services.AddScoped<IPrinterService, LocalPrinterService>();
 
            
            var app = builder.Build();
            
            // 预热打印机系统（解决首次打印失败问题）
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1000); // 等待服务完全启动
                    using (var scope = app.Services.CreateScope())
                    {
                        var printerService = scope.ServiceProvider.GetRequiredService<IPrinterService>();
                        _ = await printerService.GetAvailablePrintersAsync();
                        AnsiConsole.MarkupLine("[green]打印机系统预热完成[/]");
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]打印机系统预热失败（不影响服务运行）: {ex.Message}[/]");
                }
            });

            // 启用 OpenAPI 文档（无论什么环境都启用）
            app.MapOpenApi();

            // 添加授权中间件
            app.UseAuthorization();

            // 配置 Scalar API 文档界面（无论什么环境都启用）
            app.MapScalarApiReference(options =>
            {
                options.AddDocument("v1", "Production API", "api/v1/openapi.json",isDefault: true)
                       .AddDocument("v2-beta", "Beta API", "api/v2-beta/openapi.json")
                       .AddDocument("internal", "Internal API", "internal/openapi.json");
            });

            // 映射控制器路由
            app.MapControllers();

            // 使用 Spectre.Console 显示服务启动完成信息
            AnsiConsole.MarkupLine("[bold green]远程打印服务启动完成！[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[yellow]运行环境:[/] {app.Environment.EnvironmentName}");
            
            // 安全地获取第一个URL，避免序列为空时的异常
            var firstUrl = app.Urls.FirstOrDefault()?.TrimEnd('/') ?? "http://localhost:5035";
            AnsiConsole.MarkupLine($"[yellow]监听地址:[/] {string.Join(", ", app.Urls)}");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[blue]API 文档地址: [/][link]{0}/scalar/v1[/]", firstUrl);

            // 启动应用程序
            app.Run();
        }
    }
}