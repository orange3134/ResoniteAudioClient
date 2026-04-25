# Third-Party Notices

This repository is licensed under the MIT License for the original AudioClient
source code only.

AudioClient depends on Resonite/FrooxEngine at build time and runtime, but
those assemblies, assets, names, and other game-provided materials are not
owned by this repository and are not relicensed under MIT.

## Resonite / FrooxEngine

- Used via local installation references and/or the `Resonite.GameLibs` NuGet
  package.
- Keep Resonite-provided DLLs, assets, and decompiled sources out of this
  repository and out of release archives unless their own terms explicitly
  allow redistribution.
- End users should supply their own valid Resonite installation when required.

## Direct NuGet dependencies used by this repository

- Avalonia 11.2.7 - MIT
- Avalonia.Desktop 11.2.7 - MIT
- Avalonia.Themes.Fluent 11.2.7 - MIT
- CommunityToolkit.Mvvm 8.4.0 - MIT

## Runtime dependencies transitively included by Avalonia builds

- SkiaSharp 2.88.9 - MIT
- HarfBuzzSharp 7.3.0.3 - MIT
- Tmds.DBus.Protocol 0.20.0 - MIT

## Verification notes

- Avalonia and CommunityToolkit.Mvvm license expressions were verified from the
  local NuGet package metadata in this development environment.
- SkiaSharp, HarfBuzzSharp, and Tmds.DBus.Protocol license expressions were
  also verified from local NuGet package metadata.
- `Resonite.GameLibs` should be re-checked before each public release because
  package metadata can change over time.
