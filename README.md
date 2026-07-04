# Jellyfin ASS Fonts Auto Embedding

Jellyfin ASS Fonts Auto Embedding is a Jellyfin plugin that rewrites eligible ASS/SSA subtitle output so it can be processed through `libassfonts`.

Today, the native runtime path used by the plugin is aimed at Jellyfin on Linux. The plugin can resolve native libraries from Linux, macOS, and Windows-style plugin-local paths, but this repository currently ships Linux native binaries only.

## Installation

In Jellyfin:

1. Open **Dashboard** → **Plugins** → **Repositories**.
2. Add the manifest URL under plugin repositories.
   ```
   https://raw.githubusercontent.com/mxalbert1996/jellyfin-plugin-ass-fonts-auto-embedding/manifest-release/manifest.json
   ```
3. Save, go back to **Plugins**, and install **ASS Fonts Auto Embedding**.
4. Restart Jellyfin.

## Usage

1. Open the plugin settings.
2. Configure one or more readable font directories that contain the fonts your ASS/SSA subtitles need.
3. Save the settings to persist the configuration and trigger a font DB rebuild.
4. Play media with eligible ASS/SSA subtitles.
5. Jellyfin will return rewritten subtitles with embedded subsetted fonts when rewrite succeeds, and fall back to the original subtitle output if it does not.

If files inside an already-configured font directory change, the plugin does not automatically rebuild the font DB. Run the **Rebuild font DB** scheduled task or resave the plugin config when you need those font changes picked up.

## Development notes

- The plugin project is built with .NET:

  ```bash
  dotnet build Jellyfin.Plugin.AssFontsAutoEmbedding.sln
  ```

- The `assfonts/` directory is a git submodule.
- Native libraries are loaded by explicit full path from the plugin directory.
- The loader looks for plugin-local binaries in these locations:
  - `native/linux-<arch>/libassfonts.so`
  - `native/osx-<arch>/libassfonts.dylib`
  - `native/win-<arch>/assfonts.dll`
- The project currently copies Linux native binaries into plugin output from:
  - `build/assfonts-x86_64/install/lib/libassfonts.so`
  - `build/assfonts-arm64/install/lib/libassfonts.so`
- If those native files are missing, the managed project can still compile, but native DB build and subtitle rewrite features will not work at runtime.

## Build native libraries

Run:

```bash
assfonts-build/build.sh
```

Notes:

- The script uses Docker.
- It builds the Linux `libassfonts` binaries used by this plugin.
- The produced Linux binaries in this repository have been verified to work in official Jellyfin Docker images.
- If the bundled binaries do not work for your environment, build them yourself.
- If you are using Windows or macOS, you should also build your own native binaries for those platforms.

## Validation

Two validation entrypoints are included.

### Native smoke validation

```bash
dotnet run --project src/Jellyfin.Plugin.AssFontsAutoEmbedding/Jellyfin.Plugin.AssFontsAutoEmbedding.csproj -p:NativeSmokeValidation=true
```

This performs a native smoke check of the `libassfonts` integration.

### Managed coordination validation

```bash
ASSFONTS_MANAGED_VALIDATION=1 dotnet run --project src/Jellyfin.Plugin.AssFontsAutoEmbedding/Jellyfin.Plugin.AssFontsAutoEmbedding.csproj -p:ManagedCoordinationValidation=true
```

This exercises managed coordination and caching behavior.
