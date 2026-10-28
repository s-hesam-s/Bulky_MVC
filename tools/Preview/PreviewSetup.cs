using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

public static class PreviewSetup
{
    public static string Root(string marker)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, marker))) root = root.Parent;
        return root?.FullName ?? throw new InvalidOperationException("Run this preview inside its repository.");
    }
    public static WebApplicationBuilder Builder(string root, int port, string? webRoot = null, string? applicationName = null)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production", ContentRootPath = Path.Combine(root, "tools", "Preview"), WebRootPath = webRoot, ApplicationName = applicationName });
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection();
        builder.Logging.ClearProviders(); builder.Logging.AddSimpleConsole();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        return builder;
    }
    public static void Guard(WebApplication app, bool decorate = false)
    {
        app.Use(async (context, next) =>
        {
            if (!HttpMethods.IsGet(context.Request.Method)) { context.Response.StatusCode = 405; return; }
            context.Response.Headers["Cache-Control"] = "no-store";
            if (!decorate) { await next(); return; }
            var original = context.Response.Body; using var buffer = new MemoryStream(); context.Response.Body = buffer;
            try
            {
                await next(); buffer.Position = 0; context.Response.Body = original;
                if (context.Response.ContentType?.StartsWith("text/html") == true)
                {
                    var html = await new StreamReader(buffer).ReadToEndAsync();
                    html = Regex.Replace(html, @"<script[^>]+src=['""]https://cdn\.tiny\.cloud/[^'""]+['""][^>]*>.*?</script>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    html = html.Replace("<body>", "<body><aside style='padding:12px;background:#173641;color:white;font:14px system-ui;text-align:center'>READ-ONLY FIXTURE · Existing views · Sample data · No database or providers. Only the displayed preview routes work.</aside>");
                    await context.Response.WriteAsync(html);
                }
                else await buffer.CopyToAsync(original);
            }
            finally { context.Response.Body = original; }
        });
        app.MapGet("/preview-info", () => new { mode = "read-only fixture", database = false, externalProviders = false, originalHost = false });
    }
}

public sealed class PreviewControllers : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    { feature.Controllers.Clear(); feature.Controllers.Add(typeof(PreviewController).GetTypeInfo()); }
}
