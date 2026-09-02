using AIMS.Infrastructure;
using AIMS.Infrastructure.DependencyInjection;
using AIMS.Infrastructure.DomainEvents;
using AIMS.Infrastructure.IdentityClass;
using AIMS.SharedKernel.Interfaces;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
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
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        options.KnownProxies.Add(IPAddress.Loopback);
    });

    var app = builder.Build();

    await app.Services.SeedRolesAndAdminUserAsync();

    // Must run first so the rest of the pipeline (incl. request logging) sees
    // the forwarded https scheme.
    app.UseForwardedHeaders();

    app.UseSerilogRequestLogging();

    // ForwardedHeaders must run before anything that looks at the request
    // scheme (UseHttpsRedirection, UseHsts, absolute-URL generation in the
    // cookie-auth challenge). nginx is the only proxy in front of Kestrel
    // and runs on this same box (loopback), so trust just it — an outside
    // attacker can't spoof X-Forwarded-Proto. In the Cloudflare Full-strict
    // setup (deploy.sh NGINX_ENABLE_SSL=true) nginx forwards $scheme=https
    // here, so the app knows it's serving HTTPS and the redirect/HSTS
    // below fire correctly without a loop. In the legacy plain-HTTP prod
    // (NGINX_ENABLE_SSL=false) nginx forwards $scheme=http; with no
    // HttpsRedirection:HttpsPort configured anywhere in appsettings*,
    // UseHttpsRedirection stays a no-op (just logs a warning) and UseHsts
    // over plain HTTP is ignored by browsers — so the existing prod-on-:81
    // deploy keeps working unchanged.
    var forwardedOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    };
    forwardedOptions.KnownProxies.Add(IPAddress.Loopback);
    forwardedOptions.KnownProxies.Add(IPAddress.IPv6Loopback);
    app.UseForwardedHeaders(forwardedOptions);

    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }
    else
    {
        app.UseExceptionHandler("/Error");
        // HSTS only in non-Dev (once a browser pins it, it's sticky for a
        // year — don't lock yourself out of http://localhost in Dev).
        app.UseHsts();
    }

    app.UseHttpsRedirection();

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
