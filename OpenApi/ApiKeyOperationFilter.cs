using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ShortUrl.Api.OpenApi;

public sealed class ApiKeyOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var route = context.ApiDescription.RelativePath ?? string.Empty;
        var isManagementRoute = route.Equals("api/links", StringComparison.OrdinalIgnoreCase) ||
            route.StartsWith("api/links/", StringComparison.OrdinalIgnoreCase);

        if (!isManagementRoute)
        {
            return;
        }

        operation.Security ??= new List<OpenApiSecurityRequirement>();
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            }] = Array.Empty<string>()
        });
    }
}
