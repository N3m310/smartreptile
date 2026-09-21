namespace SmartReptile.Api.Middleware;

/// <summary>
/// Ensures every request carries a correlation id (FR-18 BR-18.3): it is read from <c>X-Correlation-ID</c> when
/// the client supplies one, generated otherwise, echoed in the response and pushed into the log scope so a
/// single id can be grepped across ingest, evaluation and notification.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    /// <summary>Header used for the correlation id.</summary>
    public const string HeaderName = "X-Correlation-ID";

    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var supplied) &&
                            !string.IsNullOrWhiteSpace(supplied)
            ? supplied.ToString()
            : context.TraceIdentifier;

        context.Response.Headers[HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["RequestPath"] = context.Request.Path.Value ?? string.Empty,
            ["RequestMethod"] = context.Request.Method,
        }))
        {
            await next(context);
        }
    }
}
