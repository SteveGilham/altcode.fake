namespace AltCode.Fake

module Setup =

  open System
  open System.Reflection
  open System.Runtime.InteropServices

  open Fake.DotNet
  open Fake.DotNet.NuGet.Restore
  open Fake.IO

  [<assembly: CLSCompliant(true)>]
  [<assembly: ComVisible(false)>]
  [<assembly: AssemblyVersionAttribute("1.0.0.0")>]
  [<assembly: AssemblyFileVersionAttribute("1.0.0.0")>]
  ()

  // Really bootstrap
  let dotnetPath =
    "dotnet"
    |> Fake.Core.ProcessUtils.tryFindFileOnPath

  let dotnetOptions (o: DotNet.Options) =
    match dotnetPath with
    | Some f -> { o with DotNetCliPath = f }
    | None -> o

  DotNet.restore
    (fun o ->
      { o with
          Packages = [ "./packages" ]
          Common = dotnetOptions o.Common })
    "./Build/NuGet.csproj"

  [<EntryPoint>]
  let private main _ = 0