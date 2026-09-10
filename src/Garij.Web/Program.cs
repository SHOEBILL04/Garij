using Garij.Application;
using Garij.Application.Configuration;
using Garij.Infrastructure;
using Garij.Infrastructure.Persistence;
using Garij.Infrastructure.SeedData;
using Garij.Web.Middleware;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();
builder.Services.Configure<BillingSettings>(builder.Configuration.GetSection(BillingSettings.SectionName));
builder.Services.Configure<LicenseSettings>(builder.Configuration.GetSection(LicenseSettings.SectionName));
builder.Services.Configure<Garij.Infrastructure.ExternalServices.Gemini.GeminiSettings>(builder.Configuration.GetSection(Garij.Infrastructure.ExternalServices.Gemini.GeminiSettings.SectionName));
builder.Services.AddHttpClient<Garij.Infrastructure.ExternalServices.Gemini.ILlmClient, Garij.Infrastructure.ExternalServices.Gemini.GeminiClient>(client => { client.Timeout = TimeSpan.FromSeconds(30); });



builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequireDigit = false;
        options.Password.RequiredLength = 6;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
    })
    .AddEntityFrameworkStores<GarijDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<Garij.Web.Filters.RequireProjectLicenseAttribute>();
});

var app = builder.Build();

app.UseGlobalExceptionMiddleware();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseStaticFiles();
app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

await DbSeeder.SeedAsync(app.Services);

app.Run();
