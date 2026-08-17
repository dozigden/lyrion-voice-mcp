using System.Text.Json;
using System.Text.Json.Serialization;
using LyrionVoiceMcp.Api.Diagnostics;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Api.Endpoints;

public static class EvaluationEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static IEndpointRouteBuilder MapEvaluationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/evaluation",
            (ProductionSearchDiagnosticService service) =>
                Results.Json(service.Description, JsonOptions));
        endpoints.MapPost("/api/evaluation/search", SearchAsync);
        return endpoints;
    }

    private static async Task<IResult> SearchAsync(
        ProductionSearchDiagnosticRequest request,
        ProductionSearchDiagnosticService service,
        CancellationToken cancellationToken)
    {
        var validationError = ProductionSearchDiagnosticValidation.Validate(request);
        if (validationError is not null)
        {
            return Results.BadRequest(new EvaluationEndpointError(validationError));
        }

        try
        {
            var response = await service.SearchAsync(request, cancellationToken);
            return Results.Json(response, JsonOptions);
        }
        catch (CatalogueSearchUnavailableException exception)
        {
            return Results.Conflict(new EvaluationEndpointError(exception.Message));
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed record EvaluationEndpointError(string Error);
}
