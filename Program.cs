using System.Text;
using System.Security.Claims;
using MarketApp.Configuration;
using MarketApp.Data;
using MarketApp.Logging;
using MarketApp.Middleware;
using MarketApp.Models;
using MarketApp.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<DatabaseLogQueue>();
builder.Services.AddSingleton<ILoggerProvider, DatabaseLoggerProvider>();
builder.Services.AddHostedService<DatabaseLogWriterService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();

var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()
    ?? throw new InvalidOperationException("Jwt ayarları appsettings.json içinde tanımlı olmalıdır.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
            RoleClaimType = ClaimTypes.Role
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddOpenApi();

var app = builder.Build();

await SeedAdminUserAsync(app.Services);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseMiddleware<GlobalExceptionMiddleware>();

app.UseHttpsRedirection();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/users", () => Results.Redirect("/users.html"));
app.MapGet("/admin/users", () => Results.Redirect("/users.html"));
app.MapGet("/user-management", () => Results.Redirect("/users.html"));
app.MapGet("/dashboard", () => Results.Redirect("/dashboard.html"));
app.MapGet("/admin/dashboard", () => Results.Redirect("/dashboard.html"));
app.MapGet("/logs", () => Results.Redirect("/logs.html"));
app.MapGet("/admin/logs", () => Results.Redirect("/logs.html"));
app.MapGet("/log-management", () => Results.Redirect("/logs.html"));
app.MapControllers();

app.Run();

static async Task SeedAdminUserAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

    await context.Database.MigrateAsync();

    var adminUsername = configuration["SeedAdmin:Username"] ?? "admin";
    var adminPassword = configuration["SeedAdmin:Password"] ?? "Admin123!";

    if (string.IsNullOrWhiteSpace(adminUsername) || string.IsNullOrWhiteSpace(adminPassword))
        throw new InvalidOperationException("Seed admin kullanıcısı için kullanıcı adı ve şifre tanımlanmalıdır.");

    adminUsername = adminUsername.Trim();

    var existingUser = await context.Users
        .FirstOrDefaultAsync(u => u.Username.ToLower() == adminUsername.ToLower());

    if (existingUser is not null)
    {
        existingUser.Role = UserRoles.Admin;
        if (!BCrypt.Net.BCrypt.Verify(adminPassword, existingUser.PasswordHash))
        {
            existingUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword);
        }

        await context.SaveChangesAsync();
        return;
    }

    if (await context.Users.AnyAsync(u => u.Role == UserRoles.Admin))
    {
        return;
    }

    context.Users.Add(new User
    {
        Username = adminUsername,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
        Role = UserRoles.Admin
    });

    await context.SaveChangesAsync();
}
