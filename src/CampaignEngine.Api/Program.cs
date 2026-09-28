using CampaignEngine.Api.Endpoints;
using CampaignEngine.Api.Http;
using CampaignEngine.Api.Security;
using CampaignEngine.Core.Serialization;
using CampaignEngine.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o => CampaignJson.Configure(o.SerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ErrorHandler>();
builder.Services.AddApiKeyAuthentication(builder.Configuration);
builder.Services.AddCampaignEngineInfrastructure(builder.Configuration);
builder.Services.AddOpenApi(o => o.AddDocumentTransformer<ApiDocument>());
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference("/docs", o => o
    .WithTitle("Campaign Engine API")
    .AddPreferredSecuritySchemes(ApiKeyHandler.SchemeName));
app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Redirect("/docs")).ExcludeFromDescription();

app.MapCampaignEndpoints();
app.MapProductListEndpoints();
app.MapChannelEndpoints();

await app.Services.InitializeCampaignDatabaseAsync();
await app.RunAsync();

/// <summary>Entry point, visible to integration tests.</summary>
public partial class Program;
