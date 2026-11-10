using Microsoft.EntityFrameworkCore;
using Project.Models;
using Project.Data;
using Project.Configuration;
using System;
using Microsoft.AspNetCore.Authentication.Cookies;
using Project.Auth;
using System.Globalization;


CultureInfo culture;
try
{
    culture = new CultureInfo("en-US");
}
catch (CultureNotFoundException)
{
  
    culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
    culture.NumberFormat.CurrencySymbol = "$";
}
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder(
        "/Staff",
        "LibraryStaff");

    options.Conventions.AuthorizePage(
        "/Inventory",
        "LibraryStaff");

    options.Conventions.AuthorizePage(
        "/AddInventoryItem",
        "LibraryStaff");

    options.Conventions.AuthorizePage(
        "/CheckoutActivity",
        "LibraryStaff");

    options.Conventions.AuthorizePage(
        "/BorrowerRentals",
        "LibraryStaff");

    options.Conventions.AuthorizePage(
        "/Checkout",
        "LibraryStaff");

    options.Conventions.AuthorizePage(
        "/CollectionPerformance",
        "LibraryAdmin");

    options.Conventions.AuthorizePage(
        "/OverdueFines",
        "LibraryAdmin");

    options.Conventions.AuthorizePage(
        "/EquipmentMaintenance",
        "LibraryAdmin");
});

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";

        options.EventsType = typeof(AccountCookieEvents);

        options.Cookie.Name = "Library.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;

        options.Cookie.SecurePolicy =
            builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;

        options.ExpireTimeSpan = TimeSpan.FromHours(2);
        options.SlidingExpiration = false;
    });

builder.Services.AddDbContext<LibraryDbContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(cs))
        throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection using user secrets or environment variables. See README.md.");
    options.UseMySql(cs, ServerVersion.AutoDetect(cs));
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("LibraryAdmin", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("Admin");
    });

    options.AddPolicy("LibraryStaff", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("Staff", "Admin");
    });
});

builder.Services.AddScoped<AccountCookieEvents>();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();


app.Run();
