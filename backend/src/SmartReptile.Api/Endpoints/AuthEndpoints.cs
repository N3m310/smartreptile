using System.Security.Claims;
using SmartReptile.Api.Security;
using SmartReptile.Application.Identity;

namespace SmartReptile.Api.Endpoints;

/// <summary>
/// The <c>/api/v1/auth</c> group (FR-01). Contract: <c>07-appendices/03</c> §4.1; the group is rate-limited
/// per client address (see the <c>auth</c> policy in <c>Program.cs</c>).
/// </summary>
public static class AuthEndpoints
{
    /// <summary>Maps register, login, refresh, logout, me and change-password.</summary>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth")
            .WithTags("auth")
            .RequireRateLimiting("auth");

        group.MapPost("/register", async (RegisterRequest request, AuthService auth, CancellationToken ct) =>
        {
            var outcome = await auth.RegisterAsync(request, ct);

            // A free identifier and a taken one answer identically, so registration cannot be used to test
            // whether an address has an account (§02-design/06 §2). That includes the recovery code: on both
            // paths one is returned, and only the one issued for an account that was actually created can ever
            // verify. The client proceeds to log in.
            return outcome.Succeeded
                ? Results.Json(
                    new { status = "accepted", recoveryCode = outcome.RecoveryCode },
                    statusCode: StatusCodes.Status202Accepted)
                : Problem(outcome.Problem!);
        })
        .WithSummary("Create an account")
        .WithDescription("Anonymous. Answers identically whether or not the identifiers were free, and returns a "
                       + "backup recovery code to store.");

        group.MapPost("/recover", async (
            RecoverRequest request,
            HttpContext context,
            AuthService auth,
            CancellationToken ct) =>
        {
            var outcome = await auth.RecoverAsync(request, ClientAddress(context), ct);

            // 200 with the replacement code rather than 204: the consumed code is single-use, so the caller has to
            // leave with a new one or it has spent its only means of recovery.
            return outcome.Succeeded
                ? Results.Ok(new { recoveryCode = outcome.RecoveryCode })
                : Problem(outcome.Problem!);
        })
        .WithSummary("Replace a forgotten password with the backup recovery code")
        .WithDescription("Anonymous and rate-limited. Consumes the stored code, issues a replacement and ends every "
                       + "existing session. A wrong code and an unknown identifier answer identically.");

        group.MapPost("/forgot-password", async (
            ForgotPasswordRequest request,
            HttpContext context,
            AuthService auth,
            CancellationToken ct) =>
        {
            await auth.ForgotPasswordAsync(request, ClientAddress(context), ct);

            // Always 202 with no body. The use case cannot fail in a reportable way, and that is the point: any
            // difference between a known and an unknown identifier would make this an account-existence oracle.
            return Results.Accepted();
        })
        .WithSummary("Request a password-reset code")
        .WithDescription("Anonymous and rate-limited. Always answers 202; whether the identifier has an account is "
                       + "told only to the account's owner, through the delivery channel.");

        group.MapPost("/reset-password", async (
            ResetPasswordRequest request,
            HttpContext context,
            AuthService auth,
            CancellationToken ct) =>
        {
            var outcome = await auth.ResetPasswordAsync(request, ClientAddress(context), ct);

            // 200 with a replacement backup code, like /recover: the reset rotates the stored backup code, and a
            // silent rotation would leave the keeper holding one that no longer works.
            return outcome.Succeeded
                ? Results.Ok(new { recoveryCode = outcome.RecoveryCode })
                : Problem(outcome.Problem!);
        })
        .WithSummary("Set a new password with a server-issued reset code")
        .WithDescription("Anonymous and rate-limited. Consumes the code, issues a new backup recovery code and ends "
                       + "every existing session.");

        group.MapPost("/login", async (LoginRequest request, HttpContext context, AuthService auth, CancellationToken ct) =>
        {
            var outcome = await auth.LoginAsync(request, ClientAddress(context), ct);
            return outcome.Succeeded
                ? Results.Ok(SessionBody(outcome.Session!))
                : Problem(outcome.Problem!);
        })
        .WithSummary("Start a session")
        .WithDescription("Returns a 15-minute access token and a single-use refresh token.");

        group.MapPost("/refresh", async (RefreshRequest request, AuthService auth, CancellationToken ct) =>
        {
            var outcome = await auth.RefreshAsync(request.RefreshToken, ct);
            return outcome.Succeeded
                ? Results.Ok(SessionBody(outcome.Session!))
                : Problem(outcome.Problem!);
        })
        .WithSummary("Rotate the session")
        .WithDescription("Consumes the refresh token. Reuse of a consumed token revokes the whole family.");

        group.MapPost("/logout", async (RefreshRequest request, AuthService auth, CancellationToken ct) =>
        {
            await auth.LogoutAsync(request.RefreshToken, ct);
            return Results.NoContent();
        })
        .WithSummary("End a session")
        .WithDescription("Idempotent: an unknown or already-revoked token is still a success.");

        group.MapGet("/me", async (ClaimsPrincipal principal, AuthService auth, CancellationToken ct) =>
        {
            var profile = principal.GetUserId() is { } userId
                ? await auth.GetProfileAsync(userId, ct)
                : null;

            return profile is null ? Results.Unauthorized() : Results.Ok(profile);
        })
        .RequireAuthorization()
        .WithSummary("The signed-in account");

        group.MapPost("/change-password", async (
            ChangePasswordRequest request,
            ClaimsPrincipal principal,
            AuthService auth,
            CancellationToken ct) =>
        {
            var outcome = await auth.ChangePasswordAsync(principal.GetUserId() ?? Guid.Empty, request, ct);
            return outcome.Succeeded ? Results.NoContent() : Problem(outcome.Problem!);
        })
        .RequireAuthorization()
        .WithSummary("Change the password")
        .WithDescription("Re-proves the current password and ends every existing session.");

        return app;
    }

    /// <summary>
    /// Maps an authentication failure to RFC 7807. The status class follows the code, not the message, so a
    /// lockout is a 429 and a policy rejection is a 400 while every credential failure stays an opaque 401.
    /// </summary>
    private static IResult Problem(AuthProblem problem)
    {
        var status = problem.Code switch
        {
            "account_locked" or "ip_blocked" => StatusCodes.Status429TooManyRequests,
            "registration_invalid" or "password_policy_violation" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status401Unauthorized,
        };

        var extensions = new Dictionary<string, object?>
        {
            ["code"] = problem.Code,
        };

        if (problem.Errors.Count > 0)
        {
            extensions["errors"] = problem.Errors
                .Select(violation => new { violation.Field, violation.Code, violation.Message })
                .ToArray();
        }

        return Results.Problem(
            title: problem.Message,
            statusCode: status,
            type: $"https://smartreptile.example/problems/{problem.Code}",
            extensions: extensions);
    }

    private static object SessionBody(AuthSession session) => new
    {
        user = session.User,
        accessToken = session.AccessToken,
        accessTokenExpiresAtUtc = session.AccessTokenExpiresAtUtc,
        refreshToken = session.RefreshToken,
        refreshTokenExpiresAtUtc = session.RefreshTokenExpiresAtUtc,
    };

    private static string ClientAddress(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
