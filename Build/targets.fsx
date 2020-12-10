open System
open System.Diagnostics.Tracing
open System.IO
open System.Reflection
open System.Xml
open System.Xml.Linq

open Actions
open AltCode.Fake.DotNet
open AltCoverFake.DotNet.DotNet
open AltCoverFake.DotNet.Testing

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

let AltCoverFilter(p : Primitive.PrepareOptions) =
  { p with
      //MethodFilter = "WaitForExitCustom" :: (p.MethodFilter |> Seq.toList)
      AssemblyExcludeFilter =
        @"NUnit3\." :: (@"\.Tests" :: (p.AssemblyExcludeFilter |> Seq.toList))
      AssemblyFilter = "FSharp" :: @"\.Placeholder" :: (p.AssemblyFilter |> Seq.toList)
      LocalSource = true
      TypeFilter = [ @"System\."; "Microsoft" ] @ (p.TypeFilter |> Seq.toList) }

let dotnetOptions (o : DotNet.Options) =
  match dotnetPath with
  | Some f -> { o with DotNetCliPath = f }
  | None -> o

let nugetCache =
  Path.Combine
    (Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".nuget/packages")

let fxcop =
  if Environment.isWindows then
    BlackFox.VsWhere.VsInstances.getAll()
    |> Seq.filter (fun i -> System.Version(i.InstallationVersion).Major = 16)
    |> Seq.map
         (fun i ->
         i.InstallationPath @@ "Team Tools/Static Analysis Tools/FxCop/FxCopCmd.exe")
    |> Seq.filter File.Exists
    |> Seq.tryHead
  else
    None

let cliArguments =
  { MSBuild.CliArguments.Create() with
      ConsoleLogParameters = []
      DistributedLoggers = None
      Properties = [("CheckEolTargetFramework", "false")]
      DisableInternalBinLog = true }

