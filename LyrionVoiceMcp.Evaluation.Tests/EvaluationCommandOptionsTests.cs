namespace LyrionVoiceMcp.Evaluation.Tests;

public sealed class EvaluationCommandOptionsTests
{
    [Fact]
    public void Serve_parse_uses_local_defaults()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lyrion-voice-serve-{Guid.NewGuid():N}");
        var cataloguePath = Path.Combine(root, ".data", "evaluation", "catalogue.db");
        Directory.CreateDirectory(Path.GetDirectoryName(cataloguePath)!);
        File.WriteAllBytes(cataloguePath, []);
        try
        {
            var outcome = EvaluationServerCommandOptions.Parse([], root);

            var parsed = Assert.IsType<EvaluationServerArgumentsParsed>(outcome);
            Assert.Equal(cataloguePath, parsed.CataloguePath);
            Assert.Equal(
                Path.Combine(root, ".data", "evaluation", "search-indexes"),
                parsed.IndexDirectoryPath);
            Assert.Equal("http://127.0.0.1:5610", parsed.Url);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("https://127.0.0.1:5610")]
    [InlineData("http://127.0.0.1")]
    [InlineData("not-a-url")]
    public void Serve_parse_rejects_an_invalid_listen_url(string url)
    {
        var root = Path.GetTempPath();
        var cataloguePath = Path.Combine(root, $"catalogue-{Guid.NewGuid():N}.db");
        File.WriteAllBytes(cataloguePath, []);
        try
        {
            var outcome = EvaluationServerCommandOptions.Parse(
                ["--catalogue", cataloguePath, "--url", url],
                root);

            Assert.IsType<EvaluationServerArgumentsRejected>(outcome);
        }
        finally
        {
            File.Delete(cataloguePath);
        }
    }

