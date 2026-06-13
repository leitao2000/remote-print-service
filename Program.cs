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
            
            // 添加 CORS 服务
            builder.Services.AddCors();
            
            // 注册打印机服务（本地 + 网络组合服务）
            builder.Services.AddScoped<LocalPrinterService>();
            builder.Services.AddScoped<NetworkPrinterService>();
            builder.Services.AddScoped<IPrinterService, CompositePrinterService>();
 
            
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

            // 全局异常处理中间件
            app.Use(async (context, next) =>
            {
                try
                {
                    await next();
                }
                catch (Exception ex)
                {
                    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                    logger.LogError(ex, "未处理的异常: {Method} {Path}", context.Request.Method, context.Request.Path);

                    context.Response.StatusCode = 500;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        success = false,
                        message = "服务器内部错误",
                        errorCode = "UNHANDLED_EXCEPTION"
                    });
                }
            });

            // 请求日志中间件
            app.Use(async (context, next) =>
            {
                var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var method = context.Request.Method;
                var path = context.Request.Path;

                logger.LogInformation("请求开始: {Method} {Path}", method, path);

                await next();

                stopwatch.Stop();
                var statusCode = context.Response.StatusCode;
                var level = statusCode >= 400 ? LogLevel.Warning : LogLevel.Information;
                logger.Log(level, "请求完成: {Method} {Path} -> {StatusCode} ({ElapsedMs}ms)",
                    method, path, statusCode, stopwatch.ElapsedMilliseconds);
            });

            // CORS 配置
            app.UseCors(policy => policy
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader());

            // 添加授权中间件
            app.UseAuthorization();

            // 健康检查端点
            app.MapGet("/health", () => Results.Ok(new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                service = "RemotePrintService"
            })).ExcludeFromDescription();

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