let withWorkingDirectoryVM dir o =
  { dotnetOptions o with
      WorkingDirectory = Path.getFullName dir
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
  if currentBranch.StartsWith("release/", StringComparison.Ordinal)
  then currentBranch = "release/" + project
  else true

let toolPackages =
  let xml =
    "./Build/NuGet.csproj"
    |> Path.getFullName
    |> XDocument.Load
  xml.Descendants(XName.Get("PackageReference"))
  |> Seq.map
       (fun x ->
         (x.Attribute(XName.Get("Include")).Value, x.Attribute(XName.Get("version")).Value))
  |> Map.ofSeq

let packageVersion (p : string) = p.ToLowerInvariant() + "/" + (toolPackages.Item p)

let misses = ref 0

let uncovered (path : string) =
  misses := 0
  !!path
  |> Seq.collect (fun f ->
       let xml = XDocument.Load f
       xml.Descendants(XName.Get("Uncoveredlines"))
       |> Seq.filter (fun x ->
            match String.IsNullOrWhiteSpace x.Value with
            | false -> true
            | _ ->
                sprintf "No coverage from '%s'" f |> Trace.traceImportant
                misses := 1 + !misses
                false)
       |> Seq.map (fun e ->
            let coverage = e.Value
            match Int32.TryParse coverage with
            | (false, _) ->
                printfn "%A" xml
                Assert.Fail("Could not parse uncovered line value '" + coverage + "'")
                0
            | (_, numeric) ->
                printfn "%s : %A"
                  (f
                   |> Path.GetDirectoryName
                   |> Path.GetFileName) numeric
                numeric))
  |> Seq.toList

let buildWithCLIArguments (o : Fake.DotNet.DotNet.BuildOptions) =
  { o with MSBuildParams = cliArguments }

let dotnetBuildRelease proj =
  DotNet.build (fun p ->
    { p.WithCommon dotnetOptions with Configuration = DotNet.BuildConfiguration.Release }
    |> buildWithCLIArguments) (Path.GetFullPath proj)

let dotnetBuildDebug proj =
  DotNet.build (fun p ->
    { p.WithCommon dotnetOptions with Configuration = DotNet.BuildConfiguration.Debug }
    |> buildWithCLIArguments) (Path.GetFullPath proj)

let commitHash = Information.getCurrentSHA1 (".")
let infoV = Information.showName "." commitHash
printfn "Build at %A" infoV

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
  let github = Environment.environVar  "GITHUB_RUN_NUMBER"
  let now = DateTimeOffset.UtcNow
  let version = if currentBranch.Contains "VsWhat"
                then sprintf "%d.%d.%d.{build}" (now.Year-2000) now.Month now.Day
                else Actions.GetVersionFromYaml()
  printfn "Raw version %s" version

  let ci =
    if String.IsNullOrWhiteSpace appveyor then
      if String.IsNullOrWhiteSpace github
      then String.Empty
      else version.Replace("{build}", github + "-github")
    else
      version.Replace("{build}", appveyor)

  let (v, majmin, y) = Actions.LocalVersion ci version
  printfn " => %A" (v, majmin, y)
  Version := v
  let copy =
    sprintf "© 2010-%d by Steve Gilham <SteveGilham@users.noreply.github.com>" y
  Copyright := "Copyright " + copy
  Directory.ensure "./_Generated"
  Actions.InternalsVisibleTo(!Version)
  let v' = !Version
  [ "./_Generated/AssemblyVersion.fs"; "./_Generated/AssemblyVersion.cs" ]
  |> List.iter (fun file ->
       AssemblyInfoFile.create file
         [ AssemblyInfo.Product "AltCode.Fake"
           AssemblyInfo.Version(majmin + ".0.0")
           AssemblyInfo.FileVersion v'
           AssemblyInfo.Company "Steve Gilham"
           AssemblyInfo.Trademark ""
           AssemblyInfo.InformationalVersion(infoV)
           AssemblyInfo.Copyright copy ] (Some AssemblyInfoFileConfig.Default))
  let hack = """namespace AltCover
module SolutionRoot =
  let location = """ + "\"\"\"" + (Path.getFullName ".") + "\"\"\""
  let path = "_Generated/SolutionRoot.fs"

  // Update the file only if it would change
  let old =
    if File.Exists(path) then File.ReadAllText(path) else String.Empty
  if not (old.Equals(hack)) then File.WriteAllText(path, hack))

// Basic compilation

_Target "Compilation" ignore

_Target "BuildRelease" (fun _ ->
  try
    DotNet.restore (fun o -> o.WithCommon(withWorkingDirectoryVM ".")) "AltCode.Fake.sln"
    "AltCode.Fake.sln"
    |> dotnetBuildRelease
  with x ->
    printfn "%A" x
    reraise())
_Target "BuildDebug" (fun _ ->
  DotNet.restore (fun o -> o.WithCommon(withWorkingDirectoryVM ".")) "AltCode.Fake.sln"
  "AltCode.Fake.sln"
  |> dotnetBuildDebug )

// Code Analysis

_Target "Analysis" ignore

_Target "Lint" (fun _ ->
  let failOnIssuesFound (issuesFound : bool) =
    Assert.That(issuesFound, Is.False, "Lint issues were found")
  let options =
    { Lint.OptionalLintParameters.Default with
        Configuration = FromFile(Path.getFullName "./fsharplint.json") }

  !!"**/*.fsproj"
  |> Seq.collect (fun n -> !!(Path.GetDirectoryName n @@ "*.fs"))
  |> Seq.distinct
  |> Seq.collect (fun f ->
       match Lint.lintFile options f with
       | Lint.LintResult.Failure x -> failwithf "%A" x
       | Lint.LintResult.Success w ->
           w
           |> Seq.filter (fun x ->
                match x.Details.SuggestedFix with
                | Some l ->
                    match l.Force() with
                    | Some fix -> fix.FromText <> "AltCover_Fake" // special case
                    | _ -> false
                | _ -> false))
  |> Seq.fold (fun _ x ->
       printfn "Info: %A\r\n Range: %A\r\n Fix: %A\r\n====" x.Details.Message
         x.Details.Range x.Details.SuggestedFix
       true) false
  |> failOnIssuesFound)

