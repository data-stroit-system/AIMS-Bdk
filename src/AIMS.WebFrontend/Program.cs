using AIMS.Infrastructure;
using AIMS.Infrastructure.DependencyInjection;
using AIMS.Infrastructure.DomainEvents;
using AIMS.Infrastructure.IdentityClass;
using AIMS.SharedKernel.Interfaces;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
using System.Net;

// Bootstrap logger: console-only, active only while the host is being built
// (including InitializeDatabase()'s schema-init pass below, which runs before
// builder.Build()). Replaced once UseSerilog's ReadFrom.Configuration below
// picks up the "Serilog" section of appsettings*.json — that section previously
// went unused because UseSerilog() was called with no arguments, which just
// adopts a logger already fully built in code and ignores config entirely.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // Autofac hosts the container (IServiceCollection registrations below are
    // Populate()d into it automatically). Autofac-specific wiring — assembly-scanned
    // calculation strategies + their decorator — lives in CalculationsModule.
    builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
    builder.Host.ConfigureContainer<ContainerBuilder>(container =>
        container.RegisterModule<CalculationsModule>());

    builder.Services.AddDapperContext(builder.Configuration);
    builder.Services.AddAuditTrail();
    builder.Services.AddTransient<IDomainEventDispatcher, DomainEventDispatcher>();

    builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(opt => opt.SignIn.RequireConfirmedAccount = false)
        .AddUserStore<DapperUserStore>()
        .AddRoleStore<DapperRoleStore>()
        .AddDefaultTokenProviders();

    builder.Services.ConfigureApplicationCookie(opt =>
    {
        opt.LoginPath = "/Account/Login";
        opt.LogoutPath = "/Account/Logout";
        opt.AccessDeniedPath = "/Account/AccessDenied";
    });

    builder.Services.AddRazorPages();
    builder.Services.AddHttpClient();
    builder.Services.InitializeDatabase();

    // nginx terminates TLS in production and forwards the scheme; trust its
    // headers so Request.Scheme/IsHttps are https behind the proxy (which also
    // makes cookie-auth cookies Secure over https via the default SameAsRequest
    // policy). Trust loopback only — nginx is the sole reverse proxy.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
        options.KnownProxies.Add(IPAddress.Loopback);
    });

    var app = builder.Build();

    await app.Services.SeedRolesAndAdminUserAsync();

    // Must run first so the rest of the pipeline (incl. request logging) sees
    // the forwarded https scheme.
    app.UseForwardedHeaders();

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }
    else
    {
        app.UseExceptionHandler("/Error");
    }

    app.UseStaticFiles();

    app.UseRouting();

    app.UseAuthorization();

    app.MapRazorPages();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "AIMS.WebFrontend terminated unexpectedly during startup");
}
finally
{
    Log.CloseAndFlush();
}
