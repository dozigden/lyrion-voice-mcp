using System.Text.Json;
using System.Text.Json.Serialization;
using LyrionVoiceMcp.Evaluation;
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
            (EvaluationDiagnosticSearchService service) =>
                Results.Json(service.Description, JsonOptions));
        endpoints.MapPost("/api/evaluation/search", SearchAsync);
        return endpoints;
    }

    private static async Task<IResult> SearchAsync(
        EvaluationDiagnosticSearchRequest request,
        EvaluationDiagnosticSearchService service,
        CancellationToken cancellationToken)
    {
        var validationError = EvaluationDiagnosticSearchValidation.Validate(request);
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
