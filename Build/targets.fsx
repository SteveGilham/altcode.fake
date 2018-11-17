open System
open System.Diagnostics.Tracing
open System.IO
open System.Reflection
open System.Xml
open System.Xml.Linq

open Actions
open AltCode.Fake.DotNet
open AltCover

open Fake.Core
open Fake.Core.TargetOperators
open Fake.DotNet
open Fake.DotNet.NuGet.NuGet
open Fake.DotNet.Testing.NUnit3
open Fake.Testing
open Fake.DotNet.Testing
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing
open Fake.IO.Globbing.Operators
open Fake.Tools.Git

open FSharpLint.Application
open FSharpLint.Framework

open NUnit.Framework

let Copyright = ref String.Empty
let Version = ref String.Empty
let consoleBefore = (Console.ForegroundColor, Console.BackgroundColor)
let programFiles = Environment.environVar "ProgramFiles"
let programFiles86 = Environment.environVar "ProgramFiles(x86)"
let dotnetPath = "dotnet" |> Fake.Core.ProcessUtils.tryFindFileOnPath

let dotnetOptions (o : DotNet.Options) =
  match dotnetPath with
  | Some f -> { o with DotNetCliPath = f }
  | None -> o

let nugetCache =
  Path.Combine
    (Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".nuget/packages")

let pwsh =
  if Environment.isWindows then
    Tools.findToolInSubPath "pwsh.exe" (programFiles @@ "PowerShell")
  else "pwsh"

let cliArguments =
  { MSBuild.CliArguments.Create() with ConsoleLogParameters = []
                                       DistributedLoggers = None
                                       DisableInternalBinLog = true }

let withWorkingDirectoryVM dir o =
  { dotnetOptions o with WorkingDirectory = Path.getFullName dir
                         Verbosity = Some DotNet.Verbosity.Minimal }

let withWorkingDirectoryOnly dir o =
  { dotnetOptions o with WorkingDirectory = Path.getFullName dir }
let withCLIArgs (o : Fake.DotNet.DotNet.TestOptions) =
  { o with MSBuildParams = cliArguments }
let withMSBuildParams (o : Fake.DotNet.DotNet.BuildOptions) =
  { o with MSBuildParams = cliArguments }

let currentBranch = 
  "."
  |> Path.getFullName
  |> Information.getBranchName

let package project =
  if currentBranch.StartsWith("release/", StringComparison.Ordinal) then
     currentBranch = "release/" + project
  else true

let _Target s f =
  Target.description s
  Target.create s f

// Preparation
_Target "Preparation" ignore

_Target "Clean" (fun _ ->
  printfn "Cleaning the build and deploy folders for %A" currentBranch
  Actions.Clean())

_Target "SetVersion" (fun _ ->
  let appveyor = Environment.environVar "APPVEYOR_BUILD_VERSION"
  let travis = Environment.environVar "TRAVIS_JOB_NUMBER"
  let version = Actions.GetVersionFromYaml()

  let ci =
    if String.IsNullOrWhiteSpace appveyor then
      if String.IsNullOrWhiteSpace travis then String.Empty
      else
        (String.Join(".", version.Replace("{build}", travis).Split('.') |> Seq.take 4)
         + "-travis")
    else appveyor

  let (v, majmin, y) = Actions.LocalVersion ci version
  Version := v
  let copy = sprintf "© 2010-%d by Steve Gilham <SteveGilham@users.noreply.github.com>" y
  Copyright := "Copyright " + copy
  Directory.ensure "./_Generated"
  Actions.InternalsVisibleTo(!Version)
  let v' = String.Join(".", (!Version).Split('.') |> Seq.take 4)
  [ "./_Generated/AssemblyVersion.fs"; "./_Generated/AssemblyVersion.cs" ]
  |> List.iter
       (fun file ->
       AssemblyInfoFile.create file [ AssemblyInfo.Product "AltCode.Fake"
                                      AssemblyInfo.Version(majmin + ".0.0")
                                      AssemblyInfo.FileVersion v'
                                      AssemblyInfo.Company "Steve Gilham"
                                      AssemblyInfo.Trademark ""
                                      AssemblyInfo.Copyright copy ]
         (Some AssemblyInfoFileConfig.Default))
  let hack = """namespace AltCover
module SolutionRoot =
  let location = """ + "\"\"\"" + (Path.getFullName ".") + "\"\"\""
  let path = "_Generated/SolutionRoot.fs"

  // Update the file only if it would change
  let old =
    if File.Exists(path) then File.ReadAllText(path)
    else String.Empty
  if not (old.Equals(hack)) then File.WriteAllText(path, hack))

