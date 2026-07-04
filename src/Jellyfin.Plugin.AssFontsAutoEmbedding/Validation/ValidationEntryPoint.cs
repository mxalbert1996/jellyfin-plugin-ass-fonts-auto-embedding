namespace Jellyfin.Plugin.AssFontsAutoEmbedding.Validation;

public static class ValidationEntryPoint
{
    public static int Run(string[] args)
    {
        if (System.Environment.GetEnvironmentVariable("ASSFONTS_MANAGED_VALIDATION") == "1")
        {
            ManagedCoordinationValidation.Main(args);
            return 0;
        }

        return NativeSmokeValidation.Main(args);
    }
}