_Target "Gendarme" (fun _ -> // Needs debug because release is compiled --standalone which contaminates everything

  Directory.ensure "./_Reports"

  let baseRules = Path.getFullName "./Build/rules-fake.xml"

  let rules =
    if Environment.isWindows then
      baseRules
    else
      // Gendarme mono doesn't into .pdb files
      let lines =
        baseRules
        |> File.ReadAllLines
        |> Seq.map
             (fun l ->
               l.Replace
                 ("AvoidSwitchStatementsRule",
                  "AvoidSwitchStatementsRule | AvoidLongMethodsRule"))

      let fixup = Path.getFullName "./_Generated/rules-fake.xml"
      File.WriteAllLines(fixup, lines)
      fixup

  [ (rules,
     [ "_Binaries/AltCode.Fake.DotNet.Gendarme/Debug+AnyCPU/net462/AltCode.Fake.DotNet.Gendarme.dll" ]) ]
  |> Seq.iter (fun (ruleset, files) ->
       Gendarme.run
         { Gendarme.Params.Create() with
             WorkingDirectory = "."
             Severity = Gendarme.Severity.All
             Confidence = Gendarme.Confidence.All
             Configuration = ruleset
             Console = true
             Log = "./_Reports/gendarme.html"
             LogKind = Gendarme.LogKind.Html
             Targets = files
             ToolType = ToolType.CreateLocalTool()
             FailBuildOnDefect = true }))

_Target "FxCop" (fun _ -> // Needs debug because release is compiled --standalone which contaminates everything

  Directory.ensure "./_Reports"
  [ ([ "_Binaries/AltCode.Fake.DotNet.Gendarme/Debug+AnyCPU/net462/AltCode.Fake.DotNet.Gendarme.dll" ],
     [],
     [
       "-Microsoft.Design#CA1006"; "-Microsoft.Design#CA1011"; "-Microsoft.Design#CA1020";
       "-Microsoft.Design#CA1062"; "-Microsoft.Design#CA1034"; "-Microsoft.Naming#CA1704";
       "-Microsoft.Naming#CA1707"; "-Microsoft.Naming#CA1709"; "-Microsoft.Naming#CA1724";
       "-Microsoft.Usage#CA2208"; "-Microsoft.Usage#CA2243:AttributeStringLiteralsShouldParseCorrectly" ]) ]
  |> Seq.iter (fun (files, types, ruleset) ->
       files
       |> FxCop.run
            { FxCop.Params.Create() with
                WorkingDirectory = "."
                ToolPath = Option.get fxcop
                UseGAC = true
                Verbose = false
                ReportFileName = "_Reports/FxCopReport.xml"
                Types = types
                Rules = ruleset
                FailOnError = FxCop.ErrorLevel.Warning
                IgnoreGeneratedCode = true }))

// Unit Test

_Target "UnitTest" (fun _ ->
  let numbers = (@"_Reports/_Unit*/Summary.xml") |> uncovered
  let omitted = numbers |> List.sum
  if omitted > 1 then
    omitted
    |> (sprintf "%d uncovered lines -- coverage too low")
    |> Assert.Fail)

_Target "BuildForUnitTestDotNet" (fun _ ->
  !!(@"./*Tests/*.tests.core.fsproj")
  |> Seq.iter
       (DotNet.build (fun p ->
         { p.WithCommon dotnetOptions with Configuration = DotNet.BuildConfiguration.Debug }
         |> withMSBuildParams)))