// Basic compilation

_Target "Compilation" ignore

_Target "BuildRelease" (fun _ ->
  try
    DotNet.restore (fun o -> o.WithCommon(withWorkingDirectoryVM ".")) "AltCode.Fake.sln"
    "AltCode.Fake.sln"
    |> MSBuild.build (fun p ->
         { p with Verbosity = Some MSBuildVerbosity.Normal
                  ConsoleLogParameters = []
                  DistributedLoggers = None
                  DisableInternalBinLog = true
                  Properties =
                    [ "Configuration", "Release"
                      "DebugSymbols", "True" ] })
  with x ->
    printfn "%A" x
    reraise())
_Target "BuildDebug" (fun _ ->
  DotNet.restore (fun o -> o.WithCommon(withWorkingDirectoryVM ".")) "AltCode.Fake.sln"
  "AltCode.Fake.sln"
  |> MSBuild.build (fun p ->
       { p with Verbosity = Some MSBuildVerbosity.Normal
                ConsoleLogParameters = []
                DistributedLoggers = None
                DisableInternalBinLog = true
                Properties =
                  [ "Configuration", "Debug"
                    "DebugSymbols", "True" ] }))

// Code Analysis

_Target "Analysis" ignore

_Target "Lint" (fun _ ->
  let failOnIssuesFound (issuesFound : bool) =
    Assert.That(issuesFound, Is.False, "Lint issues were found")
  try
    let settings =
      Configuration.SettingsFileName
      |> Path.getFullName
      |> File.ReadAllText

    let lintConfig = Configuration.configuration settings
    let options =
      { Lint.OptionalLintParameters.Default with Configuration = Some lintConfig }
    let fsVersion = System.Version("4.0")
    !!"**/*.fsproj"
    |> Seq.collect (fun n -> !!(Path.GetDirectoryName n @@ "*.fs"))
    |> Seq.distinct
    |> Seq.fold (fun _ f ->
         match Lint.lintFile options f fsVersion with
         | Lint.LintResult.Failure x -> failwithf "%A" x
         | Lint.LintResult.Success w ->
           w
           |> Seq.filter (fun x ->
                match x.Fix with
                | None -> false
                | Some fix -> fix.FromText <> "AltCover_Fake") // special case
           |> Seq.fold (fun _ x ->
                printfn "Info: %A\r\n Range: %A\r\n Fix: %A\r\n====" x.Info x.Range x.Fix
                true) false) false
    |> failOnIssuesFound
  with ex ->
    printfn "%A" ex
    reraise())

_Target "Gendarme" (fun _ -> // Needs debug because release is compiled --standalone which contaminates everything
  Directory.ensure "./_Reports"

  let baseRules = Path.getFullName "./Build/rules-fake.xml"
  let rules =
    if Environment.isWindows then baseRules
    else 
      // Gendarme mono doesn't into .pdb files
      let lines = baseRules
                  |> File.ReadAllLines
                  |> Seq.map (fun l -> l.Replace ("AvoidSwitchStatementsRule", "AvoidSwitchStatementsRule | AvoidLongMethodsRule"))
      let fixup = Path.getFullName  "./_Generated/rules-fake.xml"
      File.WriteAllLines(fixup, lines)
      fixup
  
  [ (rules,
     [ "_Binaries/AltCode.Fake.DotNet.Gendarme/Debug+AnyCPU/AltCode.Fake.DotNet.Gendarme.dll" ]) ]
  |> Seq.iter (fun (ruleset, files) ->
       Gendarme.run { Gendarme.Params.Create() with WorkingDirectory = "."
                                                    Severity = Gendarme.Severity.All
                                                    Confidence = Gendarme.Confidence.All
                                                    Configuration = ruleset
                                                    Console = true
                                                    Log = "./_Reports/gendarme.html"
                                                    LogKind = Gendarme.LogKind.Html
                                                    Targets = files }))

_Target "FxCop" (fun _ -> // Needs debug because release is compiled --standalone which contaminates everything
  Directory.ensure "./_Reports"
  [ ([ "_Binaries/AltCode.Fake.DotNet.Gendarme/Debug+AnyCPU/AltCode.Fake.DotNet.Gendarme.dll" ],
     [],
     [ 
       "-Microsoft.Design#CA1006"; "-Microsoft.Design#CA1011"; "-Microsoft.Design#CA1020";
       "-Microsoft.Design#CA1062"; "-Microsoft.Design#CA1034"; "-Microsoft.Naming#CA1704";
       "-Microsoft.Naming#CA1707"; "-Microsoft.Naming#CA1709"; "-Microsoft.Naming#CA1724";
       "-Microsoft.Usage#CA2208" ]) ]
  |> Seq.iter (fun (files, types, ruleset) ->
       files
       |> FxCop.run { FxCop.Params.Create() with WorkingDirectory = "."
                                                 UseGAC = true
                                                 Verbose = false
                                                 ReportFileName =
                                                   "_Reports/FxCopReport.xml"
                                                 Types = types
                                                 Rules = ruleset
                                                 FailOnError = FxCop.ErrorLevel.Warning
                                                 IgnoreGeneratedCode = true }))

