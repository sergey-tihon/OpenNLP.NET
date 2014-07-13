// include Fake lib
#load @".\Core.fsx"
open Fake 
open Fake.AssemblyInfoFile
open Fake.IKVM.Helpers

// Assembly / NuGet package properties
let version = "1.5.3.0"
let authors =
    ["Jorn Kottmann"
     "Benson Margulies"
     "Grant Ingersoll"
     "Isabel Drost"
     "James Kosin"
     "Jason Baldridge"
     "Thomas Morton"
     "William Silva"
     "Aliaksandr Autayeu"
     "Boris Galitsky"
     "Mark Giaconia"
     "Rodrigo Agerri"
     "Tommaso Teofili"
     "Vinh Khuc"]

let projectName = "OpenNLP"
let projectDescription = "The Apache OpenNLP library is a machine learning based toolkit for the processing of natural language text. It supports the most common NLP tasks, such as tokenization, sentence segmentation, part-of-speech tagging, named entity extraction, chunking, parsing, and coreference resolution. These tasks are usually required to build more advanced text processing services. OpenNLP also included maximum entropy and perceptron based machine learning."

// Targets

// Restore models
Target "RestoreModels" (fun _ ->
    ["http://opennlp.sourceforge.net/models-1.5/en-token.bin"
     "http://opennlp.sourceforge.net/models-1.5/en-sent.bin"
     "http://opennlp.sourceforge.net/models-1.5/en-pos-maxent.bin"
     "http://opennlp.sourceforge.net/models-1.5/en-pos-perceptron.bin"
     "http://opennlp.sourceforge.net/models-1.5/en-ner-date.bin"
     "http://opennlp.sourceforge.net/models-1.5/en-ner-location.bin"
     "http://opennlp.sourceforge.net/models-1.5/en-ner-money.bin"
     "http://opennlp.sourceforge.net/models-1.5/en-ner-organization.bin"
     "http://opennlp.sourceforge.net/models-1.5/en-ner-percentage.bin"
     "http://opennlp.sourceforge.net/models-1.5/en-ner-person.bin"
     "http://opennlp.sourceforge.net/models-1.5/en-ner-time.bin"
     "http://opennlp.sourceforge.net/models-1.5/en-chunker.bin"
     "http://opennlp.sourceforge.net/models-1.5/en-parser-chunking.bin"]
    |> List.iter (restoreFile >> ignore)
)

// Run IKVM compiler
Target "RunIKVMCompiler" (fun _ ->
    restoreFolderFromUrl
        @".\temp\apache-opennlp-1.5.3"
        "http://ftp.byfly.by/pub/apache.org/opennlp/opennlp-1.5.3/apache-opennlp-1.5.3-bin.zip"
    [IKVMcTask(@"temp\apache-opennlp-1.5.3\lib\opennlp-uima-1.5.3.jar", Version=version,
           Dependencies = [IKVMcTask(@"temp\apache-opennlp-1.5.3\lib\opennlp-tools-1.5.3.jar", Version=version,
                                Dependencies =[ IKVMcTask(@"temp\apache-opennlp-1.5.3\lib\opennlp-maxent-3.0.3.jar", Version="3.0.3")
                                                IKVMcTask(@"temp\apache-opennlp-1.5.3\lib\jwnl-1.3.3.jar", Version="1.3.3")])])]
    |> IKVMCompile ikvmDir @".\OpenNLP.snk"
)

// Create NuGet package
Target "CreateNuGet" (fun _ ->
    copyFilesToNugetFolder()

    "OpenNLP.nuspec"
      |> NuGet (fun p ->
            {p with
                Project = projectName
                Authors = authors
                Version = version
                Description = projectDescription
                NoPackageAnalysis = true
                ToolPath = ".\..\.nuget\NuGet.exe"
                WorkingDir = nugetDir
                OutputPath = nugetDir })
)

// Dependencies
"CleanIKVM"
  ==> "RestoreModels"
  ==> "RunIKVMCompiler"
  ==> "CleanNuGet"
  ==> "CreateNuGet"
  ==> "Default"

// start build
Run "Default"