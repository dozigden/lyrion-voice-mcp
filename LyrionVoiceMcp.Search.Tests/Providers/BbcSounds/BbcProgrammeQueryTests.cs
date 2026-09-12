using LyrionVoiceMcp.Search.Providers.BbcSounds;

namespace LyrionVoiceMcp.Search.Tests.Providers.BbcSounds;

public sealed class BbcProgrammeQueryTests
{
    [Theory]
    [InlineData("The Hour Show")]
    [InlineData("Mira's BBC Sounds Adventure")]
    [InlineData("Mira BBC Soundscape")]
    public void NameInterpretationShouldOnlyRemoveCompleteBoundaryPhrases(string text)
    {
        var query = BbcProgrammeQuery.Create(text);

        Assert.Equal(PhuzzyTextForms.Create(text).Normalised, query.Forms.Normalised);
        Assert.Null(query.NameInterpretation);
    }

    [Theory]
    [InlineData("BBC Sounds: -VI")]
    [InlineData("BBC Sounds: .VI")]
    public void ContextRemovalMustNotMakeIneligibleRomanSyntaxEligible(string text)
    {
        var query = BbcProgrammeQuery.Create(text).NameInterpretation!;

        Assert.DoesNotContain(query.Spans.SelectMany(span => span.Forms.QueryEquivalenceForms),
            form => form.Signal == "roman_cardinal_equivalent");
    }
}