_Target "UnitTestDotNet" (fun _ ->
  Directory.ensure "./_Reports"
  try
    !!(@"./*Tests/*.fsproj")
    |> Seq.iter
         (DotNet.test (fun p ->
           { p.WithCommon dotnetOptions with
               Configuration = DotNet.BuildConfiguration.Debug
               NoBuild = true }
           |> withCLIArgs))
  with x ->
    printfn "%A" x
    reraise())

_Target "BuildForAltCover" (fun _ ->
  !!(@"./*Tests/*.tests.core.fsproj")
  |> Seq.iter
       (DotNet.build (fun p ->
         { p.WithCommon dotnetOptions with Configuration = DotNet.BuildConfiguration.Debug }
         |> withMSBuildParams)))

_Target "UnitTestDotNetWithAltCover" (fun _ ->
  let reports = Path.getFullName "./_Reports"
  Directory.ensure reports
  let report = "./_Reports/_UnitTestWithAltCoverCoreRunner"
  Directory.ensure report

  let coverage =
    !!(@"./**/*.Tests.fsproj")
    |> Seq.fold (fun l test ->
         printfn "%A" test
         let tname = test |> Path.GetFileNameWithoutExtension

         let testDirectory =
           test
           |> Path.getFullName
           |> Path.GetDirectoryName

         let altReport = reports @@ ("UnitTestWithAltCoverCoreRunner." + tname + ".xml")

         let collect = AltCover.CollectOptions.Primitive(Primitive.CollectOptions.Create()) // FSApi

         let prepare =
           AltCover.PrepareOptions.Primitive // FSApi
             ({ Primitive.PrepareOptions.Create() with
                  XmlReport = altReport
                  SingleVisit = true }
              |> AltCoverFilter)

         let forceTrue = DotNet.CLIOptions.Force true
         //printfn "Test arguments : '%s'" (DotNet.ToTestArguments prepare collect forceTrue)

         let t =
           DotNet.TestOptions.Create().WithAltCoverOptions prepare collect forceTrue
         printfn "WithAltCoverParameters returned '%A'" t.Common.CustomParams

         let setBaseOptions (o : DotNet.Options) =
           { o with
               WorkingDirectory = Path.getFullName testDirectory
               Verbosity = Some DotNet.Verbosity.Minimal }

         let cliArguments =
           { MSBuild.CliArguments.Create() with
               ConsoleLogParameters = []
               DistributedLoggers = None
               DisableInternalBinLog = true }

         try
           DotNet.test (fun to' ->
             { to'.WithCommon(setBaseOptions).WithAltCoverOptions prepare collect
                 forceTrue with MSBuildParams = cliArguments }) test
         with x -> printfn "%A" x
         // reraise()) // while fixing

         altReport :: l) []

  ReportGenerator.generateReports (fun p ->
    { p with
        ToolType = ToolType.CreateLocalTool()
        ReportTypes =
          [ ReportGenerator.ReportType.Html; ReportGenerator.ReportType.XmlSummary ]
        TargetDir = report }) coverage

  let reportLines = coverage |> List.map File.ReadAllLines

  let top =
    reportLines
    |> List.head
    |> Seq.takeWhile (fun l -> l.StartsWith("    <Module") |> not)
  let tail =
    reportLines
    |> List.head
    |> Seq.skipWhile (fun l -> l <> "  </Modules>")
  let core =
    reportLines
    |> List.map (fun f ->
         f
         |> Seq.skipWhile (fun l -> l.StartsWith("    <Module") |> not)
         |> Seq.takeWhile (fun l -> l <> "  </Modules>"))

  let coverage = reports @@ "CombinedTestWithAltCoverRunner.coveralls"
  File.WriteAllLines
    (coverage,
     Seq.concat
       [ top
         Seq.concat core
         tail ]
     |> Seq.toArray)

  if Environment.isWindows &&
     "COVERALLS_REPO_TOKEN"
     |> Environment.environVar
     |> String.IsNullOrWhiteSpace
     |> not
  then
    let coveralls =
      ("./packages/" + (packageVersion "coveralls.io") + "/tools/coveralls.net.exe")
      |> Path.getFullName

    Actions.Run
      (coveralls, "_Reports",
         [ "--opencover"; coverage; "--debug" ]) "Coveralls upload failed"

  (report @@ "Summary.xml")
  |> uncovered
  |> printfn "%A uncovered lines")

