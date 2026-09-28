using CampaignEngine.Api.Security;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CampaignEngine.Api.Http;

/// <summary>Adds title, description and the API-key security scheme to the OpenAPI document.</summary>
internal sealed class ApiDocument : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Campaign Engine API",
            Version = "v1",
            Description = "Channel-agnostic retail campaign engine. Send the API key in the `X-Api-Key` header. " +
                          "Polymorphic rules use a `type` discriminator; see docs/rule-reference.md.",
            License = new OpenApiLicense { Name = "MIT" },
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[ApiKeyHandler.SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = ApiKeyHandler.HeaderName,
            Description = "API key of the calling system.",
        };
        document.Components.SecuritySchemes[SessionHandler.SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            Description = "Session token from POST /api/v1/auth/login (web app users).",
        };
        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(ApiKeyHandler.SchemeName, document)] = [],
        });
        return Task.CompletedTask;
    }
}
