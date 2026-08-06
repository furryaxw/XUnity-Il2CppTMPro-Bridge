using XUnity.Il2CppTMProBridge.Core;

namespace XUnity.Il2CppTMProBridge.Tests;

public sealed class TranslationPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \t")]
    [InlineData("12345")]
    [InlineData("-12.5 %")]
    [InlineData("12:30")]
    public void ShouldSkip_IgnoresBlankAndNumericText(string? text)
    {
        Assert.True(TranslationPolicy.ShouldSkip(text));
    }

    [Theory]
    [InlineData("Armor 120")]
    [InlineData("装甲")]
    [InlineData("...")]
    public void ShouldSkip_KeepsTranslatableText(string text)
    {
        Assert.False(TranslationPolicy.ShouldSkip(text));
    }

    [Fact]
    public void Decide_DoesNotWriteThePreviousTranslationAgain()
    {
        var cached = new TranslationSnapshot("Play", "开始");
        Assert.Equal(TranslationAction.Skip, TranslationStateMachine.Decide("开始", cached));
    }

    [Fact]
    public void Decide_RestoresTranslationWhenGameReappliesOriginal()
    {
        var cached = new TranslationSnapshot("Play", "开始");
        Assert.Equal(TranslationAction.RestoreCachedTranslation, TranslationStateMachine.Decide("Play", cached));
    }

    [Fact]
    public void Decide_LooksUpChangedSourceText()
    {
        var cached = new TranslationSnapshot("Play", "开始");
        Assert.Equal(TranslationAction.Lookup, TranslationStateMachine.Decide("Options", cached));
    }
}
