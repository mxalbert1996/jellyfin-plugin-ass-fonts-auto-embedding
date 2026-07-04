using System;
using System.Collections.Generic;
using Jellyfin.Plugin.AssFontsAutoEmbedding.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.AssFontsAutoEmbedding;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin? Instance { get; private set; }

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public override string Name => "ASS Fonts Auto Embedding";

    public override string Description => "Rewrites eligible ASS/SSA subtitles so Jellyfin can deliver font-embedded subtitle output using assfonts.";

    public override Guid Id => Guid.Parse("2d9e9cb9-c43e-4f6f-8133-4fcf2d178001");

    public string PluginDataPath => DataFolderPath;

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "assfontsautoembedding",
            DisplayName = "ASS Fonts Auto Embedding",
            EmbeddedResourcePath = $"{GetType().Namespace}.Web.configPage.html"
        };
    }

}
