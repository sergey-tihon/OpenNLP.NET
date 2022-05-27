#r @"paket:
source https://nuget.org/api/v2
framework netstandard2.0
nuget FSharp.Core 5.0.0
nuget Mono.Cecil
nuget System.IO.Compression.ZipFile
nuget Graphviz.DotLanguage
nuget Fake.Core.Target
nuget Fake.Core.Process
nuget Fake.Core.ReleaseNotes
nuget Fake.IO.FileSystem
nuget Fake.DotNet.Cli
nuget Fake.DotNet.MSBuild
nuget Fake.DotNet.AssemblyInfoFile
nuget Fake.DotNet.Paket
nuget Fake.Tools.Git
nuget Fake.Api.GitHub //"

#if !FAKE
#load "./.fake/build.fsx/intellisense.fsx"
#r "netstandard" // Temp fix for https://github.com/fsharp/FAKE/issues/1985
#endif

open System
open System.IO
open System.IO.Compression
open System.Collections.Generic
open System.Text.RegularExpressions
open Microsoft.FSharp.Core
open Mono.Cecil
open Fake
open Fake.Core.TargetOperators
open Fake.Core
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.DotNet
open Fake.Tools
open DotLang.CodeAnalysis.Syntax

Target.initEnvironment()

// --------------------------------------------------------------------------------------
// Provide project-specific details

let [<Literal>]root = __SOURCE_DIRECTORY__

// Read additional information from the release notes document
let release = ReleaseNotes.load "RELEASE_NOTES.md"

// --------------------------------------------------------------------------------------
// IKVM.NET compilation helpers

let fixFileNames path =
    use file = File.Open(path, FileMode.Open, FileAccess.ReadWrite)
    use archive = new ZipArchive(file, ZipArchiveMode.Update)
    archive.Entries
    |> Seq.toList
    |> List.filter(fun x -> x.FullName.Contains(":"))
    |> List.iter (fun entry ->
        printfn "%s " entry.FullName
        let newName = entry.FullName.Replace(":","_")
        let newEntry = archive.CreateEntry(newName)
        begin
            use a = entry.Open()
            use b = newEntry.Open()
            a.CopyTo(b)
        end
        entry.Delete()
       )

let unZipTo toDir file =
    Trace.trace "Renaming files inside zip archive ..."
    fixFileNames file
    Trace.tracefn "Unzipping file '%s' to '%s'" file toDir
    Compression.ZipFile.ExtractToDirectory(file, toDir)

let restoreFolderFromFile folder zipFile =
    if not <| Directory.Exists folder then
        zipFile |> unZipTo folder

// Location of IKVM Compiler
let ikvmVersion = @"8.2.0-prerelease.392"
let ikvmRootFolder = root </> "paket-files" </> "github.com"

let ikvmcFolder_NetFramework = ikvmRootFolder </> "ikvm-" + ikvmVersion + "-tools-net461-win7-x64"
let ikvmcFolder_NetCore_Windows = ikvmRootFolder </> "ikvm-" + ikvmVersion + "-tools-net461-win7-x64"
let ikvmcFolder_NetCore_Linux = ikvmRootFolder </> "ikvm-" + ikvmVersion + "-tools-net461-linux-x64"

let ikvmcExe_NetFramework = ikvmcFolder_NetFramework </> "ikvmc.exe"
let ikvmcExe_NetCore_Windows = ikvmcFolder_NetCore_Windows </> "ikvmc.exe"
let ikvmcExe_NetCore_Linux = ikvmcFolder_NetCore_Linux </> "ikvmc.exe"

type IKVMcTask(jar:string, version:string) =
    member __.JarFile = jar
    member __.Version = version
    member __.DllFile = Path.ChangeExtension(Path.GetFileName(jar), ".dll")
    member val Dependencies = List.empty<IKVMcTask> with get, set
    member this.GetDllReferences() =
        seq {
            for t in this.Dependencies do
                yield! t.GetDllReferences()
            yield this.DllFile
        }

let timeOut = TimeSpan.FromSeconds(120.0)

