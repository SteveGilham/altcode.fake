/// Contains a task which allows static analysis with Gendarme.
[<RequireQualifiedAccess>]
module AltCode.Fake.DotNet.Gendarme

open System
open Fake.Core
open Fake.IO
open Fake.IO.Globbing
open System.Globalization
open System.Diagnostics.CodeAnalysis

/// Nudge severity or confidence levels
[<NoComparison>]
type Grade =
  | Plus
  | Neutral
  | Minus

/// Filter defects for the specified severity levels.
/// Default is 'medium+'
[<NoComparison>]
type Severity =
  | All
  | Audit of Grade
  | Low of Grade
  | Medium of Grade
  | High of Grade
  | Critical of Grade

/// Filter defects for the specified confidence levels.
/// Default is 'normal+'
[<NoComparison>]
type Confidence =
  | All
  | Low of Grade
  | Normal of Grade
  | High of Grade
  | Total of Grade

/// Option type to configure ILMerge's target output.
[<NoComparison>]
type LogKind =
  | Text
  | Xml
  | Html

/// Parameter type for Gendarme
[<NoComparison; NoEquality>]
type Params =
  { /// Path to gendarme.exe
    ToolPath : string
    /// Define the tool through FAKE 5.18 ToolType -- if set, overrides
    ToolType : Fake.DotNet.ToolType
    /// Working Directory
    WorkingDirectory : string
    /// Specify the rule sets and rule settings. Default is 'rules.xml'.
    Configuration : string
    /// Specify a rule set from configfile. Default is 'default'.
    RuleSet : string
    /// Save the report to the specified file.
    Log : string
    /// Report type.
    LogKind : LogKind
    /// Do not report defects listed in the specified file.
    Ignore : string seq
    /// Stop reporting after this many defects are found.
    Limit : uint8
    /// True -> Show defects on the console even if Log is specified
    Console : bool
    /// True -> Used to disable progress and other information which is normally written to stdout.
    Quiet : bool
    /// When present and > 0 additional progress information is written to stdout
    Verbosity : uint8
    /// Specify the assemblies to verify.
    Targets : string seq
    /// Filter defects for the specified severity levels.
    /// Default is 'medium+'
    Severity : Severity
    /// Filter defects for the specified confidence levels.
    /// Default is 'normal+'
    Confidence : Confidence
    /// Fail the build if a defect is reported
    /// Default is true
    FailBuildOnDefect : bool }
  /// ILMerge default parameters. Tries to automatically locate ilmerge.exe in a subfolder.
  static member Create() =
    { ToolPath =
        match (ProcessUtils.tryFindLocalTool "PATH" "gendarme.exe" [ "." ]) with
        | Some path -> path
        | _ -> "gendarme"
      ToolType = Fake.DotNet.ToolType.CreateFullFramework()
      WorkingDirectory = String.Empty
      Configuration = String.Empty
      RuleSet = String.Empty
      Log = String.Empty
      LogKind = LogKind.Html
      Ignore = Seq.empty
      Limit = 0uy
      Console = true
      Quiet = false
      /// When present and > 0 additional progress information is written to stdout
      Verbosity = 0uy
      /// Specify the assemblies to verify.
      Targets = Seq.empty
      /// Filter defects for the specified severity levels.
      /// Default is 'medium+'
      Severity = Severity.Medium Grade.Plus
      /// Filter defects for the specified confidence levels.
      /// Default is 'normal+'
      Confidence = Confidence.Normal Grade.Plus
      FailBuildOnDefect = true }

/// Builds the arguments for the Gendarme task
/// [omit]
[<System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1308",
                                                  Justification =
                                                    "Lower-casing is safe here")>]
let internal composeCommandLine parameters =
  let Item a x =
    if x |> String.IsNullOrWhiteSpace then [] else [ a; x ]

  let ItemList a x =
    if x |> isNull then
      []
    else
      x
      |> Seq.collect (fun i -> [ a; i ])
      |> Seq.toList

  let Flag a predicate =
    if predicate then [ a ] else []

  [ Item "--config" parameters.Configuration
    Item "--set" parameters.RuleSet
    (match parameters.LogKind with
     | Text -> Item "--log"
     | Xml -> Item "--xml"
     | _ -> Item "--html") parameters.Log
    ItemList "--ignore" parameters.Ignore
    (if parameters.Limit > 0uy
     then Item "--limit" <| parameters.Limit.ToString(CultureInfo.InvariantCulture)
     else [])
    Flag "--console" parameters.Console
    Flag "--quiet" parameters.Quiet

    Item "--severity"
    <| (sprintf "%A" parameters.Severity).ToLowerInvariant().Replace(" plus", "+")
      .Replace(" minus", "-").Replace(" neutral", String.Empty)

    Item "--confidence"
    <| (sprintf "%A" parameters.Confidence).ToLowerInvariant().Replace(" plus", "+")
      .Replace(" minus", "-").Replace(" neutral", String.Empty)
    (if parameters.Verbosity > 0uy then
      { 1 .. int parameters.Verbosity }
      |> Seq.map (fun _ -> "--v")
      |> Seq.toList
     else
       [])

    ((ItemList String.Empty parameters.Targets)
     |> List.filter (String.isNullOrWhiteSpace >> not)) ]
  |> List.concat

let internal withWorkingDirectory parameters c =
  c
  |> if String.IsNullOrWhiteSpace parameters.WorkingDirectory
     then id
     else CreateProcess.withWorkingDirectory parameters.WorkingDirectory

let internal createProcess args parameters =
  CreateProcess.fromCommand (RawCommand(parameters.ToolPath, args |> Arguments.OfArgs))
  |> CreateProcess.withToolType
       (parameters.ToolType.WithDefaultToolCommandName "gendarme")
  |> withWorkingDirectory parameters

/// Uses Gendarme to analyse .NET assemblies.
/// ## Parameters
///  - `parameters` - A Gendarme.Params value with your required settings.
let run parameters =
  use __ = Trace.traceTask "Gendarme" String.Empty
  let args = (composeCommandLine parameters)

  let command =
    createProcess args parameters
    |> if parameters.FailBuildOnDefect then CreateProcess.ensureExitCode else id
    |> fun command ->
      Trace.trace command.CommandLine
      command

  command
  |> Proc.run
  |> ignore
  __.MarkSuccess()