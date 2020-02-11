# altcode.fake
FAKE helper code that I've written as a side-effect of other projects

## What's in the box?

For FAKE >= 5.9.3 or later for .net framework and .net core

* [`AltCode.Fake.DotNet.Gendarme` ![Nuget](https://buildstats.info/nuget/AltCode.Fake.DotNet.Gendarme)](http://nuget.org/packages/altcode.fake.dotnet.gendarme), a gendarme helper along the lines of the FxCop task `Fake.DotNet.FxCop`

Can be used with the most recent [homebrew release from my fork](https://www.nuget.org/packages/altcode.gendarme/) to analyze netcore/netstandard builds, even though this homebrew Gendarme is still a classic Framework/Mono tool built with the pre-Roslyn compiler (VS2013).  

DotNet global tools

*  [`AltCode.VsWhat` ![Nuget](https://buildstats.info/nuget/AltCode.VsWhat)](http://nuget.org/packages/altcode.vswhat), a tool to list Visual Studio instances and their installed packages; a thin wrapper over [BlackFox.VsWhere](https://github.com/vbfox/FoxSharp/blob/master/src/BlackFox.VsWhere/Readme.md) to make this one specific query.


## Continuous Integration


| | | |
| --- | --- | --- | 
| **Build** | <sup>AppVeyor</sup> [![Build status](https://img.shields.io/appveyor/ci/SteveGilham/altcode-fake/master.svg)](https://ci.appveyor.com/project/SteveGilham/altcode-fake) ![Build history](https://buildstats.info/appveyor/chart/SteveGilham/altcode-fake?branch=master) | <sup>Travis</sup> [![Build status](https://travis-ci.com/SteveGilham/altcode.fake.svg?branch=master)](https://travis-ci.com/SteveGilham/altcode.fake#) [![Build history](https://buildstats.info/travisci/chart/SteveGilham/altcode.fake?branch=master)](https://travis-ci.com/SteveGilham/altcode.fake/builds)|
| **Unit Test coverage** | <sup>Coveralls</sup> [![Coverage Status](https://coveralls.io/repos/github/SteveGilham/altcode.fake/badge.svg?branch=master)](https://coveralls.io/github/SteveGilham/altcode.fake?branch=master) |

## Usage

See the [Wiki page](https://github.com/SteveGilham/altcode.fake/wiki) for details


## Building

### Tooling

#### All platforms

It is assumed that the following are available

.net core SDK 2.1.402 or later (`dotnet`) -- try https://www.microsoft.com/net/download  

#### Windows

You will need Visual Studio VS2017 (Community Edition) v15.9.latest with F# language support (or just the associated build tools and your editor of choice).

#### *nix

It is assumed that `mono` (version 5.14.x) and `dotnet` are on the `PATH` already, and everything is built from the command line, with your favourite editor used for coding.

### Bootstrapping

Start by setting up `dotnet fake` with `dotnet restore dotnet-fake.fsproj`
Then `dotnet fake run ./Build/setup.fsx` to do the rest of the set-up.

### Normal builds

Running `dotnet fake run ./Build/build.fsx` performs a full build/test/package process.

Use `dotnet fake run ./Build/build.fsx --target <targetname>` to run to a specific target.


## Thanks to

* [AppVeyor](https://ci.appveyor.com/project/SteveGilham/altcode-fake) for allowing free build CI services for Open Source projects
* [travis-ci](https://travis-ci.com/SteveGilham/altcode.fake) for allowing free build CI services for Open Source projects
* [Coveralls](https://coveralls.io/r/SteveGilham/altcode.fake) for allowing free services for Open Source projects
