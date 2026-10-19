using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SmartReptile.Api.OpenApi;

/// <summary>
/// Marks the operations that actually take a bearer token, so Swagger UI sends one (§4.1 of
/// <c>07-appendices/03</c> is the contract this document is read against).
/// </summary>
/// <remarks>
/// A security <i>definition</i> is not a security <i>requirement</i>, and Swagger UI needs the second one: the
/// definition is what makes the <i>Authorize</i> dialog appear, but the value typed there is only attached to
/// operations whose document declares the scheme as a requirement — and none of them do on their own. Without this
/// filter the dialog happily accepts a token, no request carries it, and every authenticated call answers
/// <c>401 unauthenticated</c>, which reads as "the token is wrong" rather than "the token was never sent". The
/// generator neither says so nor warns: the document is valid, it is just silent about who needs a token.
/// <para>
/// The requirement is applied per operation, from the endpoint's own <c>RequireAuthorization()</c> metadata, so the
/// anonymous routes stay padlock-free and never advertise a credential they do not accept: <c>login</c>,
/// <c>register</c>, the three recovery routes, the ingest fallback (a device header, not a bearer token) and the
/// ops endpoints.
/// </para>
/// </remarks>
public sealed class BearerSecurityOperationFilter : IOperationFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var requiresAuthentication = context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<IAuthorizeData>()
            .Any();

        if (!requiresAuthentication)
        {
            return;
        }

        operation.Security ??= [];

        operation.Security.Add(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
                },
                Array.Empty<string>()
            },
        });
    }
}