    [Fact]
    public void Parse_defaults_to_the_private_sibling_corpus_and_evaluation_output()
    {
        var root = Path.Combine(Path.GetTempPath(), "lyrion-voice-mcp");
        var now = new DateTimeOffset(2026, 8, 13, 9, 10, 11, TimeSpan.Zero);

        var outcome = EvaluationCommandOptions.Parse([], root, now);

        var parsed = Assert.IsType<EvaluationArgumentsParsed>(outcome);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(root, "..", "lyrion-voice-evaluation", "corpus.json")),
            parsed.CorpusPath);
        Assert.EndsWith(
            Path.Combine(".data", "evaluation", "lms-pass-through-20260813-091011.json"),
            parsed.OutputPath,
            StringComparison.Ordinal);
        Assert.Equal(EvaluationResolverSelection.LmsPassThrough, parsed.Resolver);
        Assert.Null(parsed.CataloguePath);
        Assert.False(parsed.RefreshCatalogue);
    }

    [Fact]
    public void Parse_rejects_the_removed_settings_option()
    {
        var root = Path.Combine(Path.GetTempPath(), "lyrion-voice-mcp");

        var outcome = EvaluationCommandOptions.Parse(
            ["--settings", "settings.json"],
            root,
            DateTimeOffset.UtcNow);

        var rejected = Assert.IsType<EvaluationArgumentsRejected>(outcome);
        Assert.Contains("Unknown argument", rejected.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_selects_the_catalogue_resolver_and_local_catalogue_default()
    {
        var root = Path.Combine(Path.GetTempPath(), "lyrion-voice-mcp");
        var now = new DateTimeOffset(2026, 8, 15, 9, 10, 11, TimeSpan.Zero);

        var outcome = EvaluationCommandOptions.Parse(
            ["--resolver", "catalogue-lexical"],
            root,
            now);

        var parsed = Assert.IsType<EvaluationArgumentsParsed>(outcome);
        Assert.Equal(EvaluationResolverSelection.CatalogueLexical, parsed.Resolver);
        Assert.Equal(
            Path.Combine(root, ".data", "evaluation", "catalogue.db"),
            parsed.CataloguePath);
        Assert.EndsWith(
            Path.Combine(".data", "evaluation", "catalogue-lexical-20260815-091011.json"),
            parsed.OutputPath,
            StringComparison.Ordinal);
        Assert.False(parsed.RefreshCatalogue);
    }

    [Fact]
    public void Parse_selects_an_explicit_catalogue_refresh()
    {
        var root = Path.Combine(Path.GetTempPath(), "lyrion-voice-mcp");

        var outcome = EvaluationCommandOptions.Parse(
            ["--resolver", "catalogue-lexical", "--refresh-catalogue"],
            root,
            DateTimeOffset.UtcNow);

        var parsed = Assert.IsType<EvaluationArgumentsParsed>(outcome);
        Assert.True(parsed.RefreshCatalogue);
    }

    [Fact]
    public void Parse_selects_the_phuzzy_resolver_and_shared_catalogue()
    {
        var root = Path.Combine(Path.GetTempPath(), "lyrion-voice-mcp");

        var outcome = EvaluationCommandOptions.Parse(
            ["--resolver", "catalogue-phuzzy"],
            root,
            DateTimeOffset.UtcNow);

        var parsed = Assert.IsType<EvaluationArgumentsParsed>(outcome);
        Assert.Equal(EvaluationResolverSelection.CataloguePhuzzy, parsed.Resolver);
        Assert.Equal(
            Path.Combine(root, ".data", "evaluation", "catalogue.db"),
            parsed.CataloguePath);
        Assert.Contains("catalogue-phuzzy", parsed.OutputPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_selects_the_indexed_phuzzy_resolver_and_shared_catalogue()
    {
        var root = Path.Combine(Path.GetTempPath(), "lyrion-voice-mcp");

        var outcome = EvaluationCommandOptions.Parse(
            ["--resolver", "catalogue-phuzzy-indexed"],
            root,
            DateTimeOffset.UtcNow);

        var parsed = Assert.IsType<EvaluationArgumentsParsed>(outcome);
        Assert.Equal(EvaluationResolverSelection.CataloguePhuzzyIndexed, parsed.Resolver);
        Assert.Equal(
            Path.Combine(root, ".data", "evaluation", "catalogue.db"),
            parsed.CataloguePath);
        Assert.Contains("catalogue-phuzzy-indexed", parsed.OutputPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_selects_the_lucene_resolver_and_shared_catalogue()
    {
        var root = Path.Combine(Path.GetTempPath(), "lyrion-voice-mcp");

        var outcome = EvaluationCommandOptions.Parse(
            ["--resolver", "catalogue-lucene"],
            root,
            DateTimeOffset.UtcNow);

        var parsed = Assert.IsType<EvaluationArgumentsParsed>(outcome);
        Assert.Equal(EvaluationResolverSelection.CatalogueLucene, parsed.Resolver);
        Assert.Equal(
            Path.Combine(root, ".data", "evaluation", "catalogue.db"),
            parsed.CataloguePath);
        Assert.Contains("catalogue-lucene", parsed.OutputPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_rejects_catalogue_path_for_the_lms_resolver()
    {
        var root = Path.Combine(Path.GetTempPath(), "lyrion-voice-mcp");

        var outcome = EvaluationCommandOptions.Parse(
            ["--catalogue", "catalogue.db"],
            root,
            DateTimeOffset.UtcNow);

        var rejected = Assert.IsType<EvaluationArgumentsRejected>(outcome);
        Assert.Contains("catalogue resolver", rejected.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_rejects_catalogue_refresh_for_the_lms_resolver()
    {
        var root = Path.Combine(Path.GetTempPath(), "lyrion-voice-mcp");

        var outcome = EvaluationCommandOptions.Parse(
            ["--refresh-catalogue"],
            root,
            DateTimeOffset.UtcNow);

        var rejected = Assert.IsType<EvaluationArgumentsRejected>(outcome);
        Assert.Contains("catalogue resolver", rejected.Error, StringComparison.Ordinal);
    }
}
