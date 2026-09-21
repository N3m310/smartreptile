using System.Reflection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using SmartReptile.Api.Endpoints;
using SmartReptile.Api.Hubs;
using SmartReptile.Api.Middleware;
using SmartReptile.Infrastructure;
using SmartReptile.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// ---- logging -----------------------------------------------------------------------------------------
// Structured logs with a correlation id per request (FR-18 BR-18.3). JSON in containers, readable console locally.
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
}

// ---- services ----------------------------------------------------------------------------------------
builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails();
builder.Services.AddSignalR();

builder.Services.AddSmartReptileInfrastructure(builder.Configuration);

// CORS allowlist: explicit origins only. An empty list means "no cross-origin access" outside Development.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (allowedOrigins.Length > 0)
    {
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    }
    else
    {
        policy.SetIsOriginAllowed(_ => false);
    }
}));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "SmartReptile API",
        Version = "v1",
        Description = "IoT terrarium monitoring and alerting. MQTT over TLS for telemetry, REST for history, "
                    + "SignalR for live updates. See docs/07-appendices/03-mqtt-and-rest-api-spec.md.",
        License = new() { Name = "MIT" },
    });

    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste a user access token (15 minute lifetime).",
    });
});

var app = builder.Build();

// ---- pipeline ----------------------------------------------------------------------------------------
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

app.UseMiddleware<CorrelationIdMiddleware>();

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    // RFC 7807 for unexpected failures; expected failures return Result-based problems from the endpoints.
    var problem = new ProblemDetails
    {
        Title = "An unexpected error occurred",
        Status = StatusCodes.Status500InternalServerError,
        Type = "https://smartreptile.example/problems/internal_error",
        Detail = "The request could not be completed. Quote the traceId when reporting this.",
    };

    problem.Extensions["code"] = "internal_error";
    problem.Extensions["traceId"] = context.TraceIdentifier;

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(problem);
}));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartReptile API v1"));
}

app.UseCors();

var applicationVersion = app.Configuration["Version"]
    ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
    ?? "0.1.0";

app.MapOpsEndpoints(applicationVersion);
app.MapHub<TelemetryHub>("/hubs/telemetry");

app.MapGet("/", () => Results.Ok(new
{
    service = "SmartReptile API",
    version = applicationVersion,
    endpoints = new[] { "/health/live", "/health/ready", "/metrics", "/version", "/swagger", "/hubs/telemetry" },
    docs = "docs/README.md",
})).WithTags("ops");

// ---- start-up initialisation -------------------------------------------------------------------------
// The listener starts FIRST, then migrations/seeding run. Binding the port before touching the database keeps
// the process responsive when SQL Server is slow or down: /health/live answers immediately and /health/ready
// reports the failure, instead of the whole API looking dead for the duration of the connection retries
// (NFR-03 degraded mode). A failure here degrades the API; it does not kill it.
await app.StartAsync();

await using (var scope = app.Services.CreateAsyncScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        var outcome = await initializer.InitializeAsync();
        logger.LogInformation(
            "Start-up complete: migrationsApplied={MigrationsApplied}, profilesSeeded={ProfilesSeeded}, version={Version}",
            outcome.MigrationsApplied,
            outcome.ProfilesSeeded,
            applicationVersion);
    }
    catch (Exception ex)
    {
        logger.LogError(
            ex,
            "Database initialisation failed; the API keeps serving in degraded mode and /health/ready reports unhealthy");
    }
}

await app.WaitForShutdownAsync();

/// <summary>Exposed so integration tests can reference the API assembly.</summary>
public partial class Program;
