using System.Text.Json.Serialization;
using Ventia.Api;
using Ventia.Api.Auth;
using Ventia.Api.Filters;
using Ventia.Api.Middleware;
using Ventia.Api.Services;
using Ventia.Application;
using Ventia.Application.Abstractions;
using Ventia.Infrastructure;
using Ventia.Infrastructure.Auth;
using Ventia.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// la API solo recibe JSON (1 MB sobra y frena peticiones gigantes)
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024 * 1024);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<ITenantContext, TenantContext>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// la autorizacion la aplica la politica de respaldo (ver Auth/Policies.cs) (denegar por defecto)
builder.Services
    .AddControllers(options => options.Filters.Add<ValidationFilter>())
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = TokenService.CreateSigningKey(jwt.Key),
            NameClaimType = AppClaims.Email,
            RoleClaimType = AppClaims.Role,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        // revisa en la base de datos la cuenta y el negocio y carga el TenantId de la sesion
        options.Events = new JwtBearerEvents { OnTokenValidated = SessionValidator.OnTokenValidated };
    });
builder.Services.AddAuthorization(Policies.Configure);
builder.Services.AddVentiaRateLimits(builder.Configuration);

builder.Services.AddOpenApi();

var app = builder.Build();

// frontend compilado (solo existe en la imagen de docker y en desarrollo lo sirve ng serve)
var frontend = SpaFrontend.TryLoad(app.Environment.WebRootPath);

app.UseSecurityHeaders(app.Environment.IsDevelopment(), frontend?.ContentSecurityPolicy);
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Ventia API"));
}
else if (builder.Configuration.GetValue("Https:Redirect", true))
{
    // HSTS (el navegador recuerda usar siempre HTTPS con este dominio)
    // desactivar la redireccion a https solo sirve para probar la imagen de docker en local por http
    app.UseHsts();
    app.UseHttpsRedirection();
}

var staticFiles = new StaticFileOptions { OnPrepareResponse = SpaFrontend.SetCacheHeaders };
if (frontend is not null)
{
    app.UseDefaultFiles();
    app.UseStaticFiles(staticFiles);
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

// sin datos y sin sesion (lo usa el healthcheck de docker)
app.MapGet("/api/health", async (AppDbContext db, CancellationToken ct) =>
        await db.Database.CanConnectAsync(ct)
            ? Results.Ok(new { status = "ok" })
            : Results.Json(new { status = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable))
    .AllowAnonymous();

if (frontend is not null)
{
    // cualquier ruta del frontend sin punto (como /app/agenda o /cita/abc) devuelve index.html y Angular decide la pantalla
    // las rutas /api desconocidas y los archivos que no existen siguen dando 404
    app.MapFallbackToFile("{*path:regex(^(?!api(/|$))[^.]*$)}", "index.html", staticFiles).AllowAnonymous();
}

await DbSeeder.SeedAsync(app.Services);

app.Run();

// expuesto para pruebas de integracion (WebApplicationFactory)
public partial class Program;
