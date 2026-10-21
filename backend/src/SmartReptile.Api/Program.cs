using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SmartReptile.Api.Endpoints;
using SmartReptile.Api.Hubs;
using SmartReptile.Api.Middleware;
using SmartReptile.Api.OpenApi;
using SmartReptile.Api.Security;
using SmartReptile.Application.Admin;
using SmartReptile.Application.Devices;
using SmartReptile.Application.Identity;
using SmartReptile.Application.Ingest;
using SmartReptile.Application.Reference;
using SmartReptile.Application.Species;
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

// The live push of FR-09: an Application port implemented here, because the hub it broadcasts through belongs to
// this project. A singleton like the hub context it holds, and resolved by the ingest pipeline's fan-out.
builder.Services.AddSingleton<ITelemetryBroadcaster, SignalRTelemetryBroadcaster>();

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
    // The code stays `insufficient_role` because it is what a deployed client already handles; the title says what
    // the refusal now means, which since 2026-10-17 is a missing permission rather than a missing role.
    type = "https://smartreptile.example/problems/insufficient_role",
    title = "Your account does not hold the permission this action needs.",
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

            // A browser cannot put a header on the WebSocket handshake, so SignalR's own convention is to pass the
            // token in the query string. Accepted on the hub path only: a token in a URL reaches logs, proxies and
            // referrer headers, which is a cost worth paying for the one route that cannot avoid it and nowhere
            // else.
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];

                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization(options =>
{
    // One policy per permission in the catalogue (FR-18). The names are the codes, so an endpoint asks for the
    // capability it needs and no route translates a role into one — see AuthorizationPolicies for why that
    // translation moved into the database.
    options.AddPermissionPolicies();

    // The routes that are about the caller's own account carry a token and nothing more: signing yourself out,
    // reading your own profile, changing your own password. Keeping this as a named policy means every other
    // endpoint in the codebase names a permission, so "which capability does this route need" is always answered.
    options.AddPolicy(AuthorizationPolicies.Authenticated, policy => policy.RequireAuthenticatedUser());
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

// The species-profile catalogue (FR-10 — roadmap 3.9). No settings of its own: a profile's defaults are the
// domain defaults, so there is nothing here for a deployment to move.
builder.Services.AddScoped<SpeciesProfileService>();

// The silence watchdog's settings (FR-07, roadmap task 3.3). The missed-interval count is the one the read surface
// already derives the online badge from, so a deployment that changes it moves the stored status and the badge
// together; the 30-minute critical threshold is a rule, not a setting. The device-onboarding service takes the same
// value, so the fleet screen's badge and the terrarium header's badge cannot disagree (TASKS 2026-10-14, item 9b).
builder.Services.AddSingleton(sp => new DeviceSilenceSettings(
    sp.GetRequiredService<IOptions<DefaultsOptions>>().Value.SilentAfterIntervals));

// Reference data (TASKS 2026-10-14, item 11). No settings: the catalogue is the table, seeded from the code that
// owns it.
builder.Services.AddScoped<ReferenceService>();

// Account management for the administrator (TASKS 2026-10-14, item 12). No settings of its own: the guards are
// rules rather than deployment choices.
builder.Services.AddScoped<UserAdminService>();

// The role/permission matrix as data (TASKS 2026-10-14, item 12). Editing it is an administrative write, so the
// service is registered like any other account operation.
builder.Services.AddScoped<RoleAdminService>();

// The administrator's get-all dashboard (TASKS 2026-10-14, item 13). Read-only and cross-account: every route that
// reaches it names `data.read.any`.
builder.Services.AddScoped<AdminService>();

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

    // The session's own routes — `/auth/me`, `/auth/refresh`, `/auth/logout` — are not credential-guessing
    // surfaces: each needs a token the caller already holds, and the client calls them as a matter of course
    // (read the profile on load, rotate a 15-minute token, end a session). Sharing the credential budget made
    // ordinary housekeeping spend it, and a `429` on a rotation is the one refusal that can end a signed-in
    // session — so these three get a budget sized for the client that uses them rather than for an attacker who
    // is guessing passwords. The credential routes keep the tight budget above.
    options.AddPolicy("authSession", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    // The ingest fallback is limited per device (6/min, `07-appendices/03` §3.6) rather than per address, because
    // two boards behind one home router are two budgets - which is the point of the fallback: a device that has
    // been offline is the one that needs to back-fill, and it must not be starved by the other one.
    //
    // The key comes from a header the caller supplies, so this is a *fairness* limit and not a security control:
    // a hostile caller can mint a fresh partition per request by varying the id. What bounds that caller is the
    // authentication that follows - every forged id costs a parse and a failed device lookup - plus the
    // address-keyed policy above on the auth group. Stated here because a limit that reads like a defence and is
    // not one is worse than a documented fairness rule.
    options.AddPolicy("ingest", context =>
    {
        // The same parse the endpoint uses, deliberately: a limit keyed on a different reading of the header than
        // the one that authorises the request is a limit that can be pointed at someone else's budget.
        var key = DeviceCredentialHeader.TryParse(
                context.Request.Headers.Authorization.ToString(),
                out var devicePublicId,
                out _)
            ? devicePublicId
            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: key,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 6,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    });
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
        Description = "The accessToken field of POST /api/v1/auth/login's response — not its refreshToken, which no "
                    + "bearer route accepts. Paste the value alone, without a 'Bearer ' prefix, which Swagger UI adds "
                    + "itself. It is valid for 15 minutes and nothing here rotates it for you, so a call that answers "
                    + "401 unauthenticated after a while means the token aged out: run POST /api/v1/auth/refresh with "
                    + "the refreshToken and paste the accessToken it returns.",
    });

    // The document has to *require* the scheme on the operations that use it, or Swagger UI accepts a token in its
    // dialog and never sends it — every authenticated call then answers `401 unauthenticated`, which reads as a bad
    // token. That is what this filter is for; a definition alone is only what makes the dialog appear.
    options.OperationFilter<BearerSecurityOperationFilter>();

    // The other half of the same problem, for the conditional header an update must carry: the parameter is bound
    // as nullable so the endpoint can answer `428` itself, which would otherwise leave the document describing it
    // as optional. See the filter's remarks.
    options.OperationFilter<RequiredIfMatchOperationFilter>();
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
    //
    // A body the framework could not bind is the caller's mistake, not the server's. Answering 500 would send
    // someone hunting a server fault for a malformed payload, and a client that *can* fix its request cannot tell
    // that apart from one that should stop retrying. BadHttpRequestException carries the status the framework
    // chose: 400 for a body that will not parse, 413 for one over the endpoint's limit.
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var status = StatusCodes.Status500InternalServerError;
    ProblemDetails problem;

    if (error is BadHttpRequestException badRequest && badRequest.StatusCode is >= 400 and < 500)
    {
        status = badRequest.StatusCode;

        // 413 is deliberately *not* called `payload_too_large`: that code is documented as a 400 raised by the
        // ingest endpoint for a batch over its own 32 KB limit (`07-appendices/03` §5), and giving the same name
        // two different statuses would make the code useless to a client. This is the transport refusing to read
        // the body at all. Kestrel answers its own request-size limit before the application sees the request, so
        // in practice this branch only fires for an in-app limit such as [RequestSizeLimit].
        var tooLarge = status == StatusCodes.Status413PayloadTooLarge;
        var code = tooLarge ? "request_too_large" : "malformed_request";

        problem = new ProblemDetails
        {
            Title = tooLarge ? "The request body is too large" : "The request body could not be read",
            Status = status,
            Type = $"https://smartreptile.example/problems/{code}",
            Detail = tooLarge
                ? "The request body is larger than this endpoint accepts. Nothing was processed."
                : "The request body was not valid JSON for this endpoint. Nothing was processed.",
        };

        problem.Extensions["code"] = code;
    }
    else
    {
        problem = new ProblemDetails
        {
            Title = "An unexpected error occurred",
            Status = status,
            Type = "https://smartreptile.example/problems/internal_error",
            Detail = "The request could not be completed. Quote the traceId when reporting this.",
        };

        problem.Extensions["code"] = "internal_error";
    }

    problem.Extensions["traceId"] = context.TraceIdentifier;

    context.Response.StatusCode = status;

    // `application/problem+json` is what RFC 7807 specifies and what `07-appendices/03` §5 documents. Passing it
    // to the writer rather than assigning `ContentType` first is deliberate: WriteAsJsonAsync sets the header
    // itself when the parameter is null, and assigning beforehand is silently overwritten (verified: the response
    // came back as application/json until this overload was used).
    await context.Response.WriteAsJsonAsync(
        problem,
        options: null,
        contentType: "application/problem+json");
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
app.MapSpeciesProfileEndpoints();
app.MapAlertEndpoints();
app.MapNotificationEndpoints();
app.MapAuditEndpoints();
app.MapReferenceEndpoints();
app.MapUserEndpoints();
app.MapRoleEndpoints();
app.MapAdminEndpoints();
app.MapIngestEndpoints();
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
        "/api/v1/species-profiles", "/api/v1/species-profiles/{profileId}",
        "/api/v1/species-profiles/{profileId}/duplicate",
        "/api/v1/terrariums/{terrariumId}/readings/latest", "/api/v1/terrariums/{terrariumId}/readings",
        "/api/v1/terrariums/{terrariumId}/coverage",
        "/api/v1/terrariums/{terrariumId}/thresholds", "/api/v1/terrariums/{terrariumId}/thresholds/{metric}",
        "/api/v1/terrariums/{terrariumId}/silences", "/api/v1/terrariums/{terrariumId}/silences/{silenceId}",
        "/api/v1/alerts", "/api/v1/alerts/{alertId}", "/api/v1/alerts/{alertId}/ack",
        "/api/v1/alerts/{alertId}/resolve", "/api/v1/alerts/{alertId}/timeline",
        "/api/v1/notifications", "/api/v1/notifications/{notificationId}/read", "/api/v1/notifications/read-all",
        "/api/v1/audit",
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
            "Start-up complete: migrationsApplied={MigrationsApplied}, profilesSeeded={ProfilesSeeded}, "
            + "timeZonesSeeded={TimeZonesSeeded}, adminSeeded={AdminSeeded}, demoSeeded={DemoSeeded}, version={Version}",
            outcome.MigrationsApplied,
            outcome.ProfilesSeeded,
            outcome.TimeZonesSeeded,
            outcome.AdminSeeded,
            outcome.Demo.Seeded,
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
