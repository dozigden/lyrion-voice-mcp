using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Evaluation.Tests;

public sealed class EvaluationCorpusReaderTests
{
    [Fact]
    public void Read_accepts_match_and_no_match_cases()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "cases": [
                {
                  "id": "moonlit-owls",
                  "query": "moon light owls",
                  "expected": [
                    {
                      "kind": "artist",
                      "title": "The Moonlit Owls"
                    }
                  ],
                  "category": "fictional-transcription",
                  "notes": "A fictional test case."
                },
                {
                  "id": "unheard-orchestra",
                  "query": "unheard orchestra",
                  "expected": [],
                  "category": "no-match"
                }
              ]
            }
            """;

        var outcome = new EvaluationCorpusReader().Read(json);

        var loaded = Assert.IsType<CorpusRead>(outcome);
        Assert.Equal(2, loaded.Corpus.Cases.Count);
        Assert.Equal(MediaEntityKind.Artist, loaded.Corpus.Cases[0].Expected[0].Kind);
        Assert.Equal(64, loaded.ContentHash.Length);
    }

    [Fact]
    public void Read_reports_all_structural_validation_errors()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "cases": [
                {
                  "id": "bad id",
                  "query": " ",
                  "expected": [],
                  "category": " "
                },
                {
                  "id": "bad id",
                  "query": "query",
                  "expected": [
                    {
                      "kind": "track",
                      "title": ""
                    }
                  ],
                  "category": "test"
                }
              ]
            }
            """;

        var outcome = new EvaluationCorpusReader().Read(json);

        var rejected = Assert.IsType<CorpusRejected>(outcome);
        Assert.Contains(rejected.Errors, error => error.Contains("kebab-case", StringComparison.Ordinal));
        Assert.Contains(rejected.Errors, error => error.Contains("query", StringComparison.Ordinal));
        Assert.Contains(rejected.Errors, error => error.Contains("category", StringComparison.Ordinal));
        Assert.Contains(rejected.Errors, error => error.Contains("title", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_rejects_unknown_fields()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "cases": [],
              "surprise": true
            }
            """;

        var outcome = new EvaluationCorpusReader().Read(json);

        var rejected = Assert.IsType<CorpusRejected>(outcome);
        Assert.Single(rejected.Errors);
        Assert.Contains("invalid", rejected.Errors[0], StringComparison.OrdinalIgnoreCase);
    }
}
