namespace Placeholder

module Dummy =

  [<EntryPoint>]
  let main argv =
    printfn "%A" argv

    if argv |> Seq.last |> System.IO.File.Exists then
      0 // return an integer exit code
    else
      1