// Unit Test

_Target "UnitTest" (fun _ ->
  let numbers =
    !!(@"_Reports/_Unit*/Summary.xml")
    |> Seq.collect (fun f ->
         let xml = XDocument.Load f
         xml.Descendants(XName.Get("Linecoverage"))
         |> Seq.map (fun e ->
              let coverage = e.Value.Replace("%", String.Empty)
              match Double.TryParse coverage with
              | (false, _) ->
                Assert.Fail("Could not parse coverage " + coverage)
                0.0
              | (_, numeric) ->
                printfn "%s : %A" (f
                                   |> Path.GetDirectoryName
                                   |> Path.GetFileName) numeric
                numeric))
    |> Seq.toList
  if numbers
     |> List.tryFind (fun n -> n <= 99.0)
     |> Option.isSome
  then Assert.Fail("Coverage is too low"))
_Target "JustUnitTest" (fun _ -> ())
_Target "BuildForUnitTestDotNet"
  (fun _ ->
  !!(@"./*Tests/*.tests.core.fsproj")
  |> Seq.iter
       (DotNet.build
          (fun p ->
          { p.WithCommon dotnetOptions with Configuration =
                                              DotNet.BuildConfiguration.Debug }
          |> withMSBuildParams)))

_Target "UnitTestDotNet" (fun _ ->
  Directory.ensure "./_Reports"
  try
    !!(@"./*Tests/*.fsproj")
    |> Seq.iter (DotNet.test (fun p ->
                   { p.WithCommon dotnetOptions with Configuration =
                                                       DotNet.BuildConfiguration.Debug
                                                     NoBuild = true }
                   |> withCLIArgs))
  with x ->
    printfn "%A" x
    reraise())

_Target "BuildForAltCoverApi"
  (fun _ ->
  !!(@"./*Tests/*.tests.core.fsproj")
  |> Seq.iter
       (DotNet.build
          (fun p ->
          { p.WithCommon dotnetOptions with Configuration =
                                              DotNet.BuildConfiguration.Debug }
          |> withMSBuildParams)))

_Target "UnitTestDotNetWithAltCoverApi" (fun _ ->
  Directory.ensure "./_Reports"
  let p0 =
    { PrepareParams.Create() with AssemblyExcludeFilter = [ "Tests" ]
                                  TypeFilter = [ """^Microsoft\.""" ] }

  let c0 = CollectParams.Create()

  let setBaseOptions here (o : DotNet.Options) =
    { o with WorkingDirectory = Path.getFullName here
             Verbosity = Some DotNet.Verbosity.Minimal }

  let cliArguments =
    { MSBuild.CliArguments.Create() with ConsoleLogParameters = []
                                         DistributedLoggers = None
                                         DisableInternalBinLog = true }

  try
    let (xml, total) =
      !!(@"./*Tests/*.fsproj")
      |> Seq.zip [ p0 ]
      |> Seq.fold (fun (l, c) (p, f) ->
           let here = Path.GetDirectoryName f
           let p' = { p with XmlReport = here @@ "coverage.xml" }
           try
             f
             |> DotNet.test
                  (fun to' ->
                  { to'.WithCommon(setBaseOptions here).WithParameters p' c0 with MSBuildParams =
                                                                                    cliArguments
                                                                                  Configuration =
                                                                                    DotNet.BuildConfiguration.Debug
                                                                                  Framework =
                                                                                    Some
                                                                                      "netcoreapp2.1"
                                                                                  NoBuild =
                                                                                    true })
           with x -> eprintf "%A" x
           let file = here @@ "coverage.xml"

           let cover =
             file
             |> File.ReadAllLines
             |> Seq.skipWhile (fun l -> l.StartsWith("    <Module") |> not)
             |> Seq.takeWhile (fun l -> l <> "  </Modules>")
             |> Seq.toList
           (file :: l, c @ cover))
           ([],
            [ """<?xml version="1.0" encoding="utf-8" standalone="yes"?>""";
              """<CoverageSession xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">""";
              """  <Summary numSequencePoints="39" visitedSequencePoints="39" numBranchPoints="32" visitedBranchPoints="24" sequenceCoverage="100" branchCoverage="75" maxCyclomaticComplexity="5" minCyclomaticComplexity="1" visitedClasses="13" numClasses="13" visitedMethods="15" numMethods="15" minCrapScore="1" maxCrapScore="5" />""";
              """  <Modules>""" ])
    ReportGenerator.generateReports (fun p ->
      { p with ExePath = Tools.findToolInSubPath "ReportGenerator.exe" "."
               ReportTypes =
                 [ ReportGenerator.ReportType.Html; ReportGenerator.ReportType.XmlSummary ]
               TargetDir = "_Reports/_UnitTestWithAltCoverApi" }) xml
    if not <| String.IsNullOrWhiteSpace(Environment.environVar "APPVEYOR_BUILD_NUMBER") then
      let full = total @ [ "  </Modules>"; "</CoverageSession>" ]
      let coverage = Path.getFullName "./_Reports/coveralls.xml"
      File.WriteAllLines(coverage, full)
      Actions.Run
        (Tools.findToolInSubPath "coveralls.net.exe" nugetCache, "_Reports",
         [ "--opencover"; coverage ]) "Coveralls upload failed"
  with x ->
    printfn "%A" x
    reraise())

