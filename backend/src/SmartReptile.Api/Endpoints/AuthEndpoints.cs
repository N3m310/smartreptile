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
        // Two budgets, attached per route rather than to the group: guessing a password or a code and holding a
        // token already are not the same kind of caller (`auth` and `authSession` in `Program.cs`).
        var group = app.MapGroup("/api/v1/auth")
            .WithTags("auth");

        group.MapPost("/register", async (RegisterRequest request, AuthService auth, CancellationToken ct) =>
        {
            var outcome = await auth.RegisterAsync(request, ct);

            // 202 carries the recovery code of the account that was created. A taken identifier is a 409 naming the
            // field to fix, rather than a success that hides it (ADR-020).
            return outcome.Succeeded
                ? Results.Json(
                    new { status = "accepted", recoveryCode = outcome.RecoveryCode },
                    statusCode: StatusCodes.Status202Accepted)
                : Problem(outcome.Problem!);
        })
        .RequireRateLimiting("auth")
        .WithSummary("Create an account")
        .WithDescription("Anonymous. Answers 202 with a backup recovery code to store, or 409 registration_conflict "
                       + "naming the identifier that is already in use (ADR-020).");

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
        .RequireRateLimiting("auth")
        .WithSummary("Replace a forgotten password with the backup recovery code")
        .WithDescription("Anonymous and rate-limited. Consumes the stored code, issues a replacement and ends every "
                       + "existing session. A wrong code and an unknown identifier answer identically.");

        group.MapPost("/forgot-password", async (
            ForgotPasswordRequest request,
            HttpContext context,
            AuthService auth,
            CancellationToken ct) =>
        {
            var outcome = await auth.ForgotPasswordAsync(request, ClientAddress(context), ct);

            // 202 with no body when a code was issued; 404 identifier_unknown when nobody holds the identifier.
            // That difference is deliberate (ADR-020): a keeper who mistyped their address is told so instead of
            // waiting for a code that was never generated. The cost — this endpoint can now be used to ask whether
            // an address has an account — is recorded in the ADR rather than left implicit.
            return outcome.Succeeded ? Results.Accepted() : Problem(outcome.Problem!);
        })
        .RequireRateLimiting("auth")
        .WithSummary("Request a password-reset code")
        .WithDescription("Anonymous and rate-limited. Answers 202 when a code was issued, and 404 identifier_unknown "
                       + "when no account uses the identifier (ADR-020).");

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
        .RequireRateLimiting("auth")
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
        .RequireRateLimiting("auth")
        .WithSummary("Start a session")
        .WithDescription("Returns a 15-minute access token and a single-use refresh token.");

        group.MapPost("/refresh", async (RefreshRequest request, AuthService auth, CancellationToken ct) =>
        {
            var outcome = await auth.RefreshAsync(request.RefreshToken, ct);
            return outcome.Succeeded
                ? Results.Ok(SessionBody(outcome.Session!))
                : Problem(outcome.Problem!);
        })
        .RequireRateLimiting("authSession")
        .WithSummary("Rotate the session")
        .WithDescription("Consumes the refresh token. Reuse of a consumed token revokes the whole family.");

        group.MapPost("/logout", async (RefreshRequest request, AuthService auth, CancellationToken ct) =>
        {
            await auth.LogoutAsync(request.RefreshToken, ct);
            return Results.NoContent();
        })
        .RequireRateLimiting("authSession")
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
        .RequireRateLimiting("authSession")
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
        .RequireRateLimiting("auth")
        .WithSummary("Change the password")
        .WithDescription("Re-proves the current password and ends every other session of the account, keeping the one "
                       + "whose refresh token the request names.");

        return app;
    }

    /// <summary>
    /// Maps an authentication failure to RFC 7807. The status class follows the code, not the message, so a lockout
    /// is a 429, a policy rejection is a 400, a taken registration identifier is a 409, an identifier nobody holds is
    /// a 404, and every credential failure stays an opaque 401.
    /// </summary>
    private static IResult Problem(AuthProblem problem)
    {
        var status = problem.Code switch
        {
            "account_locked" or "ip_blocked" => StatusCodes.Status429TooManyRequests,
            "registration_invalid" or "password_policy_violation" => StatusCodes.Status400BadRequest,
            "registration_conflict" => StatusCodes.Status409Conflict,
            "identifier_unknown" => StatusCodes.Status404NotFound,
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
