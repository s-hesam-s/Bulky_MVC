using Microsoft.AspNetCore.Mvc;

namespace BulkyBookWeb.Configuration;

public static class MvcConfiguration
{
    public static IMvcBuilder AddSecureMvc(this IServiceCollection services) =>
        services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

    public static IServiceCollection AddOptionalFacebook(this IServiceCollection services, IConfiguration configuration)
    {
        var appId = configuration["Authentication:Facebook:AppId"];
        var secret = configuration["Authentication:Facebook:AppSecret"];
        if (string.IsNullOrWhiteSpace(appId) && string.IsNullOrWhiteSpace(secret)) return services;
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Configure both Facebook application settings, or leave both unset.");
        services.AddAuthentication().AddFacebook(options => { options.AppId = appId; options.AppSecret = secret; });
        return services;
    }
}
