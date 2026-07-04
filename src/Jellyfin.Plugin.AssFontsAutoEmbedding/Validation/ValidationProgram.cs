namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Validation;

public sealed class ValidationProgram
{
    public static int Main(string[] args)
        => ValidationEntryPoint.Run(args);
}