// Pure OperationalTests

_Target "OperationalTest" ignore

// Packaging

_Target "Packaging" (fun _ ->
  let gendarmeDir =
    Path.getFullName "_Binaries/AltCode.Fake.DotNet.Gendarme/Release+AnyCPU/net462"
  let packable = Path.getFullName "./_Binaries/README.html"

  let gendarmeFiles =
    [ (gendarmeDir @@ "AltCode.Fake.DotNet.Gendarme.dll", Some "lib/net462", None)
      (gendarmeDir @@ "AltCode.Fake.DotNet.Gendarme.pdb", Some "lib/net462", None)
      (Path.getFullName "./LICENS*", Some "", None)
      (Path.getFullName "./Build/AltCode.Fake_128.*g", Some "", None)
      (packable, Some "", None) ]

  let gendarmeNetcoreFiles =
    (!!(gendarmeDir @@ "netstandard2.0/AltCode.Fake.DotNet.Gendarme.*"))
    |> Seq.map (fun x -> (x, Some "lib/netstandard2.0", None))
    |> Seq.toList

  let publishWhat = (Path.getFullName "./_Publish.vsWhat").Length

  let whatFiles where =
    (!!"./_Publish.vsWhat/**/*.*")
    |> Seq.map (fun x ->
         (x,
          Some(where + Path.GetDirectoryName(x).Substring(publishWhat).Replace("\\", "/")),
          None))
    |> Seq.toList

  let whatPack =
    [ (Path.getFullName "./LICENS*", Some "", None)
      (Path.getFullName "./Build/AltCode.VsWhat_128.*g", Some "", None)
      (packable, Some "", None) ]

  [ (List.concat [ gendarmeFiles; gendarmeNetcoreFiles ], "_Packaging.Gendarme",
     "./_Generated/altcode.fake.dotnet.gendarme.nuspec", "AltCode.Fake.DotNet.Gendarme",
     "A helper task for running Mono.Gendarme from FAKE ( >= 5.18.1 )", "Gendarme",
     [ // make these explicit, as this package implies an opt-in
       ("Fake.Core.Environment", "5.18.1")
       ("Fake.DotNet.Cli", "5.18.1")
       ("FSharp.Core", "4.7")
       ("System.Collections.Immutable", "1.6.0") ])
    (List.concat
      [ whatFiles "tools/netcoreapp2.1/any"
        whatPack ], "_Packaging.VsWhat", "./_Generated/altcode.vswhat.nuspec",
     "AltCode.VsWhat",
     "A tool to list Visual Studio instances and their installed packages", "VsWhat", []) ]
  |> List.filter (fun (_, _, _, _, _, what, _) -> package what)
  |> List.iter (fun (files, output, nuspec, project, description, what, dependencies) ->
       let outputPath = "./" + output
       let workingDir = "./_Binaries/" + output
       Directory.ensure workingDir
       Directory.ensure outputPath
       NuGet (fun p ->
         { p with
             Authors = [ "Steve Gilham" ]
             Project = project
             Description = description
             OutputPath = outputPath
             WorkingDir = workingDir
             Files = files
             Dependencies = dependencies
             Version = !Version
             Copyright = (!Copyright).Replace("©", "(c)")
             Publish = false
             ReleaseNotes =
               Path.getFullName ("ReleaseNotes." + what + ".md") |> File.ReadAllText
             ToolPath =
               if Environment.isWindows then
                 ("./packages/" + (packageVersion "NuGet.CommandLine")
                  + "/tools/NuGet.exe") |> Path.getFullName
               else
                 "/usr/bin/nuget" }) nuspec))

_Target "PrepareFrameworkBuild" ignore

