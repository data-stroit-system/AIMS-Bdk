using System.Reflection;
using AIMS.WebFrontend.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using AIMS.Infrastructure;
using AIMS.Infrastructure.DomainEvents;
using AIMS.SharedKernel.Interfaces;
using Autofac.Extensions.DependencyInjection;
using AIMS.Infrastructure.Data;
using AIMS.Infrastructure.IdentityClass;
using Autofac.Core;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext(builder.Configuration);
builder.Services.AddAuditTrail(); // Register audit trail services
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddTransient<IDomainEventDispatcher, DomainEventDispatcher>();

builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(opt => opt.SignIn.RequireConfirmedAccount = false)
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultUI()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(opt =>
{
    opt.LoginPath = "/Account/Login";
    opt.LogoutPath = "/Account/Logout";
    opt.AccessDeniedPath = "/Account/AccessDenied";
});
//builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true)
//    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddRazorPages();
ContainerSetup.InitializeWeb(Assembly.GetExecutingAssembly(), builder.Services);
builder.Services.SeedData();
builder.Services.AddAutofac(c => 
    new AutofacServiceProviderFactory());
var app = builder.Build();

// Seed default roles and admin user
await app.Services.SeedRolesAndAdminUserAsync();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();

app.Run();
