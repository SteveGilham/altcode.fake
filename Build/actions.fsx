open System
open System.IO
open System.Reflection
open System.Xml
open System.Xml.Linq
open Fake.Core
open Fake.DotNet
open Fake.IO.FileSystemOperators
open Fake.IO
open Fake.IO.Globbing.Operators
open HeyRed.MarkdownSharp
open NUnit.Framework
open YamlDotNet.RepresentationModel

module Actions =
  let Clean() =
    let rec Clean1 depth =
      try
        (DirectoryInfo ".").GetDirectories("*", SearchOption.AllDirectories)
        |> Seq.filter (fun x -> x.Name.StartsWith "_" || x.Name = "bin" || x.Name = "obj")
        |> Seq.filter (fun n ->
             match n.Name with
             | "obj" ->
               Path.Combine(n.FullName, "dotnet-fake.fsproj.nuget.g.props")
               |> File.Exists
               |> not
             | _ -> true)
        |> Seq.filter (fun n ->
             "packages"
             |> Path.GetFullPath
             |> n.FullName.StartsWith
             |> not)
        |> Seq.map (fun x -> x.FullName)
        |> Seq.distinct
        // arrange so leaves get deleted first, avoiding "does not exist" warnings
        |> Seq.groupBy (fun x ->
             x
             |> Seq.filter (fun c -> c = '\\' || c = '/')
             |> Seq.length)
        |> Seq.map (fun (n, x) -> (n, x |> Seq.sort))
        |> Seq.sortBy (fun p -> -1 * (fst p))
        |> Seq.map snd
        |> Seq.concat
        |> Seq.iter (fun n ->
             printfn "Deleting %s" n
             Directory.Delete(n, true))
        !!(@"./*Tests/*.tests.core.fsproj")
        |> Seq.map (fun f -> (Path.GetDirectoryName f) @@ "coverage.opencover.xml")
        |> Seq.iter File.Delete
        let temp = Environment.environVar "TEMP"
        if not <| String.IsNullOrWhiteSpace temp then
          Directory.GetFiles(temp, "*.tmp.dll.mdb") |> Seq.iter File.Delete
      with
      | :? System.IO.IOException as x -> Clean' (x :> Exception) depth
      | :? System.UnauthorizedAccessException as x -> Clean' (x :> Exception) depth

    and Clean' x depth =
      printfn "looping after %A" x
      System.Threading.Thread.Sleep(500)
      if depth < 10 then Clean1(depth + 1)
      else Assert.Fail "Could not clean all the files"

    Clean1 0

  let template = """namespace AltCode
open System.Reflection
open System.Runtime.CompilerServices

[<assembly: AssemblyDescription("Part of a cross-platform coverage gathering and processing tool set for .net/.net core and Mono")>]

#if DEBUG
[<assembly: AssemblyConfiguration("Debug {0}")>]
#if NETSTANDARD2_0
[<assembly: InternalsVisibleTo("AltCode.Fake.Tests")>]
#else
[<assembly: InternalsVisibleTo("AltCode.Fake.Tests, PublicKey={1}")>]
#endif
#else
[<assembly: AssemblyConfiguration("Release {0}")>]
#endif
do ()"""
  let prefix =
    [| 0x00uy; 0x24uy; 0x00uy; 0x00uy; 0x04uy; 0x80uy; 0x00uy; 0x00uy; 0x94uy; 0x00uy;
       0x00uy; 0x00uy |]

  let GetPublicKey(stream : Stream) =
    // see https://social.msdn.microsoft.com/Forums/vstudio/en-US/d9ef264e-1a74-4f48-b93f-3e2c7902f660/determine-contents-of-a-strong-name-key-file-snk?forum=netfxbcl
    // for the exact format; this is a stripped down hack
    let buffer = Array.create 148 0uy
    let size = stream.Read(buffer, 0, buffer.Length)
    Assert.That(size, Is.EqualTo buffer.Length)
    Assert.That(buffer.[0], Is.EqualTo 7uy) // private key blob
    buffer.[0] <- 6uy // public key blob
    Assert.That(buffer.[11], Is.EqualTo 0x32uy) // RSA2 magic number
    buffer.[11] <- 0x31uy // RSA1 magic number
    Array.append prefix buffer

  let InternalsVisibleTo version =
    let stream =
      new System.IO.FileStream("./Build/Infrastructure.snk", System.IO.FileMode.Open,
                               System.IO.FileAccess.Read)

    //let pair = StrongNameKeyPair(stream)
    //let key = BitConverter.ToString pair.PublicKey
    let key =
      stream
      |> GetPublicKey
      |> BitConverter.ToString

    let file =
      String.Format
        (System.Globalization.CultureInfo.InvariantCulture, template, version,
         key.Replace("-", String.Empty))
    let path = "_Generated/VisibleToTest.fs"

    // Update the file only if it would change
    let old =
      if File.Exists(path) then File.ReadAllText(path)
      else String.Empty
    if not (old.Equals(file)) then File.WriteAllText(path, file)

  let GetVersionFromYaml() =
    use yaml =
      new FileStream("appveyor.yml", FileMode.Open, FileAccess.ReadWrite, FileShare.None,
                     4096, FileOptions.SequentialScan)
    use yreader = new StreamReader(yaml)
    let ystream = new YamlStream()
    ystream.Load(yreader)
    let mapping = ystream.Documents.[0].RootNode :?> YamlMappingNode
    string mapping.Children.[YamlScalarNode("version")]

  let LocalVersion appveyor (version : string) =
    let now = DateTimeOffset.UtcNow
    let epoch = DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan(int64 0))
    let diff = now.Subtract(epoch)
    let fraction = diff.Subtract(TimeSpan.FromDays(float diff.Days))
    let revision = ((int fraction.TotalSeconds) / 3)
    let majmin = String.Join(".", version.Split('.') |> Seq.take 2)

    let result =
      if String.IsNullOrWhiteSpace appveyor then
        sprintf "%s.%d.%d" majmin diff.Days revision
      else appveyor
    printfn "Build version : %s" version
    (result, majmin, now.Year)

  let HandleResults (msg : string) (result : Fake.Core.ProcessResult) =
    String.Join(Environment.NewLine, result.Messages) |> printfn "%s"
    let save = (Console.ForegroundColor, Console.BackgroundColor)
    match result.Errors |> Seq.toList with
    | [] -> ()
    | errors ->
      try
        Console.ForegroundColor <- ConsoleColor.Black
        Console.BackgroundColor <- ConsoleColor.White
        String.Join(Environment.NewLine, errors) |> printfn "ERR : %s"
      finally
        Console.ForegroundColor <- fst save
        Console.BackgroundColor <- snd save
    Assert.That(result.ExitCode, Is.EqualTo 0, msg)

  let AssertResult (msg : string) (result : Fake.Core.ProcessResult<'a>) =
    Assert.That(result.ExitCode, Is.EqualTo 0, msg)

  let Run (file, dir, args) msg =
    CreateProcess.fromRawCommand file args
    |> CreateProcess.withWorkingDirectory dir
    |> CreateProcess.withFramework
    |> Proc.run
    |> (AssertResult msg)

  let RunDotnet (o : DotNet.Options -> DotNet.Options) cmd args msg =
    DotNet.exec o cmd args |> (HandleResults msg)

  let PrepareReadMe packingCopyright =
    let readme = Path.getFullName "README.md"
    let document = File.ReadAllText readme
    let markdown = Markdown()
    let docHtml = """<?xml version="1.0"  encoding="utf-8"?>
<!DOCTYPE html>
<html lang="en">
<head>
<title>AltCover README</title>
<style>
body, html {
color: #000; background-color: #eee;
font-family: 'Segoe UI', 'Open Sans', Calibri, verdana, helvetica, arial, sans-serif;
position: absolute; top: 0px; width: 50em;margin: 1em; padding:0;
}
a {color: #444; text-decoration: none; font-weight: bold;}
a:hover {color: #ecc;}
</style>
</head>
<body>
"""               + markdown.Transform document + """
<footer><p style="text-align: center">""" + packingCopyright + """</p>
</footer>
</body>
</html>
"""
    let xmlform = XDocument.Parse docHtml
    let body = xmlform.Descendants(XName.Get "body")
    let eliminate = [ "Continuous Integration"; "Building"; "Thanks to" ]
    let keep = ref true

    let kill =
      body.Elements()
      |> Seq.map (fun x ->
           match x.Name.LocalName with
           | "h2" ->
             keep
             := (List.tryFind (fun e -> e = String.Concat(x.Nodes())) eliminate)
                |> Option.isNone
           | "footer" -> keep := true
           | _ -> ()
           if !keep then None
           else Some x)
      |> Seq.toList
    kill
    |> Seq.iter (fun q ->
         match q with
         | Some x -> x.Remove()
         | _ -> ())
    let packable = Path.getFullName "./_Binaries/README.html"
    xmlform.Save packable