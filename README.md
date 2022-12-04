# altcode.fake
FAKE helper code that I've written as a side-effect of other projects

## What's in the box?

For FAKE ≥ 5.23.0 or later for .net core

* [`AltCode.Fake.DotNet.Gendarme` ![Nuget](https://buildstats.info/nuget/AltCode.Fake.DotNet.Gendarme)](http://nuget.org/packages/altcode.fake.dotnet.gendarme), a gendarme helper along the lines of the FxCop task `Fake.DotNet.FxCop`

Can be used with the most recent [homebrew release from my fork](https://www.nuget.org/packages/altcode.gendarme/) to analyze netcore/netstandard builds.  

DotNet global tools

*  [`AltCode.VsWhat` ![Nuget](https://buildstats.info/nuget/AltCode.VsWhat)](http://nuget.org/packages/altcode.vswhat), a tool to list Visual Studio instances and their installed packages; a thin wrapper over [BlackFox.VsWhere](https://github.com/vbfox/FoxSharp/blob/master/src/BlackFox.VsWhere/Readme.md) to make this one specific query.


## Continuous Integration


| | | |
| --- | --- | --- |
| **Build**  | <sup>GitHub</sup> [![Build status](https://github.com/SteveGilham/altcode.fake/workflows/CI/badge.svg)](https://github.com/SteveGilham/altcover/altcode.fake?query=workflow%3ACI) | [![Build history](https://buildstats.info/github/chart/SteveGilham/altcode.fake?branch=master)](https://github.com/SteveGilham/altcode.fake/actions?query=workflow%3ACI)|
| **Unit Test coverage** | <sup>Coveralls</sup> | [![Coverage Status](https://coveralls.io/repos/github/SteveGilham/altcode.fake/badge.svg?branch=master)](https://coveralls.io/github/SteveGilham/altcode.fake?branch=master) |

## Usage

See the [Wiki page](https://github.com/SteveGilham/altcode.fake/wiki) for details


## Building

### Tooling

#### All platforms

It is assumed that the following are available

.net core SDK 7.0.100 or later (`dotnet`) -- try https://www.microsoft.com/net/download  

#### Windows

You will need Visual Studio VS2022 (Community Edition) latest with F# language support (or just the associated build tools and your editor of choice).

#### *nix

It is assumed that the latest `mono`, and `dotnet` are on the `PATH` already, and everything is built from the command line, with your favourite editor used for coding.

### Bootstrapping

Start by setting up `required tools with `dotnet tool restore`
Then `dotnet run --project ./Build/Setup.fsproj` to do the rest of the set-up.

### Normal builds

Running `dotnet run --project ./Build/Build.fsproj` performs a full build/test/package process.

Use `dotnet run --project ./Build/Build.fsproj --target <targetname>` to run to a specific target.


## Thanks to

* [Coveralls](https://coveralls.io/r/SteveGilham/altcode.fake) for allowing free services for Open Source projects