let IKVMCompile framework workingDirectory keyFile tasks =
    let origKeyFile =
        if (File.Exists keyFile) then
            Path.GetFileName(keyFile)
        else keyFile
    let (|StartsWith|_|) needle (haystack : string) = if haystack.StartsWith(string needle) then Some() else None
    let command =
        (match framework with
        | StartsWith "net4" () ->
            (if Environment.isWindows
            then ikvmcExe_NetFramework
            else "mono")
        | _ ->
            (if Environment.isWindows
            then ikvmcExe_NetCore_Windows
            else ikvmcExe_NetCore_Linux))
    let commandArgs args =
        (match framework with
        | StartsWith "net4" () ->
            (if not <| Environment.isWindows
            then ikvmcExe_NetFramework + " " + args
            else args)
        | _ -> args)
         
    let ikvmc args =
        let cArgs = commandArgs args
        CreateProcess.fromRawCommandLine command cArgs
        |> CreateProcess.withWorkingDirectory (DirectoryInfo(workingDirectory).FullName)
        |> CreateProcess.withTimeout timeOut
        |> CreateProcess.ensureExitCode
        |> Proc.run
        |> ignore
    let newKeyFile =
        if (File.Exists keyFile) then
            let file = workingDirectory @@ (Path.GetFileName(keyFile))
            File.Copy(keyFile, file, true)
            Path.GetFileName(file)
        else keyFile

    let bprintf = Microsoft.FSharp.Core.Printf.bprintf
    let cache = HashSet<_>()
    let rec compile (task:IKVMcTask) =
        let getIKVMCommandLineArgs() =
            let sb = Text.StringBuilder()
            task.Dependencies
            |> Seq.collect (fun x ->
                compile x
                x.GetDllReferences()
            )
            |> Seq.distinct
            |> Seq.iter (bprintf sb " -r:%s")
            if not <| String.IsNullOrEmpty(task.Version)
                then task.Version |> bprintf sb " -version:%s"
            bprintf sb " %s -out:%s" task.JarFile task.DllFile
            bprintf sb " -keyfile:%s" origKeyFile
            //bprintf sb " -debug" // Not supported on Mono
            sb.ToString()

        if cache.Contains task.JarFile
        then
            Trace.tracefn "Task '%s' already compiled" task.JarFile
        else
            //File.Copy(task.JarFile, workingDirectory @@ (Path.GetFileName(task.JarFile)) ,true)
            ikvmc <| getIKVMCommandLineArgs()
            if (File.Exists(newKeyFile)) then
                let key = FileInfo(newKeyFile).FullName
                          |> File.ReadAllBytes
                ModuleDefinition
                    .ReadModule(task.DllFile, ReaderParameters(InMemory=true))
                    .Write(task.DllFile, WriterParameters(StrongNameKeyBlob=key))
            cache.Add(task.JarFile) |> ignore
    tasks |> Seq.iter compile

/// Infer dependencies between *.jar files
let buildJDepsGraph dotFile jarFolder (jars:string seq)=
    "-filter:package -dotoutput dot " + String.Join(" ", jars)
    |> CreateProcess.fromRawCommandLine "jdeps" 
    |> CreateProcess.withWorkingDirectory jarFolder
    |> CreateProcess.ensureExitCode
    |> Proc.run
    |> ignore

    jarFolder </> "dot/summary.dot"
    |> Shell.copyFile dotFile

let loadJDepsGraph dotFile (jars:Set<string>) = 
    let parser = dotFile |> File.ReadAllText |> Parser

    parser.Parse().Graphs.[0].Statements
    |> Seq.cast<EdgeStatementSyntax>
    |> Seq.map (fun x-> 
        (x.Left :?> NodeStatementSyntax).Identifier.IdentifierToken.StringValue, 
        (x.Right :?> NodeStatementSyntax).Identifier.IdentifierToken.StringValue)
    |> Seq.filter (fun (x,y) -> jars.Contains x && jars.Contains y)
    |> Seq.groupBy fst
    |> Seq.map (fun (x,xs)-> x, xs |> Seq.map snd |> Seq.toList)
    |> Map.ofSeq

/// Build IKVm task with proper dependencies order (topological sort)
let createCompilationGraph dotFile jarFolder (jars:Set<string>) =
    let deps = loadJDepsGraph dotFile jars
    let cache = Dictionary<_,_>()

    let pattern = Regex("-(?<version>[0-9.]*).jar$", RegexOptions.Compiled)
    let getVersion str =
        let m = pattern.Match(str)
        if m.Success 
        then m.Groups.["version"].Value |> Some
        else None

    let rec getTask name =
        match cache.TryGetValue name with
        | true, task -> task
        | _ -> 
            let deps = 
                Map.tryFind name deps
                |> Option.defaultValue []
                |> List.map getTask
            let ver = getVersion name 
                      |> Option.defaultValue release.AssemblyVersion
            let task = IKVMcTask(jarFolder </> name, ver, Dependencies = deps)
            cache.Add(name, task)
            task

    seq {
        for jar in jars do
            if not <| cache.ContainsKey jar
            then yield getTask jar
    } |> Seq.toList

