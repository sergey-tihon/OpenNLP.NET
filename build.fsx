#r @"paket:
source https://api.nuget.org/v3/index.json
framework: net6.0
nuget FSharp.Core
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
#endif

open System
open System.IO
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
open DotLang.CodeAnalysis.Syntax

Target.initEnvironment()

// --------------------------------------------------------------------------------------
// Provide project-specific details

let [<Literal>]root = __SOURCE_DIRECTORY__

// Read additional information from the release notes document
let release = ReleaseNotes.load "RELEASE_NOTES.md"

// --------------------------------------------------------------------------------------
// IKVM.NET compilation helpers

type TargetRuntime =
    | Net_461
    | NetCore_3_1
    override this.ToString() =
        match this with
        | Net_461 -> "net461"
        | NetCore_3_1 -> "netcoreapp3.1"
    

// Location of IKVM Compiler
let ikvmRootFolder = root </> "paket-files" </> "github.com"

let getIkmvcFolder = function
    | Net_461       -> ikvmRootFolder </> "tools-ikvmc-net461/any"
    | NetCore_3_1   -> ikvmRootFolder </> "tools-ikvmc-netcoreapp3.1/win7-x64"

let getIkvmRuntimeFolder = function
    | Net_461       -> ikvmRootFolder </> "bin-net461"
    | NetCore_3_1   -> ikvmRootFolder </> "bin-netcoreapp3.1"

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
    let command = (getIkmvcFolder framework) </> "ikvmc.exe"
    let ikvmc args =
        CreateProcess.fromRawCommandLine command args
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
            let dependencies =
                task.Dependencies
                |> Seq.collect (fun x ->
                    compile x
                    x.GetDllReferences()
                )
                |> Seq.distinct

            let sb = Text.StringBuilder()
            bprintf sb " -out:%s" task.DllFile
            if not <| String.IsNullOrEmpty(task.Version)
                then task.Version |> bprintf sb " -version:%s"
            bprintf sb " -keyfile:%s" origKeyFile
            //bprintf sb " -debug" // Not supported on Mono

            if framework = NetCore_3_1 then
                bprintf sb " -nostdlib"
                !! (sprintf "%s/refs/*.dll" (getIkmvcFolder framework))
                |> Seq.iter (fun lib -> bprintf sb " -r:%s" lib)
            
            let runtime = getIkvmRuntimeFolder framework
            bprintf sb " -runtime:%s/IKVM.Runtime.dll" runtime

            dependencies |> Seq.iter (bprintf sb " -r:%s")

            bprintf sb " \"%s\"" task.JarFile
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
     Shell.cleanDirs ["bin"; "temp"; "TestResults"]
)

// --------------------------------------------------------------------------------------
// Compile Stanford.NLP.CoreNLP and build NuGet package

let openNLPDir = root </> "paket-files/archive.apache.org/apache-opennlp-1.9.4/lib"
let frameworks = [Net_461; NetCore_3_1]

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

    for framework in frameworks do
        Trace.trace $"Compiling for {framework}"
        
        let ikvmDir  = $"bin/lib/{framework}"
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

let dotnet cmd args =
    let result = DotNet.exec id cmd args
    if not result.OK
    then failwithf "Failed: %A" result.Errors

Target.create "BuildTests" (fun _ ->
    dotnet "build" "OpenNLP.NET.sln -c Release"
)

Target.create "RunTests" (fun _ ->
    for framework in frameworks do           
        Trace.trace $"Running tests for {framework}"
        
        !! $"tests/**/bin/Release/{framework}/*.Tests.dll"
        |> Seq.iter (fun lib ->
            let logFileName = $"""${framework}-{Path.GetFileNameWithoutExtension(lib)}-TestResults.trx"""
            dotnet "test" $"{lib} -c Release --no-build --logger:\"console;verbosity=normal\" --logger:\"trx;LogFileName={logFileName}\""
        )
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