_Target "UnitTestWithOpenCover" (fun _ -> ())

// Hybrid (Self) Tests

// Pure OperationalTests

_Target "OperationalTest" ignore

// Packaging

_Target "Packaging" (fun _ ->
  let gendarmeDir =
    Path.getFullName "_Binaries/AltCode.Fake.DotNet.Gendarme/Release+AnyCPU"
  let packable = Path.getFullName "./_Binaries/README.html"

  let gendarmeFiles =
    [ (gendarmeDir @@ "AltCode.Fake.DotNet.Gendarme.dll", Some "lib/net462", None)
      (packable, Some "", None) ]

  let gendarmeNetcoreFiles =
    (!!(gendarmeDir @@ "netstandard2.0/AltCode.Fake.DotNet.Gendarme.*"))
    |> Seq.map (fun x -> (x, Some "lib/netstandard2.0", None))
    |> Seq.toList

  let publishWhat = (Path.getFullName "./_Publish.vsWhat").Length
  let whatFiles where =
    (!!"./_Publish.vsWhat/**/*.*")
    |> Seq.map
         (fun x ->
           (x, Some(where + Path.GetDirectoryName(x).Substring(publishWhat).Replace("\\", "/")), None))
    |> Seq.toList

  [ (List.concat [ gendarmeFiles; gendarmeNetcoreFiles ], "_Packaging.Gendarme",
     "./_Generated/altcode.fake.dotnet.gendarme.nuspec", "AltCode.Fake.DotNet.Gendarme",
     "A helper task for running Mono.Gendarme from FAKE ( >= 5.9.3 )",
     package "Gendarme")
    (whatFiles "tools/netcoreapp2.1/any", "_Packaging.VsWhat",
     "./_Generated/altcode.vswhat.nuspec", "AltCode.VsWhat",
     "A tool to list Visual Studio instances and their installed packages",
     package "VsWhat") ]
  |> List.filter (fun (_,_,_,_,_,ok) -> ok)
  |> List.iter (fun (files, output, nuspec, project, description, _) ->
       let outputPath = "./" + output
       let workingDir = "./_Binaries/" + output
       Directory.ensure workingDir
       Directory.ensure outputPath
       NuGet (fun p ->
         { p with Authors = [ "Steve Gilham" ]
                  Project = project
                  Description = description
                  OutputPath = outputPath
                  WorkingDir = workingDir
                  Files = files
                  Version = !Version
                  Copyright = (!Copyright).Replace("©", "(c)")
                  Publish = false
                  ReleaseNotes = Path.getFullName "ReleaseNotes.md" |> File.ReadAllText
                  ToolPath =
                    if Environment.isWindows then
                      Tools.findToolInSubPath "NuGet.exe" "./packages"
                    else "/usr/bin/nuget" }) nuspec))

_Target "PrepareFrameworkBuild" (fun _ -> ())

