// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

#r @"packages/FAKE/tools/FakeLib.dll"
#r "System.IO.Compression.FileSystem.dll"
#r "packages/FSharp.Management/lib/net40/FSharp.Management.dll"

open Microsoft.FSharp.Core.Printf
open Fake
open Fake.Git
open Fake.ReleaseNotesHelper
open System
open System.IO
open FSharp.Management

type root = FileSystem<  __SOURCE_DIRECTORY__  >

// --------------------------------------------------------------------------------------
// Provide project-specific details

let project = "OpenNLP.NET"
let solutionFile  = "OpenNLP.NET.sln"
// Pattern specifying assemblies to be tested using NUnit
let testAssemblies = "tests/**/bin/Release/*Tests*.dll"

// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted
let gitOwner = "sergey-tihon"
let gitHome = "https://github.com/" + gitOwner

// The name of the project on GitHub
let gitName = "OpenNLP.NET"
// The url for the raw files hosted
let gitRaw = environVarOrDefault "gitRaw" "https://raw.github.com/sergey-tihon"
// --------------------------------------------------------------------------------------

// Read additional information from the release notes document
let release = LoadReleaseNotes "RELEASE_NOTES.md"

// --------------------------------------------------------------------------------------
// IKVM.NET compilation helpers

let unZipTo toDir file =
    printfn "Unzipping file '%s' to '%s'" file toDir
    Compression.ZipFile.ExtractToDirectory(file, toDir)

let restoreFolderFromFile folder zipFile =
    if not <| Directory.Exists folder then
        zipFile |> unZipTo folder

// Location of IKVM Compiler & ildasm / ilasm
let ikvmc = root.``paket-files``.``www.frijters.net``.``ikvm-8.1.5717.0``.bin.``ikvmc.exe``
let ildasm = @"c:\Program Files (x86)\Microsoft SDKs\Windows\v7.0A\Bin\x64\ildasm.exe"
let ilasm =  @"c:\Windows\Microsoft.NET\Framework64\v2.0.50727\ilasm.exe"

type IKVMcTask(jar:string) =
    member val JarFile = jar
    member val Version = "" with get, set
    member val Dependencies = List.empty<IKVMcTask> with get, set

let timeOut = TimeSpan.FromSeconds(120.0)

let IKVMCompile workingDirectory keyFile tasks =
    let getNewFileName newExtension (fileName:string) =
        Path.GetFileName(fileName).Replace(Path.GetExtension(fileName), newExtension)
    let startProcess fileName args =
        let result =
            ExecProcess
                (fun info ->
                    info.FileName <- fileName
                    info.WorkingDirectory <- FullName workingDirectory
                    info.Arguments <- args)
                timeOut
        if result<> 0 then
            failwithf "Process '%s' failed with exit code '%d'" fileName result

    let newKeyFile =
        if (File.Exists keyFile) then
            let file = workingDirectory @@ (Path.GetFileName(keyFile))
            File.Copy(keyFile, file, true)
            Path.GetFileName(file)
        else keyFile
    let rec compile (task:IKVMcTask) =
        let getIKVMCommandLineArgs() =
            let sb = Text.StringBuilder()
            task.Dependencies |> Seq.iter
                (fun x ->
                    compile x
                    x.JarFile |> getNewFileName ".dll" |> bprintf sb " -r:%s")
            if not <| String.IsNullOrEmpty(task.Version)
                then task.Version |> bprintf sb " -version:%s"
            bprintf sb " %s -out:%s"
                (task.JarFile |> getNewFileName ".jar")
                (task.JarFile |> getNewFileName ".dll")
            sb.ToString()

        File.Copy(task.JarFile, workingDirectory @@ (Path.GetFileName(task.JarFile)) ,true)
        startProcess ikvmc (getIKVMCommandLineArgs())
        if (File.Exists(newKeyFile)) then
            let dllFile = task.JarFile |> getNewFileName ".dll"
            let ilFile  = task.JarFile |> getNewFileName ".il"
            startProcess ildasm (sprintf " /all /out=%s %s" ilFile dllFile)
            File.Delete(dllFile)
            startProcess ilasm  (sprintf " /dll /key=%s %s" (newKeyFile) ilFile)
    tasks |> Seq.iter compile

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

let createNuGetPackage workingDir nuspec =
    removeNotAssembliesFrom (workingDir+"\\lib")
    NuGet (fun p ->
        { p with
            Version = release.NugetVersion
            ReleaseNotes = String.Join(Environment.NewLine, release.Notes)
            OutputPath = "bin"
            AccessKey = getBuildParamOrDefault "nugetkey" ""
            Publish = hasBuildParam "nugetkey"
            WorkingDir = workingDir
            ToolPath = root.packages.``NuGet.CommandLine``.tools.``NuGet.exe`` })
        nuspec

let keyFile = @"nuget\OpenNLP.snk"

// --------------------------------------------------------------------------------------
// Clean build results

Target "Clean" (fun _ ->
    CleanDirs ["bin"; "temp"]
)

// --------------------------------------------------------------------------------------
// Compile Stanford.NLP.CoreNLP and build NuGet package

type openNLPDir = root.``paket-files``.``ftp.byfly.by``.``apache-opennlp-1.8.4``.lib

Target "Compile" (fun _ ->
    let ikvmDir  = @"bin\lib"
    CreateDir ikvmDir
    
    [IKVMcTask(openNLPDir.``opennlp-uima-1.8.4.jar``, Version=release.AssemblyVersion,
           Dependencies = 
            [
                IKVMcTask(openNLPDir.``opennlp-tools-1.8.4.jar``, Version=release.AssemblyVersion)
            ])
    ]
    |> IKVMCompile ikvmDir @".\OpenNLP.snk"
)

Target "NuGet" (fun _ ->
    createNuGetPackage "bin/" root.nuget.``OpenNLP.nuspec``
)

// --------------------------------------------------------------------------------------
// Build and run test projects

Target "BuildTests" (fun _ ->
    !! solutionFile
    |> MSBuildRelease "" "Rebuild"
    |> ignore
)

Target "RunTests" (fun _ ->
    !! testAssemblies
    |> NUnit (fun p ->
        { p with
            DisableShadowCopy = true
            TimeOut = TimeSpan.FromMinutes 60.
            OutputFile = "TestResults.xml" })
)

Target "Release" DoNothing

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Target "All" DoNothing

"Clean"
  ==> "Compile"
  ==> "NuGet"

"NuGet"
  ==> "BuildTests"
  ==> "RunTests"
  ==> "All"

RunTargetOrDefault "All"