let copyPackages fromDir toDir =
    if (not <| Directory.Exists(toDir))
        then Directory.CreateDirectory(toDir) |> ignore
    Directory.GetFiles(fromDir)
    |> Seq.filter (fun x -> Path.GetExtension(x) = ".nupkg")
    |> Seq.iter   (fun x -> File.Copy(x, Path.Combine(toDir, Path.GetFileName(x)), true))

let removeNotAssembliesFrom dir =
    !! (dir + @"/*.*")
      -- (dir + @"/*.dll")
      |> Seq.iter (System.IO.File.Delete)

let createNuGetPackage template =
    Paket.pack(fun p ->
        { p with
            ToolType = Fake.DotNet.ToolType.CreateLocalTool()
            TemplateFile = template
            OutputPath = "bin"
            Version = release.NugetVersion
            ReleaseNotes = String.toLines release.Notes})

let keyFile = @"./nuget/OpenNLP.snk"

// --------------------------------------------------------------------------------------
// Clean build results

Target.create "Clean" (fun _ ->
     Shell.cleanDirs ["bin"; "temp"]
)

// --------------------------------------------------------------------------------------
// Compile Stanford.NLP.CoreNLP and build NuGet package

let openNLPDir = root </> "paket-files/archive.apache.org/apache-opennlp-1.9.1/lib"

Target.create "Compile" (fun _ ->
    // Get *.jar file for compilation
    let jars =
        Directory.GetFiles(openNLPDir, "*.jar")
        |> Array.map (Path.GetFileName)
        |> Set.ofArray
    if (jars.IsEmpty)
    then failwith "Found 0 *.jar files"

    let dotFile = "nuget/OpenNLP.dot"
    if not <| File.Exists dotFile then
        buildJDepsGraph dotFile openNLPDir jars

    let framework  = @"net461"
    let ikvmDir  = @"bin/lib/" + framework
    Shell.mkdir ikvmDir

    createCompilationGraph dotFile openNLPDir jars
    |> IKVMCompile framework ikvmDir keyFile

    let framework  = @"netcoreapp3.1"
    let ikvmDir  = @"bin/lib/" + framework
    Shell.mkdir ikvmDir

    createCompilationGraph dotFile openNLPDir jars
    |> IKVMCompile framework ikvmDir keyFile
)

Target.create "NuGet" (fun _ ->
    root </> "nuget/OpenNLP.template"
    |> createNuGetPackage
)

// --------------------------------------------------------------------------------------
// Build and run test projects

Target.create "BuildTests" (fun _ ->
    let result = DotNet.exec id "build" "OpenNLP.NET.sln -c Release"
    if not result.OK
    then failwithf "Build failed: %A" result.Errors
)

Target.create "RunTests" (fun _ ->
    Trace.trace $"Running tests for netcoreapp3.1"

    let doubleQuote = '"'
    let libs = !! "tests/**/bin/Release/netcoreapp3.1/*.Tests.dll"
    for lib in libs do
        DotNet.exec id "test" $"{lib} -c Release --no-build --logger:{doubleQuote}console;verbosity=normal{doubleQuote} --logger:{doubleQuote}trx;LogFileName=TestResults.trx{doubleQuote}"
        |> ignore

    Trace.trace $"Running tests for net461"

    let libs = !! "tests/**/bin/Release/net461/*.Tests.dll"
    let args = String.Join(" ", libs)
    let runner = "packages/NUnit.ConsoleRunner/tools/nunit3-console.exe"

    (if Environment.isWindows
     then CreateProcess.fromRawCommandLine runner args
     else CreateProcess.fromRawCommandLine "mono" (runner + " " + args))
    |> CreateProcess.ensureExitCode
    |> Proc.run
    |> ignore
)

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Target.create "All" ignore
Target.create "Release" ignore

"Clean"
  ==> "Compile"
  ==> "NuGet"
  ==> "BuildTests"
  ==> "RunTests"
  ==> "All"

Target.runOrDefault "All"
