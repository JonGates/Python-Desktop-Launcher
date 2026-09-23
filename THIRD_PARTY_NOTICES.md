# Third-party components

This source delivery does not contain restored NuGet binaries, .NET runtime binaries, an EXE, or font files. Build outputs have their own redistribution requirements; preserve the accompanying license collection before distributing a published executable to others.

## YamlDotNet

Direct package reference: **YamlDotNet 18.1.0**. License: MIT.
Copyright (c) 2008, 2009, 2010, 2011, 2012, 2013, 2014 Antoine Aubry and contributors.

Official project: https://github.com/aaubry/YamlDotNet
Official license: https://github.com/aaubry/YamlDotNet/blob/master/LICENSE.txt

The build helper collects license / notice files available in the restored package cache into each distribution's `licenses` directory. Verify that the YamlDotNet license is actually present; package layouts may change.

## .NET / WPF

A self-contained build includes .NET runtime / Windows desktop framework components and their third-party material. Official sources and notices:

- https://github.com/dotnet/runtime/blob/main/LICENSE.TXT
- https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT
- https://github.com/dotnet/wpf/blob/main/LICENSE.TXT
- https://github.com/dotnet/wpf/blob/main/THIRD-PARTY-NOTICES.TXT

The build helper collects notices found in the restored runtime packages; review them against the exact published runtime version. This file is an index, not a replacement for all component permission notices.

## Fonts and icons

No font files are included. WPF references fonts already installed on Windows. The launcher icon and UI geometry are original simple geometric resources included under the source license. The optional static UI preview uses system fonts; it has no external font download.

## Development workflow references

The GitHub Actions workflow references official checkout, setup-dotnet and upload-artifact actions. They are not bundled into the application. The static checker and Python demo tests use development-environment libraries as documented, not application runtime dependencies.
