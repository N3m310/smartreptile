using SmartReptile.Domain.Identity;

namespace SmartReptile.Api.Endpoints;

/// <summary>
/// The one place an RFC 7807 body is shaped, so every route answers the same way (TASKS 2026-10-19, BE-05).
/// </summary>
/// <remarks>
/// Two members are contract rather than implementation, because the web and mobile clients translate from them:
/// <list type="bullet">
/// <item><c>code</c> — a stable, snake_case problem code. It is what a client maps to a localised sentence; the
/// English <c>title</c>/<c>detail</c> are the log-and-fallback text and no screen shows them to a user.</item>
/// <item><c>errors[]</c> — one entry per refused field, each with the same <c>field</c> path, a stable
/// <c>code</c> and the English <c>message</c> as a fallback. Present only when the failure was input validation,
/// and then every violation is present, not just the first.</item>
/// </list>
/// The duplication this replaces was the failure mode worth removing: seven route files each projected the same
/// three members by hand, so one of them could drift and a client would have to handle both shapes.
/// </remarks>
public static class ApiProblem
{
    /// <summary>A problem answer carrying a code and, when the failure was field-level, its violations.</summary>
    /// <param name="code">Stable problem code, e.g. <c>validation_failed</c>.</param>
    /// <param name="message">English message for logs and as a fallback.</param>
    /// <param name="statusCode">HTTP status the code maps to.</param>
    /// <param name="errors">Field-level violations, when the failure was input validation.</param>
    public static IResult Create(
        string code,
        string message,
        int statusCode,
        IReadOnlyList<IdentityViolation>? errors = null) =>
        Results.Problem(
            title: message,
            statusCode: statusCode,
            type: $"https://smartreptile.example/problems/{code}",
            extensions: Extensions(code, errors));

    /// <summary>
    /// The extension members for an answer being built by hand — for the routes that also add members of their own
    /// (a device id, a trace id). The code and the violations always have the same shape as
    /// <see cref="Create"/>'s.
    /// </summary>
    /// <param name="code">Stable problem code.</param>
    /// <param name="errors">Field-level violations, when the failure was input validation.</param>
    public static Dictionary<string, object?> Extensions(
        string code,
        IReadOnlyList<IdentityViolation>? errors = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = code,
        };

        if (errors is { Count: > 0 } violations)
        {
            // Projected rather than handed over as the domain records: the wire shape is `field`/`code`/`message`,
            // and a domain record that grows a member must not silently become part of the API's contract.
            extensions["errors"] = violations
                .Select(violation => new { violation.Field, violation.Code, violation.Message })
                .ToArray();
        }

        return extensions;
    }
}