_Target "PrepareDotNetBuild" (fun _ ->
  let publish = Path.getFullName "./_Publish"

  DotNet.publish (fun options ->
    { options with OutputPath = Some(publish + ".vswhat")
                   Configuration = DotNet.BuildConfiguration.Release
                   Framework = Some "netcoreapp2.1" })
    (Path.getFullName "./AltCode.VsWhat/AltCode.VsWhat.fsproj")

  [ (String.Empty, "./_Generated/altcode.fake.dotnet.gendarme.nuspec",
     "AltCode.Fake.DotNet.Gendarme (FAKE task helper)", None,
     Some "FAKE build Gendarme") 
    ("DotnetTool", "./_Generated/altcode.vswhat.nuspec",
     "AltCode.VsWhat (Visual Studio package listing tool)", Some "Build/AltCode.VsWhat_128.png",
     Some "Visual Studio") ]
  |> List.iter (fun (ptype, path, caption, icon, tags) ->
       let x s = XName.Get(s, "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd")
       let dotnetNupkg = XDocument.Load "./Build/AltCode.Fake.nuspec"
       let title = dotnetNupkg.Descendants(x "title") |> Seq.head
       title.ReplaceNodes caption
       if ptype
          |> String.IsNullOrWhiteSpace
          |> not
       then
         let tag = dotnetNupkg.Descendants(x "tags") |> Seq.head
         let insert = XElement(x "packageTypes")
         insert.Add(XElement(x "packageType", XAttribute(XName.Get "name", ptype)))
         tag.AddAfterSelf insert
       match icon with
       | None -> ()
       | Some logo ->
         let tag = dotnetNupkg.Descendants(x "iconUrl") |> Seq.head
         let text = String.Concat(tag.Nodes()).Replace("Build/AltCode.Fake_128.png", logo)
         tag.Value <- text
       match tags with
       | None -> ()
       | Some line ->
         let tagnode = dotnetNupkg.Descendants(x "tags") |> Seq.head
         tagnode.Value <- line
       dotnetNupkg.Save path))

_Target "PrepareReadMe"
  (fun _ ->
  Actions.PrepareReadMe
    ((!Copyright).Replace("©", "&#xa9;").Replace("<", "&lt;").Replace(">", "&gt;")))

// Post-packaging deployment touch test

_Target "Deployment" ignore

// AOB

_Target "BulkReport" (fun _ ->
  printfn "Overall coverage reporting"
  Directory.ensure "./_Reports/_BulkReport"
  !!"./_Reports/*.xml"
  |> Seq.filter
       (fun f -> not <| f.EndsWith("Report.xml", StringComparison.OrdinalIgnoreCase))
  |> Seq.toList
  |> ReportGenerator.generateReports (fun p ->
       { p with ExePath = Tools.findToolInSubPath "ReportGenerator.exe" "."
                ReportTypes = [ ReportGenerator.ReportType.Html ]
                TargetDir = "_Reports/_BulkReport" }))
_Target "All" ignore

let resetColours _ =
  Console.ForegroundColor <- consoleBefore |> fst
  Console.BackgroundColor <- consoleBefore |> snd

Target.description "ResetConsoleColours"
Target.createFinal "ResetConsoleColours" resetColours
Target.activateFinal "ResetConsoleColours"

// Dependencies
"Clean"
==> "SetVersion"
==> "Preparation"

"Preparation"
==> "BuildRelease"

"BuildRelease"
==> "BuildDebug"
==> "Compilation"

"BuildRelease"
==> "Lint"
==> "Analysis"

"Compilation"
?=> "Analysis"

"Compilation"
==> "FxCop"
=?> ("Analysis", Environment.isWindows) // not supported

"Compilation"
==> "Gendarme"
==> "Analysis"

"Compilation"
?=> "UnitTest"

"Compilation"
==> "JustUnitTest"
==> "UnitTest"

"Compilation"
==> "BuildForUnitTestDotNet"
==> "UnitTestDotNet"
==> "UnitTest"

"Compilation"
==> "UnitTestWithOpenCover"
=?> ("UnitTest", Environment.isWindows) // OpenCover Mono support

"UnitTestDotNet"
==> "BuildForAltCoverApi"
==> "UnitTestDotNetWithAltCoverApi"
=?> ("UnitTest", Environment.isWindows)

"Compilation"
?=> "OperationalTest"

"Compilation"
?=> "Packaging"

"Compilation"
==> "PrepareFrameworkBuild"
=?> ("Packaging", Environment.isWindows) // can't ILMerge

"Compilation"
==> "PrepareDotNetBuild"
==> "Packaging"

"Compilation"
==> "PrepareReadMe"
==> "Packaging"
==> "Deployment"

"Analysis" ==> "All"

"UnitTest" ==> "All"

"OperationalTest" ==> "All"

"Deployment" ==> "BulkReport" ==> "All"

let defaultTarget() =
  resetColours()
  "All"

Target.runOrDefault <| defaultTarget()