namespace AltCode.Fake

module Targets =

  open System
  open System.IO
  open System.Xml.Linq

  open Actions
  open AltCode.Fake.DotNet
  open AltCoverFake.DotNet.DotNet
  open AltCoverFake.DotNet.Testing

  open Fake.Core
  open Fake.Core.TargetOperators
  open Fake.DotNet
  open Fake.DotNet.NuGet.NuGet
  open Fake.Testing
  open Fake.DotNet.Testing
  open Fake.IO
  open Fake.IO.FileSystemOperators
  open Fake.IO.Globbing.Operators
  open Fake.Tools.Git

  open NUnit.Framework

  let Copyright = ref String.Empty
  let Version = ref String.Empty

  let consoleBefore =
    (Console.ForegroundColor, Console.BackgroundColor)

  let programFiles =
    Environment.environVar "ProgramFiles"

  let programFiles86 =
    Environment.environVar "ProgramFiles(x86)"

  let dotnetPath =
    "dotnet"
    |> Fake.Core.ProcessUtils.tryFindFileOnPath

  let AltCoverFilter (p: Primitive.PrepareOptions) =
    { p with
        //MethodFilter = "WaitForExitCustom" :: (p.MethodFilter |> Seq.toList)
        AssemblyExcludeFilter =
          @"NUnit3\."
          :: (@"\.Tests"
              :: (p.AssemblyExcludeFilter |> Seq.toList))
        AssemblyFilter =
          "FSharp"
          :: @"\.Placeholder"
             :: (p.AssemblyFilter |> Seq.toList)
        LocalSource = true
        TypeFilter =
          [ @"System\."; "Microsoft" ]
          @ (p.TypeFilter |> Seq.toList) }

  let dotnetOptions (o: DotNet.Options) =
    match dotnetPath with
    | Some f -> { o with DotNetCliPath = f }
    | None -> o

  let dotnetInfo =
    DotNet.exec (fun o -> dotnetOptions (o.WithRedirectOutput true)) "" "--info"

  let dotnetSdkPath =
    dotnetInfo.Results
    |> Seq.filter (fun x -> x.IsError |> not)
    |> Seq.map (fun x -> x.Message)
    |> Seq.tryFind (fun x -> x.Contains "Base Path:")
    |> Option.map (fun x -> x.Replace("Base Path:", "").TrimStart())

  let refdir =
    dotnetSdkPath
    |> Option.map (fun path -> path @@ "ref")

  let nugetCache =
    Path.Combine(
      Environment.GetFolderPath Environment.SpecialFolder.UserProfile,
      ".nuget/packages"
    )

  let (fxcop, dixon) =
    if Environment.isWindows then
      let expect =
        "./packages/fxcop/FxCopCmd.exe"
        |> Path.getFullName

      if File.Exists expect then
        (Some expect,
         Some(
           "./packages/fxcop/DixonCmd.exe"
           |> Path.getFullName
         ))
      else
        (None, None)
    else
      (None, None)

  let cliArguments =
    { MSBuild.CliArguments.Create() with
        ConsoleLogParameters = []
        DistributedLoggers = None
        Properties = [ ("CheckEolTargetFramework", "false") ]
        DisableInternalBinLog = true }

  let withWorkingDirectoryVM dir o =
    { dotnetOptions o with
        WorkingDirectory = Path.getFullName dir
        Verbosity = Some DotNet.Verbosity.Minimal }

  let withWorkingDirectoryOnly dir o =
    { dotnetOptions o with WorkingDirectory = Path.getFullName dir }

  let withCLIArgs (o: Fake.DotNet.DotNet.TestOptions) =
    { o with MSBuildParams = cliArguments }

  let withMSBuildParams (o: Fake.DotNet.DotNet.BuildOptions) =
    { o with MSBuildParams = cliArguments }

  let currentBranch =
    "."
    |> Path.getFullName
    |> Information.getBranchName

  let packageGendarme =
    if currentBranch.Contains "VsWhat" then
      "_ProForma.Gendarme"
    else
      "_Packaging.Gendarme"

  let packageVsWhat =
    if currentBranch.Contains "Gendarme" then
      "_ProForma.VsWhat"
    else
      "_Packaging.VsWhat"

  let toolPackages =
    let xml =
      "./Directory.Packages.props"
      |> Path.getFullName
      |> XDocument.Load

    xml.Descendants()
    |> Seq.filter (fun x -> x.Attribute(XName.Get("Include")) |> isNull |> not)
    |> Seq.map (fun x ->
      (x.Attribute(XName.Get("Include")).Value, x.Attribute(XName.Get("Version")).Value))
    |> Map.ofSeq

  let packageVersion (p: string) =
    p.ToLowerInvariant() + "/" + (toolPackages.Item p)

  let misses = ref 0

  let uncovered (path: string) =
    misses.Value <- 0

    !!path
    |> Seq.collect (fun f ->
      let xml = XDocument.Load f

      xml.Descendants(XName.Get("Uncoveredlines"))
      |> Seq.filter (fun x ->
        match String.IsNullOrWhiteSpace x.Value with
        | false -> true
        | _ ->
          sprintf "No coverage from '%s'" f
          |> Trace.traceImportant

          misses.Value <- 1 + misses.Value
          false)
      |> Seq.map (fun e ->
        let coverage = e.Value

        match Int32.TryParse coverage with
        | (false, _) ->
          printfn "%A" xml

          Assert.Fail(
            "Could not parse uncovered line value '"
            + coverage
            + "'"
          )

          0
        | (_, numeric) ->
          printfn "%s : %A" (f |> Path.GetDirectoryName |> Path.GetFileName) numeric
          numeric))
    |> Seq.toList

  let buildWithCLIArguments (o: Fake.DotNet.DotNet.BuildOptions) =
    { o with MSBuildParams = cliArguments }

  let dotnetBuildRelease proj =
    DotNet.build
      (fun p ->
        { p.WithCommon dotnetOptions with
            Configuration = DotNet.BuildConfiguration.Release }
        |> buildWithCLIArguments)
      (Path.GetFullPath proj)

  let dotnetBuildDebug proj =
    DotNet.build
      (fun p ->
        { p.WithCommon dotnetOptions with Configuration = DotNet.BuildConfiguration.Debug }
        |> buildWithCLIArguments)
      (Path.GetFullPath proj)

  let commitHash =
    Information.getCurrentSHA1 (".")

  let infoV =
    Information.showName "." commitHash

  printfn "Build at %A" infoV

  let _Target s f =
    let doTarget s f =
      let banner x =
        printfn ""
        printfn " ****************** %s ******************" s
        f x

      Target.create s banner

    Target.description s
    doTarget s f

    let s2 = "Replay" + s
    Target.description s2
    doTarget s2 f

  // Preparation

  let Clean =
    (fun _ ->
      printfn "Cleaning the build and deploy folders for %A" currentBranch
      Actions.Clean())

  let SetVersion =
    (fun _ ->
      let github =
        Environment.environVar "GITHUB_RUN_NUMBER"

      let now = DateTimeOffset.UtcNow

      let trailer =
        if currentBranch.StartsWith "release/" then
          String.Empty
        else
          "-pre"

      let version =
        (if currentBranch.Contains "VsWhat" then
           sprintf "%d.%d.%d.{build}" (now.Year - 2000) now.Month now.Day
         else
           Actions.GetVersionFromYaml())
        + trailer

      printfn "Raw version %s" version

      let ci =
        if String.IsNullOrWhiteSpace github then
          String.Empty
        else
          version.Replace("{build}", github)

      let (v, majmin, y) =
        Actions.LocalVersion ci version

      printfn " => %A" (v, majmin, y)
      Version.Value <- v

      let copy =
        sprintf "© 2010-%d by Steve Gilham <SteveGilham@users.noreply.github.com>" y

      Copyright.Value <- "Copyright " + copy
      Directory.ensure "./_Generated"
      Actions.InternalsVisibleTo(Version.Value)
      let v' = Version.Value

      [ "./_Generated/AssemblyVersion.fs"
        "./_Generated/AssemblyVersion.cs" ]
      |> List.iter (fun file ->
        AssemblyInfoFile.create
          file
          [ AssemblyInfo.Product "AltCode.Fake"
            AssemblyInfo.Version(majmin + ".0.0")
            AssemblyInfo.FileVersion v'
            AssemblyInfo.Company "Steve Gilham"
            AssemblyInfo.Trademark ""
            AssemblyInfo.InformationalVersion(infoV)
            AssemblyInfo.Copyright copy ]
          (Some AssemblyInfoFileConfig.Default))

      let hack =
        """namespace AltCover
  module SolutionRoot =
    let location = """
        + "\"\"\""
        + (Path.getFullName ".")
        + "\"\"\""

      let path = "_Generated/SolutionRoot.fs"

      // Update the file only if it would change
      let old =
        if File.Exists(path) then
          File.ReadAllText(path)
        else
          String.Empty

      if not (old.Equals(hack)) then
        File.WriteAllText(path, hack))

  // Basic compilation

  let BuildRelease =
    (fun _ ->
      try
        DotNet.restore
          (fun o -> o.WithCommon(withWorkingDirectoryVM "."))
          "AltCode.Fake.sln"

        "AltCode.Fake.sln" |> dotnetBuildRelease
      with x ->
        printfn "%A" x
        reraise ())

  let BuildDebug =
    (fun _ ->
      DotNet.restore
        (fun o -> o.WithCommon(withWorkingDirectoryVM "."))
        "AltCode.Fake.sln"

      "AltCode.Fake.sln" |> dotnetBuildDebug)

  // Code Analysis

  let Lint =
    (fun _ ->
      let cfg =
        Path.getFullName "./fsharplint.json"

      let doLint f =
        CreateProcess.fromRawCommand "dotnet" [ "fsharplint"; "lint"; "-l"; cfg; f ]
        |> CreateProcess.ensureExitCodeWithMessage "Lint issues were found"
        |> Proc.run

      let doLintAsync f = async { return (doLint f).ExitCode }

      let throttle x =
        Async.Parallel(x, System.Environment.ProcessorCount)

      let demo = Path.getFullName "./Demo"

      let regress =
        Path.getFullName "./RegressionTesting"

      let sample = Path.getFullName "./Samples"

      let underscore = Path.getFullName "./_"

      let failOnIssuesFound (issuesFound: bool) =
        Assert.That(issuesFound, Is.False, "Lint issues were found")

      [ !! "./**/*.fsproj"
        |> Seq.sortBy (Path.GetFileName)
        |> Seq.filter (fun f ->
          ((f.Contains demo)
           || (f.Contains regress)
           || (f.Contains underscore)
           || (f.Contains sample))
          |> not)
        !! "./Build/*.fsx" |> Seq.map Path.GetFullPath ]
      |> Seq.concat
      |> Seq.map doLintAsync
      |> throttle
      |> Async.RunSynchronously
      |> Seq.exists (fun x -> x <> 0)
      |> failOnIssuesFound)

  let Gendarme =
    (fun _ -> // Needs debug because release is compiled --standalone which contaminates everything

      Directory.ensure "./_Reports"

      let rules =
        Path.getFullName "./Build/rules-fake.xml"


      [ (rules,
         [ "_Binaries/AltCode.Fake.DotNet.Gendarme/Debug+AnyCPU/netstandard2.0/AltCode.Fake.DotNet.Gendarme.dll" ])
        (Path.getFullName "./Build/build-rules.xml",
         [ "$Binaries/Build/Debug+AnyCPU/net7.0/Build.dll"
           "$Binaries/Setup/Debug+AnyCPU/net7.0/Setup.dll" ]) ]
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

  let FxCop =
    (fun _ -> // Needs debug because release is compiled --standalone which contaminates everything

      Directory.ensure "./_Reports"

      let dd =
        toolPackages
        |> Map.toSeq
        |> Seq.map (fun (k, v) -> k.ToLowerInvariant(), v)
        |> Map.ofSeq

      printfn "%A" dd

      let ddItem x =
        try
          dd.Item x
        with _ ->
          printfn "Failed to get %A" x
          reraise ()

      [ ([ "_Binaries/AltCode.Fake.DotNet.Gendarme/Debug+AnyCPU/netstandard2.0/AltCode.Fake.DotNet.Gendarme.dll" ],
         [],
         [ "-Microsoft.Design#CA1006"
           "-Microsoft.Design#CA1011"
           "-Microsoft.Design#CA1020"
           "-Microsoft.Design#CA1062"
           "-Microsoft.Design#CA1034"
           "-Microsoft.Naming#CA1704"
           "-Microsoft.Naming#CA1707"
           "-Microsoft.Naming#CA1709"
           "-Microsoft.Naming#CA1724"
           "-Microsoft.Usage#CA2208"
           "-Microsoft.Usage#CA2243:AttributeStringLiteralsShouldParseCorrectly" ]) ]
      |> Seq.iter (fun (files, types, ruleset) ->
        files
        |> FxCop.run
             { FxCop.Params.Create() with
                 WorkingDirectory = "."
                 ToolPath = Option.get dixon
                 PlatformDirectory = Option.get refdir
                 DependencyDirectories =
                   [ nugetCache
                     @@ "fake.core.process/"
                        + (ddItem "fake.core.process")
                        + "/lib/netstandard2.0"
                     nugetCache
                     @@ "fake.core.trace/"
                        + (ddItem "fake.core.trace")
                        + "/lib/netstandard2.0"
                     nugetCache
                     @@ "fake.dotnet.cli/"
                        + (ddItem "fake.dotnet.cli")
                        + "/lib/netstandard2.0"
                     nugetCache
                     @@ "fsharp.core/"
                        + (ddItem "fsharp.core")
                        + "/lib/netstandard2.0" ]
                 UseGAC = true
                 Verbose = false
                 ReportFileName = "_Reports/FxCopReport.xml"
                 Types = types
                 Rules = ruleset
                 FailOnError = FxCop.ErrorLevel.Warning
                 IgnoreGeneratedCode = true }))

  // Unit Test

  let UnitTest =
    (fun _ ->
      let numbers =
        (@"_Reports/_Unit*/Summary.xml") |> uncovered

      let omitted = numbers |> List.sum

      if omitted > 1 then
        omitted
        |> (sprintf "%d uncovered lines -- coverage too low")
        |> Assert.Fail)

  let BuildForUnitTestDotNet =
    (fun _ ->
      !!(@"./*Tests/*.tests.core.fsproj")
      |> Seq.iter (
        DotNet.build (fun p ->
          { p.WithCommon dotnetOptions with
              Configuration = DotNet.BuildConfiguration.Debug }
          |> withMSBuildParams)
      ))

  let UnitTestDotNet =
    (fun _ ->
      Directory.ensure "./_Reports"

      try
        !!(@"./*Tests/*.fsproj")
        |> Seq.iter (
          DotNet.test (fun p ->
            { p.WithCommon dotnetOptions with
                Configuration = DotNet.BuildConfiguration.Debug
                NoBuild = true }
            |> withCLIArgs)
        )
      with x ->
        printfn "%A" x
        reraise ())

  let BuildForAltCover =
    (fun _ ->
      !!(@"./*Tests/*.tests.core.fsproj")
      |> Seq.iter (
        DotNet.build (fun p ->
          { p.WithCommon dotnetOptions with
              Configuration = DotNet.BuildConfiguration.Debug }
          |> withMSBuildParams)
      ))

  let UnitTestDotNetWithAltCover =
    (fun _ ->
      let reports = Path.getFullName "./_Reports"
      Directory.ensure reports

      let report =
        "./_Reports/_UnitTestWithAltCoverCoreRunner"

      Directory.ensure report

      let coverage =
        !!(@"./**/*.Tests.fsproj")
        |> Seq.fold
             (fun l test ->
               printfn "%A" test

               let tname =
                 test |> Path.GetFileNameWithoutExtension

               let testDirectory =
                 test |> Path.getFullName |> Path.GetDirectoryName

               let altReport =
                 reports
                 @@ ("UnitTestWithAltCoverCoreRunner." + tname + ".xml")

               let collect =
                 AltCover.CollectOptions.Primitive(Primitive.CollectOptions.Create()) // FSApi

               let prepare =
                 AltCover.PrepareOptions.Primitive(
                   { Primitive.PrepareOptions.Create() with
                       Report = altReport
                       SingleVisit = true }
                   |> AltCoverFilter
                 )

               let forceTrue = DotNet.CLIOptions.Force true
               //printfn "Test arguments : '%s'" (DotNet.ToTestArguments prepare collect forceTrue)

               let t =
                 DotNet.TestOptions.Create().WithAltCoverOptions prepare collect forceTrue

               printfn "WithAltCoverParameters returned '%A'" t.Common.CustomParams

               let setBaseOptions (o: DotNet.Options) =
                 { o with
                     WorkingDirectory = Path.getFullName testDirectory
                     Verbosity = Some DotNet.Verbosity.Minimal }

               let cliArguments =
                 { MSBuild.CliArguments.Create() with
                     ConsoleLogParameters = []
                     DistributedLoggers = None
                     DisableInternalBinLog = true }

               try
                 DotNet.test
                   (fun to' ->
                     { (to'.WithCommon(setBaseOptions).WithAltCoverOptions
                         prepare
                         collect
                         forceTrue) with MSBuildParams = cliArguments })
                   test
               with x ->
                 printfn "%A" x
               // reraise()) // while fixing

               altReport :: l)
             []

      ReportGenerator.generateReports
        (fun p ->
          { p with
              ToolType = ToolType.CreateLocalTool()
              ReportTypes =
                [ ReportGenerator.ReportType.Html
                  ReportGenerator.ReportType.XmlSummary ]
              TargetDir = report })
        coverage

      let reportLines =
        coverage |> List.map File.ReadAllLines

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

      let coverage =
        reports
        @@ "CombinedTestWithAltCoverRunner.coveralls"

      File.WriteAllLines(
        coverage,
        Seq.concat [ top; Seq.concat core; tail ]
        |> Seq.toArray
      )

      if
        Environment.isWindows
        && "COVERALLS_REPO_TOKEN"
           |> Environment.environVar
           |> String.IsNullOrWhiteSpace
           |> not
      then
        let maybe envvar fallback =
          let x = Environment.environVar envvar

          if String.IsNullOrWhiteSpace x then
            fallback
          else
            x

        let log = Information.shortlog "."
        let gap = log.IndexOf ' '
        let commit = log.Substring gap

        Actions.Run
          ("dotnet",
           "_Reports",
           [ "csmacnz.Coveralls"
             "--opencover"
             "-i"
             coverage
             "--repoToken"
             Environment.environVar "COVERALLS_REPO_TOKEN"
             "--commitId"
             commitHash
             "--commitBranch"
             Information.getBranchName (".")
             "--commitAuthor"
             maybe "COMMIT_AUTHOR" "" // TODO
             "--commitEmail"
             maybe "COMMIT_AUTHOR_EMAIL" "" //
             "--commitMessage"
             commit
             "--jobId"
             DateTime.UtcNow.ToString("yyMMdd-HHmmss") ])
          "Coveralls upload failed"

      (report @@ "Summary.xml")
      |> uncovered
      |> printfn "%A uncovered lines")

  // Pure OperationalTests

  // Packaging

  let Packaging =
    (fun _ ->
      let gendarmeDir =
        Path.getFullName "_Binaries/AltCode.Fake.DotNet.Gendarme/Release+AnyCPU"

      let gendarmeFiles =
        [ (Path.getFullName "./LICENS*", Some "", None)
          (Path.getFullName "./Build/AltCode.Fake_128.*g", Some "", None)
          (Path.getFullName "./Build/README.Fake.md", Some "", None)
          (Path.getFullName "./_Binaries/README.Fake.html", Some "", None) ]

      let gendarmeNetcoreFiles =
        (!!(gendarmeDir
            @@ "netstandard2.0/AltCode.Fake.DotNet.Gendarme.*"))
        |> Seq.map (fun x -> (x, Some "lib/netstandard2.0", None))
        |> Seq.toList

      let publishWhat =
        (Path.getFullName "./_Publish.vsWhat").Length

      let whatFiles where =
        (!! "./_Publish.vsWhat/**/*.*")
        |> Seq.map (fun x ->
          (x,
           Some(
             where
             + Path
               .GetDirectoryName(x)
               .Substring(publishWhat)
               .Replace("\\", "/")
           ),
           None))
        |> Seq.toList

      let whatPack =
        [ (Path.getFullName "./LICENS*", Some "", None)
          (Path.getFullName "./Build/AltCode.VsWhat_128.*g", Some "", None)
          (Path.getFullName "./Build/README.What.md", Some "", None)
          (Path.getFullName "./_Binaries/README.What.html", Some "", None) ]

      [ (List.concat [ gendarmeFiles; gendarmeNetcoreFiles ],
         packageGendarme,
         "./_Generated/altcode.fake.dotnet.gendarme.nuspec",
         "AltCode.Fake.DotNet.Gendarme",
         "A helper task for running Mono.Gendarme from FAKE ( >= 5.23.0 )",
         "Gendarme",
         [ // make these explicit, as this package implies an opt-in
           ("Fake.Core.Environment", "5.23.0")
           ("Fake.DotNet.Cli", "5.23.0")
           ("FSharp.Core", "4.7")
           ("System.Collections.Immutable", "1.6.0") ])
        (List.concat
          [ whatFiles "tools/netcoreapp2.1/any"
            whatPack ],
         packageVsWhat,
         "./_Generated/altcode.vswhat.nuspec",
         "AltCode.VsWhat",
         "A tool to list Visual Studio instances and their installed packages",
         "VsWhat",
         []) ]
      |> List.iter
        (fun (files, output, nuspec, project, description, what, dependencies) ->
          let outputPath = "./" + output
          let workingDir = "./_Binaries/" + output
          Directory.ensure workingDir
          Directory.ensure outputPath

          NuGet
            (fun p ->
              { p with
                  Authors = [ "Steve Gilham" ]
                  Project = project
                  Description = description
                  OutputPath = outputPath
                  WorkingDir = workingDir
                  Files = files
                  Dependencies = dependencies
                  Version = Version.Value
                  Copyright = Copyright.Value.Replace("©", "(c)")
                  Publish = false
                  ReleaseNotes =
                    Path.getFullName ("ReleaseNotes." + what + ".md")
                    |> File.ReadAllText
                  ToolPath =
                    if Environment.isWindows then
                      ("./packages/"
                       + (packageVersion "NuGet.CommandLine")
                       + "/tools/NuGet.exe")
                      |> Path.getFullName
                    else
                      "/usr/bin/nuget" })
            nuspec))

  let PrepareDotNetBuild =
    (fun _ ->
      let publish = Path.getFullName "./_Publish"

      DotNet.publish
        (fun options ->
          { options with
              OutputPath = Some(publish + ".vsWhat")
              Configuration = DotNet.BuildConfiguration.Release
              Framework = Some "netcoreapp2.1" })
        (Path.getFullName "./AltCode.VsWhat/AltCode.VsWhat.fsproj")

      [ (String.Empty,
         "./_Generated/altcode.fake.dotnet.gendarme.nuspec",
         "AltCode.Fake.DotNet.Gendarme (FAKE task helper)",
         None,
         Some "FAKE build Gendarme",
         "README.Fake.md")
        ("DotnetTool",
         "./_Generated/altcode.vswhat.nuspec",
         "AltCode.VsWhat (Visual Studio package listing tool)",
         Some "Build/AltCode.VsWhat_128.png",
         Some "Visual Studio",
         "README.What.md") ]
      |> List.iter (fun (ptype, path, caption, icon, tags, readme) ->
        let x s =
          XName.Get(s, "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd")

        let dotnetNupkg =
          XDocument.Load "./Build/AltCode.Fake.nuspec"

        dotnetNupkg.Descendants(x "readme")
        |> Seq.iter (fun hint -> hint.SetValue readme)

        let title =
          dotnetNupkg.Descendants(x "title") |> Seq.head

        title.ReplaceNodes caption

        if ptype |> String.IsNullOrWhiteSpace |> not then
          let tag =
            dotnetNupkg.Descendants(x "tags") |> Seq.head

          let insert = XElement(x "packageTypes")
          insert.Add(XElement(x "packageType", XAttribute(XName.Get "name", ptype)))
          tag.AddAfterSelf insert

        match icon with
        | None -> ()
        | Some logo ->
          let tag =
            dotnetNupkg.Descendants(x "iconUrl") |> Seq.head

          let text =
            String
              .Concat(tag.Nodes())
              .Replace("Build/AltCode.Fake_128.png", logo)

          tag.Value <- text

          let tag2 =
            dotnetNupkg.Descendants(x "icon") |> Seq.head

          tag2.Value <- logo |> Path.GetFileName

        match tags with
        | None -> ()
        | Some line ->
          let tagnode =
            dotnetNupkg.Descendants(x "tags") |> Seq.head

          tagnode.Value <- line

        dotnetNupkg.Save path))

  let PrepareReadMe =
    (fun _ ->
      let c =
        Copyright
          .Value
          .Replace("©", "&#xa9;")
          .Replace("<", "&lt;")
          .Replace(">", "&gt;")

      [ "./Build/README.Fake.md"
        "./Build/README.What.md" ]
      |> Seq.iter (Actions.PrepareReadMe c))

  // Post-packaging deployment touch test

  let AltCodeVsWhatGlobalIntegration =
    (fun _ ->
      let working =
        Path.getFullName "./_AltCodeVsWhatTest"

      let mutable set = false

      try
        Directory.ensure working
        Shell.cleanDir working

        Actions.RunDotnet
          (fun o' -> { dotnetOptions o' with WorkingDirectory = working })
          "tool"
          ("install -g altcode.vswhat --add-source "
           + (Path.getFullName packageVsWhat)
           + " --version "
           + Version.Value)
          "Installed"

        Actions.RunDotnet
          (fun o' -> { dotnetOptions o' with WorkingDirectory = working })
          "tool"
          ("list -g ")
          "Checked"

        set <- true

        CreateProcess.fromRawCommand "altcode-vswhat" []
        |> CreateProcess.withWorkingDirectory working
        |> Proc.run
        |> (Actions.AssertResult "altcode.vswhat")

      finally
        if set then
          Actions.RunDotnet
            (fun o' -> { dotnetOptions o' with WorkingDirectory = working })
            "tool"
            ("uninstall -g altcode.vswhat")
            "uninstalled"

        let folder =
          (nugetCache @@ "altcode.vswhat") @@ Version.Value

        Shell.mkdir folder
        Shell.deleteDir folder)

  // AOB

  let BulkReport =
    (fun _ ->
      printfn "Overall coverage reporting"
      Directory.ensure "./_Reports/_BulkReport"

      !! "./_Reports/*.xml"
      |> Seq.filter (fun f ->
        not
        <| f.EndsWith("Report.xml", StringComparison.OrdinalIgnoreCase))
      |> Seq.toList
      |> ReportGenerator.generateReports (fun p ->
        { p with
            ToolType = ToolType.CreateLocalTool()
            ReportTypes = [ ReportGenerator.ReportType.Html ]
            TargetDir = "_Reports/_BulkReport" }))

  let All =
    (fun _ ->
      if
        Environment.isWindows
        && currentBranch.StartsWith "release/"
        && "NUGET_API_TOKEN"
           |> Environment.environVar
           |> String.IsNullOrWhiteSpace
           |> not
      then
        (!! "./_Packagin*/*.nupkg")
        |> Seq.iter (fun f ->
          printfn "Publishing %A from %A" f currentBranch

          Actions.Run
            ("dotnet",
             ".",
             [ "nuget"
               "push"
               f
               "--api-key"
               Environment.environVar "NUGET_API_TOKEN"
               "--source"
               "https://api.nuget.org/v3/index.json" ])
            ("NuGet upload failed " + f)))

  let resetColours _ =
    Console.ForegroundColor <- consoleBefore |> fst
    Console.BackgroundColor <- consoleBefore |> snd

  Target.description "ResetConsoleColours"
  Target.createFinal "ResetConsoleColours" resetColours
  Target.activateFinal "ResetConsoleColours"

  let initTargets () =
    _Target "Preparation" ignore
    _Target "Clean" Clean
    _Target "SetVersion" SetVersion
    _Target "Compilation" ignore
    _Target "BuildRelease" BuildRelease
    _Target "BuildDebug" BuildDebug
    _Target "Analysis" ignore
    _Target "Lint" Lint
    _Target "Gendarme" Gendarme
    _Target "FxCop" FxCop
    _Target "UnitTest" UnitTest
    _Target "BuildForUnitTestDotNet" BuildForUnitTestDotNet
    _Target "UnitTestDotNet" UnitTestDotNet
    _Target "BuildForAltCover" BuildForAltCover
    _Target "UnitTestDotNetWithAltCover" UnitTestDotNetWithAltCover
    _Target "OperationalTest" ignore
    _Target "Packaging" Packaging
    _Target "PrepareFrameworkBuild" ignore
    _Target "PrepareDotNetBuild" PrepareDotNetBuild
    _Target "PrepareReadMe" PrepareReadMe
    _Target "AltCodeVsWhatGlobalIntegration" AltCodeVsWhatGlobalIntegration
    _Target "Deployment" ignore
    _Target "BulkReport" BulkReport
    _Target "All" All

    // Dependencies
    "Clean" ==> "SetVersion" ==> "Preparation"
    |> ignore

    "Preparation" ==> "BuildRelease" |> ignore

    "BuildRelease" ==> "BuildDebug" ==> "Compilation"
    |> ignore

    "BuildRelease" ==> "Lint" ==> "Analysis" |> ignore

    "Compilation" ?=> "Analysis" |> ignore

    "Compilation" ==> "FxCop"
    =?> ("Analysis", Environment.isWindows) // not supported
    |> ignore

    "Compilation" ==> "Gendarme" ==> "Analysis"
    |> ignore

    "Compilation" ?=> "UnitTest" |> ignore

    "Compilation"
    ==> "BuildForUnitTestDotNet"
    ==> "UnitTestDotNet"
    ==> "UnitTest"
    |> ignore

    "UnitTestDotNet"
    ==> "BuildForAltCover"
    ==> "UnitTestDotNetWithAltCover"
    ==> "UnitTest"
    |> ignore

    "Compilation" ?=> "OperationalTest" |> ignore

    "Compilation" ?=> "Packaging" |> ignore

    "Compilation" ==> "PrepareFrameworkBuild"
    =?> ("Packaging", Environment.isWindows) // can't ILMerge
    |> ignore

    "Compilation"
    ==> "PrepareDotNetBuild"
    ==> "Packaging"
    |> ignore

    "Compilation"
    ==> "PrepareReadMe"
    ==> "Packaging"
    ==> "Deployment"
    |> ignore

    "Analysis" ==> "All" |> ignore

    "UnitTest" ==> "All" |> ignore

    "OperationalTest" ==> "All" |> ignore

    "Packaging" ==> "AltCodeVsWhatGlobalIntegration"
    =?> ("Deployment", Environment.isWindows) // Not sure about VS for non-Windows
    |> ignore

    "Deployment" ==> "BulkReport" ==> "All" |> ignore

  let defaultTarget () =
    resetColours ()
    "All"