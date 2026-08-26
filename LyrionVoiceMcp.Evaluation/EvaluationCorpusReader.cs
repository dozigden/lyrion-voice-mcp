using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LyrionVoiceMcp.Evaluation;

public sealed class EvaluationCorpusReader
{
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<CorpusReadOutcome> ReadFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new CorpusRejected([$"Corpus file was not found: {path}"]);
        }

        try
        {
            var content = await File.ReadAllTextAsync(path, cancellationToken);
            return Read(content);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new CorpusRejected([$"Corpus file could not be read: {exception.Message}"]);
        }
    }

    public CorpusReadOutcome Read(string content)
    {
        EvaluationCorpus? corpus;
        try
        {
            corpus = JsonSerializer.Deserialize<EvaluationCorpus>(content, JsonOptions);
        }
        catch (JsonException exception)
        {
            return new CorpusRejected([$"Corpus JSON is invalid: {exception.Message}"]);
        }

        if (corpus is null)
        {
            return new CorpusRejected(["Corpus JSON did not contain an object."]);
        }

        var errors = Validate(corpus);
        if (errors.Count > 0)
        {
            return new CorpusRejected(errors);
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        return new CorpusRead(corpus, hash);
    }

    private static IReadOnlyList<string> Validate(EvaluationCorpus corpus)
    {
        var errors = new List<string>();
        if (corpus.SchemaVersion != SupportedSchemaVersion)
        {
            errors.Add(
                $"Corpus schemaVersion must be {SupportedSchemaVersion}, but was {corpus.SchemaVersion}.");
        }

        if (corpus.Cases is null || corpus.Cases.Count == 0)
        {
            errors.Add("Corpus must contain at least one case.");
            return errors;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < corpus.Cases.Count; index++)
        {
            var item = corpus.Cases[index];
            var location = $"cases[{index}]";
            if (item is null)
            {
                errors.Add($"{location} must be an object.");
                continue;
            }

            if (!IsStableId(item.Id))
            {
                errors.Add($"{location}.id must be a lowercase kebab-case identifier.");
            }
            else if (!ids.Add(item.Id))
            {
                errors.Add($"{location}.id duplicates '{item.Id}'.");
            }

            if (!string.IsNullOrWhiteSpace(item.Query) && item.Query != item.Query.Trim())
            {
                errors.Add($"{location}.query must have no surrounding whitespace.");
            }

            if (string.IsNullOrWhiteSpace(item.Category)
                || item.Category != item.Category.Trim())
            {
                errors.Add($"{location}.category must be non-empty and have no surrounding whitespace.");
            }

            if (item.Expected is null)
            {
                errors.Add($"{location}.expected must be an array; use an empty array for a no-match case.");
                continue;
            }

            for (var expectedIndex = 0; expectedIndex < item.Expected.Count; expectedIndex++)
            {
                var expected = item.Expected[expectedIndex];
                var expectedLocation = $"{location}.expected[{expectedIndex}]";
                if (expected is null)
                {
                    errors.Add($"{expectedLocation} must be an object.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(expected.Title))
                {
                    errors.Add($"{expectedLocation}.title must be non-empty.");
                }

                if (expected.Artist is not null && string.IsNullOrWhiteSpace(expected.Artist))
                {
                    errors.Add($"{expectedLocation}.artist must be omitted or non-empty.");
                }

                if (expected.Album is not null && string.IsNullOrWhiteSpace(expected.Album))
                {
                    errors.Add($"{expectedLocation}.album must be omitted or non-empty.");
                }
            }
        }

        return errors;
    }

    private static bool IsStableId(string? value)
    {
        if (string.IsNullOrEmpty(value)
            || value[0] == '-'
            || value[^1] == '-')
        {
            return false;
        }

        var previousWasHyphen = false;
        foreach (var character in value)
        {
            var isLetterOrDigit = character is >= 'a' and <= 'z' or >= '0' and <= '9';
            if (!isLetterOrDigit && character != '-')
            {
                return false;
            }

            if (character == '-' && previousWasHyphen)
            {
                return false;
            }

            previousWasHyphen = character == '-';
        }

        return true;
    }
}
