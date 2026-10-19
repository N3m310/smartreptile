using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SmartReptile.Api.OpenApi;

/// <summary>
/// Marks <c>If-Match</c> as required in the generated document, because it is required by the routes that declare
/// it: an update without the header answers <c>428 precondition_required</c> (FR-03, §4.2 of <c>07-appendices/03</c>).
/// </summary>
/// <remarks>
/// The parameter cannot be bound as a non-nullable string to say so: the framework then refuses a missing header
/// with its own <c>400</c> before the handler runs, and the refusal is the endpoint's to make — it names the route
/// that hands the value out. So the two are kept apart: the binding stays nullable and the document is corrected
/// here. Without it Swagger UI renders the box without complaint, and a caller who fills the rest of the form in
/// learns the rule from a <c>428</c> instead of from the page (TASKS 2026-10-14, item 4).
/// </remarks>
public sealed class RequiredIfMatchOperationFilter : IOperationFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.Parameters is null)
        {
            return;
        }

        foreach (var parameter in operation.Parameters)
        {
            if (parameter.In == ParameterLocation.Header
                && string.Equals(parameter.Name, "If-Match", StringComparison.OrdinalIgnoreCase))
            {
                parameter.Required = true;
            }
        }
    }
}
