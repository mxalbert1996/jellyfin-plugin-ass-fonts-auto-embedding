namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Services;

public sealed record RewriteEligibilityResult(bool IsEligible, string Reason)
{
    public static RewriteEligibilityResult Eligible() => new(true, string.Empty);

    public static RewriteEligibilityResult Ineligible(string reason) => new(false, reason);
}