_Target "PrepareDotNetBuild" (fun _ ->
  let publish = Path.getFullName "./_Publish"

  DotNet.publish (fun options ->
    { options with
        OutputPath = Some(publish + ".vsWhat")
        Configuration = DotNet.BuildConfiguration.Release
        Framework = Some "netcoreapp2.1" })
    (Path.getFullName "./AltCode.VsWhat/AltCode.VsWhat.fsproj")

  [ (String.Empty, "./_Generated/altcode.fake.dotnet.gendarme.nuspec",
     "AltCode.Fake.DotNet.Gendarme (FAKE task helper)", None, Some "FAKE build Gendarme")
    ("DotnetTool", "./_Generated/altcode.vswhat.nuspec",
     "AltCode.VsWhat (Visual Studio package listing tool)",
     Some "Build/AltCode.VsWhat_128.png", Some "Visual Studio") ]
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
           let text =
             String.Concat(tag.Nodes()).Replace("Build/AltCode.Fake_128.png", logo)
           tag.Value <- text
           let tag2 = dotnetNupkg.Descendants(x "icon") |> Seq.head
           tag2.Value <- logo |> Path.GetFileName
       match tags with
       | None -> ()
       | Some line ->
           let tagnode = dotnetNupkg.Descendants(x "tags") |> Seq.head
           tagnode.Value <- line
       dotnetNupkg.Save path))

_Target "PrepareReadMe" (fun _ ->
  Actions.PrepareReadMe
    ((!Copyright).Replace("©", "&#xa9;").Replace("<", "&lt;").Replace(">", "&gt;")))

// Post-packaging deployment touch test

_Target "AltCodeVsWhatGlobalIntegration" (fun _ ->
  let working = Path.getFullName "./_AltCodeVsWhatTest"
  let mutable set = false
  try
    Directory.ensure working
    Shell.cleanDir working

    Actions.RunDotnet (fun o' -> { dotnetOptions o' with WorkingDirectory = working })
      "tool"
      ("install -g altcode.vswhat --add-source "
       + (Path.getFullName "./_Packaging.VsWhat") + " --version " + !Version) "Installed"

    Actions.RunDotnet (fun o' -> { dotnetOptions o' with WorkingDirectory = working })
      "tool" ("list -g ") "Checked"
    set <- true

    CreateProcess.fromRawCommand "altcode-vswhat" []
    |> CreateProcess.withWorkingDirectory working
    |> Proc.run
    |> (Actions.AssertResult "altcode.vswhat")

  finally
    if set then
      Actions.RunDotnet (fun o' -> { dotnetOptions o' with WorkingDirectory = working })
        "tool" ("uninstall -g altcode.vswhat") "uninstalled"
    let folder = (nugetCache @@ "altcode.vswhat") @@ !Version
    Shell.mkdir folder
    Shell.deleteDir folder)

_Target "Deployment" ignore

// AOB

_Target "BulkReport" (fun _ ->
  printfn "Overall coverage reporting"
  Directory.ensure "./_Reports/_BulkReport"
  !!"./_Reports/*.xml"
  |> Seq.filter (fun f ->
       not <| f.EndsWith("Report.xml", StringComparison.OrdinalIgnoreCase))
  |> Seq.toList
  |> ReportGenerator.generateReports (fun p ->
       { p with
           ToolType = ToolType.CreateLocalTool()
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
==> "BuildForUnitTestDotNet"
==> "UnitTestDotNet"
==> "UnitTest"

"UnitTestDotNet"
==> "BuildForAltCover"
==> "UnitTestDotNetWithAltCover"
==> "UnitTest"

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

"Packaging"
==> "AltCodeVsWhatGlobalIntegration"
=?> ("Deployment", Environment.isWindows) // Not sure about VS for non-Windows

"Deployment"
==> "BulkReport"
==> "All"

let defaultTarget() =
  resetColours()
  "All"

Target.runOrDefault <| defaultTarget()