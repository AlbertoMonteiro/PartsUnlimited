using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using PartsUnlimited;
using PartsUnlimited.Areas.Admin;
using PartsUnlimited.Models;
using PartsUnlimited.Queries;
using PartsUnlimited.Recommendations;
using PartsUnlimited.Search;
using PartsUnlimited.Security;
using PartsUnlimited.Telemetry;
using PartsUnlimited.WebsiteConfiguration;

var builder = WebApplication.CreateBuilder(args);
builder.Host.ConfigureAppConfiguration((hostingContext, config) =>
{
    config.Sources.Clear();
    config.AddJsonFile("config.json", optional: true);
    config.AddJsonFile($"config.{hostingContext.HostingEnvironment.EnvironmentName}.json", optional: true);
});

// Add services to the container.
builder.Services.AddControllersWithViews();
var Configuration = builder.Configuration;
var services = builder.Services;
var runningOnMono = Type.GetType("Mono.Runtime") != null;
var sqlConnectionString = Configuration[ConfigurationPath.Combine("ConnectionStrings", "DefaultConnectionString")];
var useInMemoryDatabase = string.IsNullOrWhiteSpace(sqlConnectionString);

if (useInMemoryDatabase || runningOnMono)
{
    sqlConnectionString = "";
}

// Add EF services to the services container
services.AddScoped<PartsUnlimitedContext>(_ => new PartsUnlimitedContext(sqlConnectionString));
services.AddScoped<IPartsUnlimitedContext>(sp => sp.GetRequiredService<PartsUnlimitedContext>());

// Add Identity services to the services container
services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<PartsUnlimitedContext>()
    .AddDefaultTokenProviders();

// Configure admin policies
services.AddAuthorization(auth =>
{
    auth.AddPolicy(AdminConstants.Role,
        authBuilder =>
        {
            authBuilder.RequireClaim(AdminConstants.ManageStore.Name, AdminConstants.ManageStore.Allowed);
        });

});

// Add implementations
services.AddSingleton<IMemoryCache, MemoryCache>();
services.AddScoped<IOrdersQuery, OrdersQuery>();
services.AddScoped<IRaincheckQuery, RaincheckQuery>();

services.AddSingleton<ITelemetryProvider, EmptyTelemetryProvider>();
services.AddScoped<IProductSearch, StringContainsProductSearch>();

SetupRecommendationService(services);

services.AddScoped<IWebsiteOptions>(p =>
{
    var telemetry = p.GetRequiredService<ITelemetryProvider>();

    return new ConfigurationWebsiteOptions(Configuration.GetSection("WebsiteOptions"), telemetry);
});

services.AddScoped<IApplicationInsightsSettings>(p =>
{
    return new ConfigurationApplicationInsightsSettings(Configuration.GetSection(ConfigurationPath.Combine("Keys", "ApplicationInsights")));
});

services.AddApplicationInsightsTelemetry(Configuration);


// We need access to these settings in a static extension method, so DI does not help us :(
ContentDeliveryNetworkExtensions.Configuration = new ContentDeliveryNetworkConfiguration(Configuration.GetSection("CDN"));

// Add MVC services to the services container
services.AddMvc(o => o.EnableEndpointRouting = false);

//Add InMemoryCache
services.AddSingleton<IMemoryCache, MemoryCache>();

// Add session related services.
//services.AddCaching();
services.AddSession();

// antes e depois do build
var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    //var services = scope.ServiceProvider;

    try
    {
        //Populates the PartsUnlimited sample data
        SampleData.InitializePartsUnlimitedDatabaseAsync(scope.ServiceProvider).Wait();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred seeding the DB.");
    }
}

// Configure Session.
app.UseSession();

// Add static files to the request pipeline
app.UseStaticFiles();

// Add cookie-based authentication to the request pipeline
app.UseAuthentication();

AppBuilderLoginProviderExtensions.AddLoginProviders(services, new ConfigurationLoginProviders(Configuration.GetSection("Authentication")));
// Add login providers (Microsoft/AzureAD/Google/etc).  This must be done after `app.UseIdentity()`
//app.AddLoginProviders( new ConfigurationLoginProviders(Configuration.GetSection("Authentication")));

// Add MVC to the request pipeline
app.UseMvc(routes =>
{
    routes.MapRoute(
        name: "areaRoute",
        template: "{area:exists}/{controller}/{action}",
        defaults: new { action = "Index" });

    routes.MapRoute(
        name: "default",
        template: "{controller}/{action}/{id?}",
        defaults: new { controller = "Home", action = "Index" });

    routes.MapRoute(
        name: "api",
        template: "{controller}/{id?}");
});
_ = app.Environment.EnvironmentName switch
{
    "Development" => app.UseDeveloperExceptionPage(),
    _ => app.UseExceptionHandler("/Home/Error"),
};

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

void SetupRecommendationService(IServiceCollection services)
{
    var azureMlConfig = new AzureMLFrequentlyBoughtTogetherConfig(Configuration.GetSection(ConfigurationPath.Combine("Keys", "AzureMLFrequentlyBoughtTogether")));

    // If keys are not available for Azure ML recommendation service, register an empty recommendation engine
    if (string.IsNullOrEmpty(azureMlConfig.AccountKey) || string.IsNullOrEmpty(azureMlConfig.ModelName))
    {
        services.AddSingleton<IRecommendationEngine, EmptyRecommendationsEngine>();
    }
    else
    {
        services.AddSingleton<IAzureMLAuthenticatedHttpClient, AzureMLAuthenticatedHttpClient>();
        services.AddSingleton<IAzureMLFrequentlyBoughtTogetherConfig>(azureMlConfig);
        services.AddScoped<IRecommendationEngine, AzureMLFrequentlyBoughtTogetherRecommendationEngine>();
    }
}

