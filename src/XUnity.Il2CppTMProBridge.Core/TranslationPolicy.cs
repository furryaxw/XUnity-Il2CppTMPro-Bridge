namespace XUnity.Il2CppTMProBridge.Core;

public static class TranslationPolicy
{
    private const string NumericPunctuation = "+-.,:/%()[]";

    public static bool ShouldSkip(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var hasDigit = false;
        foreach (var character in text)
        {
            if (char.IsDigit(character))
            {
                hasDigit = true;
                continue;
            }

            if (!char.IsWhiteSpace(character) && NumericPunctuation.IndexOf(character) < 0)
            {
                return false;
            }
        }

        return hasDigit;
    }
}

public enum TranslationAction
{
    Skip,
    RestoreCachedTranslation,
    Lookup
}

public readonly record struct TranslationSnapshot(string Original, string Translation);

public static class TranslationStateMachine
{
    public static TranslationAction Decide(string? current, TranslationSnapshot? cached)
    {
        if (TranslationPolicy.ShouldSkip(current))
        {
            return TranslationAction.Skip;
        }

        if (cached is { } value)
        {
            if (string.Equals(current, value.Translation, StringComparison.Ordinal))
            {
                return TranslationAction.Skip;
            }

            if (string.Equals(current, value.Original, StringComparison.Ordinal))
            {
                return TranslationAction.RestoreCachedTranslation;
            }
        }

        return TranslationAction.Lookup;
    }
}
