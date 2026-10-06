using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SmartReptile.Api.Endpoints;
using SmartReptile.Api.Hubs;
using SmartReptile.Api.Middleware;
using SmartReptile.Api.Security;
using SmartReptile.Application.Devices;
using SmartReptile.Application.Identity;
using SmartReptile.Application.Terrariums;
using SmartReptile.Domain.Identity;
using SmartReptile.Infrastructure;
using SmartReptile.Domain.Devices;
using SmartReptile.Infrastructure.Options;
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

// ---- problem bodies for framework-generated responses ------------------------------------------------
// 401, 403 and 429 are produced by middleware rather than by an endpoint, so their bodies are declared here:
// every failure this API returns should look the same to a client (RFC 7807 with a stable code).
var unauthorizedBody = new
{
    type = "https://smartreptile.example/problems/unauthenticated",
    title = "A valid access token is required.",
    status = StatusCodes.Status401Unauthorized,
    code = "unauthenticated",
};

var forbiddenBody = new
{
    type = "https://smartreptile.example/problems/insufficient_role",
    title = "Your role does not allow this action.",
    status = StatusCodes.Status403Forbidden,
    code = "insufficient_role",
};

var rateLimitedBody = new
{
    type = "https://smartreptile.example/problems/rate_limited",
    title = "Too many requests. Try again shortly.",
    status = StatusCodes.Status429TooManyRequests,
    code = "rate_limited",
};

// ---- authentication (FR-01) --------------------------------------------------------------------------
var jwtSettings = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Claims keep the names they were issued with, so "sub" and "role" are read verbatim instead of being
        // remapped into the legacy XML-schema claim URIs the handler defaults to.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role",
        };

        options.Events = new JwtBearerEvents
        {
            // The default challenge and forbid responses are empty bodies. Everything else this API returns is an
            // RFC 7807 document with a stable code, so an unauthenticated or under-privileged call is put in the
            // same shape rather than making the client special-case them.
            OnChallenge = async context =>
            {
                context.HandleResponse();
                await WriteProblemAsync(context.HttpContext, StatusCodes.Status401Unauthorized, unauthorizedBody);
            },
            OnForbidden = async context =>
                await WriteProblemAsync(context.HttpContext, StatusCodes.Status403Forbidden, forbiddenBody),
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Policies, not role strings, at the endpoint (§02-design/06 §3). Owner is listed in the Technician policy
    // explicitly rather than implied: an Owner may do everything a Technician may, and saying so is cheaper than
    // a hierarchy rule that has to be discovered.
    options.AddPolicy(AuthorizationPolicies.Owner, policy =>
        policy.RequireClaim("role", nameof(UserRole.Owner)));

    options.AddPolicy(AuthorizationPolicies.Technician, policy =>
        policy.RequireClaim("role", nameof(UserRole.Owner), nameof(UserRole.Technician)));
});

/// <summary>Writes an RFC 7807 body for a response the framework would otherwise leave empty.</summary>
static Task WriteProblemAsync(HttpContext httpContext, int statusCode, object body)
{
    httpContext.Response.StatusCode = statusCode;
    httpContext.Response.ContentType = "application/problem+json";
    return httpContext.Response.WriteAsync(JsonSerializer.Serialize(body));
}

// The application services, wired where both Application and Infrastructure are visible.
builder.Services.AddScoped<AuthService>();
builder.Services.AddSingleton(sp => new AuthSettings(
    sp.GetRequiredService<IOptions<JwtOptions>>().Value.RefreshTokenDays,
    sp.GetRequiredService<IOptions<PasswordResetOptions>>().Value.CodeMinutes));

builder.Services.AddScoped<DeviceProvisioningService>();
builder.Services.AddSingleton(sp =>
{
    var provisioning = sp.GetRequiredService<IOptions<ProvisioningOptions>>().Value;
    var protection = sp.GetRequiredService<IOptions<OnboardingProtectionOptions>>().Value;

    return new ProvisioningSettings(
        provisioning.ClaimCodeAlphabet,
        provisioning.ClaimCodeLength,
        provisioning.ClaimCodeMinutes,
        new OnboardingLimits(
            protection.MaxAttemptsPerIp,
            protection.WindowMinutes,
            protection.MaxAttemptsPerHourGlobal));
});

// The terrarium read surface (FR-03, FR-08, FR-09 — roadmap task 2.8). Its settings are mapped from the
// Defaults section for the same reason as ProvisioningSettings: Application does not read configuration.
builder.Services.AddScoped<TerrariumService>();
builder.Services.AddSingleton(sp =>
{
    var defaults = sp.GetRequiredService<IOptions<DefaultsOptions>>().Value;

    return new TerrariumSettings(defaults.TimeZoneId, defaults.SilentAfterIntervals);
});

// ---- rate limiting (§07-appendices/03 §5) ------------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // A rejected request still has to look like every other failure: RFC 7807 with a stable code. The default
    // rejection is an empty body, which would make the client show "unknown error" for the one response that
    // tells it to slow down.
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        var response = context.HttpContext.Response;
        response.ContentType = "application/problem+json";

        await response.WriteAsync(JsonSerializer.Serialize(rateLimitedBody), cancellationToken);
    };

    // Auth endpoints are limited per client address rather than per account. Limiting per account would reward
    // an attacker who sprays identifiers: each one would get its own fresh budget.
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

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

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

var applicationVersion = app.Configuration["Version"]
    ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
    ?? "0.1.0";

app.MapOpsEndpoints(applicationVersion);
app.MapAuthEndpoints();
app.MapDeviceEndpoints();
app.MapTerrariumEndpoints();
app.MapHub<TelemetryHub>("/hubs/telemetry");

app.MapGet("/", () => Results.Ok(new
{
    service = "SmartReptile API",
    version = applicationVersion,
    endpoints = new[]
    {
        "/health/live", "/health/ready", "/metrics", "/version", "/swagger", "/hubs/telemetry",
        "/api/v1/auth/register", "/api/v1/auth/login", "/api/v1/auth/refresh", "/api/v1/auth/logout",
        "/api/v1/auth/me", "/api/v1/auth/change-password",
        "/api/v1/devices/self-register", "/api/v1/devices/claim",
        "/api/v1/devices/{deviceId}/rotate-secret", "/api/v1/devices/{deviceId}/revoke",
        "/api/v1/terrariums", "/api/v1/terrariums/{terrariumId}",
        "/api/v1/terrariums/{terrariumId}/readings/latest", "/api/v1/terrariums/{terrariumId}/readings",
        "/api/v1/terrariums/{terrariumId}/coverage",
    },
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
