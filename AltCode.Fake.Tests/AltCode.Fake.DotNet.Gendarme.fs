module AltCode.Fake.DotNet.Gendarme

open System
open System.IO
open System.Reflection
open AltCode.Fake.DotNet
open Fake.IO
open Expecto
open Expecto.ExpectoFsCheck

let serializingObject = Object()

let testCases =
  [ testCase "Test that default arguments are processed as expected" <| fun _ ->
      let p = Gendarme.Params.Create()
      let args = Gendarme.getArguments p
      Expect.isTrue (p.Console) "A field should have non-default value for a bool"
      Expect.equal args
        [ "--console"; "--severity"; "medium+"; "--confidence"; "normal+" ]
        "The defaults should be simple"
    testCase "Test that string arguments are processed as expected" <| fun _ ->
      let config = Guid.NewGuid().ToString()
      let rules = Guid.NewGuid().ToString()
      let log = Guid.NewGuid().ToString()

      let ignore =
        [ Guid.NewGuid().ToString()
          Guid.NewGuid().ToString() ]

      let targets =
        [ Guid.NewGuid().ToString()
          Guid.NewGuid().ToString() ]

      let p =
        { Gendarme.Params.Create() with Configuration = config
                                        RuleSet = rules
                                        Log = log
                                        Ignore = ignore
                                        Targets = targets }

      let args = Gendarme.getArguments p
      Expect.equal args ([ "--config"
                           config
                           "--set"
                           rules
                           "--html"
                           log
                           "--ignore"
                           ignore |> Seq.head
                           "--ignore"
                           ignore |> Seq.last
                           "--console"
                           "--severity"
                           "medium+"
                           "--confidence"
                           "normal+" ]
                         @ targets) "The defaults should be simple"

    testCase "Test that log-kind arguments are processed as expected" <| fun _ ->
      [ (Guid.NewGuid().ToString(), Gendarme.LogKind.Text, "--log")
        (Guid.NewGuid().ToString(), Gendarme.LogKind.Html, "--html")
        (Guid.NewGuid().ToString(), Gendarme.LogKind.Xml, "--xml") ]
      |> Seq.iter
           (fun (log, kind, name) ->
           let p =
             { Gendarme.Params.Create() with Log = log
                                             LogKind = kind }

           let args = Gendarme.getArguments p
           Expect.equal args
             [ name; log; "--console"; "--severity"; "medium+";
               "--confidence"; "normal+" ] ("The log kind should be " + name))

    testCase "Test that limit arguments are processed as expected" <| fun _ ->
      let p = { Gendarme.Params.Create() with Limit = uint8 (23 + DateTime.Now.Second) }
      let args = Gendarme.getArguments p
      Expect.equal args [ "--limit"
                          (sprintf "%A" p.Limit).Replace("uy", String.Empty)
                          "--console"
                          "--severity"
                          "medium+"
                          "--confidence"
                          "normal+" ] (sprintf "The limit should be %A" p.Limit)

    testCase "Test that console may be switched off" <| fun _ ->
      let p = { Gendarme.Params.Create() with Console = false }
      let args = Gendarme.getArguments p
      Expect.equal args
        [ "--severity"; "medium+"; "--confidence"; "normal+" ]
        "The console should be gone"

    testCase "Test that output may be hushed" <| fun _ ->
      let p = { Gendarme.Params.Create() with Quiet = true }
      let args = Gendarme.getArguments p
      Expect.equal args
        [ "--console"; "--quiet"; "--severity"; "medium+"; "--confidence";
          "normal+" ] "The output should be quieted"

    testProperty "Test that verbosity may be set" <| fun (x : uint8) ->
      let p = { Gendarme.Params.Create() with Verbosity = x }
      let args = Gendarme.getArguments p
      Expect.equal args
        ([ "--console"; "--severity"; "medium+"; "--confidence"; "normal+" ]
         @ (Seq.initInfinite (fun _ -> "--v")
            |> Seq.take (int x)
            |> Seq.toList)) "The verbosity should be set"

    testCase "Test that severity may be set" <| fun _ ->
      [ Gendarme.Severity.All, "all"
        Gendarme.Severity.Audit Gendarme.Grade.Plus, "audit+"
        Gendarme.Severity.Audit Gendarme.Grade.Neutral, "audit"
        Gendarme.Severity.Audit Gendarme.Grade.Minus, "audit-"
        Gendarme.Severity.Low Gendarme.Grade.Plus, "low+"
        Gendarme.Severity.Low Gendarme.Grade.Neutral, "low"
        Gendarme.Severity.Low Gendarme.Grade.Minus, "low-"
        Gendarme.Severity.Medium Gendarme.Grade.Plus, "medium+"
        Gendarme.Severity.Medium Gendarme.Grade.Neutral, "medium"
        Gendarme.Severity.Medium Gendarme.Grade.Minus, "medium-"
        Gendarme.Severity.High Gendarme.Grade.Plus, "high+"
        Gendarme.Severity.High Gendarme.Grade.Neutral, "high"
        Gendarme.Severity.High Gendarme.Grade.Minus, "high-"
        Gendarme.Severity.Critical Gendarme.Grade.Plus, "critical+"
        Gendarme.Severity.Critical Gendarme.Grade.Neutral, "critical"
        Gendarme.Severity.Critical Gendarme.Grade.Minus, "critical-" ]
      |> List.iter
           (fun (s, m) ->
           let p = { Gendarme.Params.Create() with Severity = s }
           let args = Gendarme.getArguments p
           Expect.equal args
             [ "--console"; "--severity"; m; "--confidence"; "normal+" ]
             ("The severity should be " + m))

    testCase "Test that confidence may be set" <| fun _ ->
      [ Gendarme.Confidence.All, "all"
        Gendarme.Confidence.Low Gendarme.Grade.Plus, "low+"
        Gendarme.Confidence.Low Gendarme.Grade.Neutral, "low"
        Gendarme.Confidence.Low Gendarme.Grade.Minus, "low-"
        Gendarme.Confidence.Normal Gendarme.Grade.Plus, "normal+"
        Gendarme.Confidence.Normal Gendarme.Grade.Neutral, "normal"
        Gendarme.Confidence.Normal Gendarme.Grade.Minus, "normal-"
        Gendarme.Confidence.High Gendarme.Grade.Plus, "high+"
        Gendarme.Confidence.High Gendarme.Grade.Neutral, "high"
        Gendarme.Confidence.High Gendarme.Grade.Minus, "high-"
        Gendarme.Confidence.Total Gendarme.Grade.Plus, "total+"
        Gendarme.Confidence.Total Gendarme.Grade.Neutral, "total"
        Gendarme.Confidence.Total Gendarme.Grade.Minus, "total-" ]
      |> List.iter
           (fun (c, m) ->
           let p = { Gendarme.Params.Create() with Confidence = c }
           let args = Gendarme.getArguments p
           Expect.equal args
             [ "--console"; "--severity"; "medium+"; "--confidence"; m ]
             ("The severity should be " + m))

    testCase "Test that command arguments are processed as expected" <| fun _ ->
      let here = Assembly.GetExecutingAssembly().Location |> Path.GetDirectoryName
      let fake = Path.Combine(here, "gendarme.exe")
      let p = Gendarme.Params.Create()
      let args = Gendarme.getArguments p
      let proc = Gendarme.createProcess args p
      let info =
        proc.GetType()
            .GetProperty("WorkingDirectory",
                         BindingFlags.Instance ||| BindingFlags.NonPublic)
      let w = info.GetValue(proc) :?> string option
      Expect.equal proc.CommandLine
        (fake + " --console --severity medium+ --confidence normal+")
        "The defaults should be simple"
      Expect.equal w None "Default working directory should be empty"

    testCase "Test that tool path can be set" <| fun _ ->
      let fake = Guid.NewGuid().ToString()
      let p = { Gendarme.Params.Create() with ToolPath = fake }
      let args = Gendarme.getArguments p
      let proc = Gendarme.createProcess args p
      Expect.equal proc.CommandLine
        (fake + " --console --severity medium+ --confidence normal+")
        "The toolpath should match"

    testCase "Test that working directory is processed as expected" <| fun _ ->
      let fake = Guid.NewGuid().ToString()
      let p = { Gendarme.Params.Create() with WorkingDirectory = fake }
      let args = Gendarme.getArguments p
      let proc = Gendarme.createProcess args p
      let info =
        proc.GetType()
            .GetProperty("WorkingDirectory",
                         BindingFlags.Instance ||| BindingFlags.NonPublic)
      let w = info.GetValue(proc) :?> string option
      Expect.equal w (Some fake) "Default working directory should be as given"

    testCase "A good run should proceed as expected" <| fun _ ->
      let here = Assembly.GetExecutingAssembly().Location |> Path.GetDirectoryName
      let fake = Path.Combine(here, "AltCode.Nuget.Placeholder.exe")

      let args =
        { Gendarme.Params.Create() with ToolPath = fake
                                        Targets = [ fake ] }
      Expect.equal (Gendarme.run args) () "Should be silent"

    testCase "A bad run should proceed as expected" <| fun _ ->
      let here = Assembly.GetExecutingAssembly().Location |> Path.GetDirectoryName
      let fake = Path.Combine(here, "AltCode.Nuget.Placeholder.exe")

      let args =
        { Gendarme.Params.Create() with ToolPath = fake
                                        Targets = [ fake + ".nonesuch" ] }
      Expect.throwsC (fun () -> Gendarme.run args)
        (fun ex ->
        Expect.equal ex.Message
          ("Gendarme --console --severity medium+ --confidence normal+ " + fake
           + ".nonesuch failed.") "Message should reflect inputs")

    testCase "Test that null arguments are processed as expected" <| fun _ ->
      let p =
        { Gendarme.Params.Create() with Configuration = null
                                        RuleSet = null
                                        Log = null
                                        Ignore = null
                                        Targets = null }

      let args = Gendarme.getArguments p
      Expect.equal args
        [ "--console"; "--severity"; "medium+"; "--confidence"; "normal+" ]
        "The defaults should be simple" ]

[<Tests>]
let tests = testList "Fake.DotNet.Gendarme.Tests" testCases