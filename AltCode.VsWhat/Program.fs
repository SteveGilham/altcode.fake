open System

[<EntryPoint>]
let main argv =
  BlackFox.VsWhere.VsInstances.getAll ()
  |> Seq.iter (fun i -> Console.WriteLine("{0} version {1}", i.DisplayName,i.InstallationVersion)
                        Console.WriteLine("    at {0}", i.InstallationPath)
                        let status = match i.IsComplete with
                                     | None -> "Unknown"
                                     | Some true -> "Complete"
                                     | _ -> "Incomplete"
                        Console.WriteLine ("Install status: {0}\r\nPackages Installed:", status)
                        i.Packages
                        |> Seq.map (fun p -> p.Id)
                        |> Seq.sortBy (fun x -> x.ToUpperInvariant())
                        |> Seq.iter (printfn "    %A")
                        printfn "===========================\r\n")
  0 // return an integer exit code