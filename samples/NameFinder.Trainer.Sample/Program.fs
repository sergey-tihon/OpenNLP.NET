open java.nio.charset
open java.io
open opennlp.tools.util
open opennlp.tools.namefind

// The training data should contain at least 15000 sentences to create a model which performs well. 
let train (inputFile: string) =
    let factory = {
        new InputStreamFactory with
            member _.createInputStream() =
                new FileInputStream(inputFile) :> InputStream
    }
    let lineStream = new PlainTextByLineStream(factory, StandardCharsets.UTF_8)
    use sampleStream = new NameSampleDataStream(lineStream)
    let nameFinderFactory = TokenNameFinderFactory()
    
    let trainingParameters = TrainingParameters()
    //trainingParameters.put(TrainingParameters.ITERATIONS_PARAM, "5")
    //trainingParameters.put(TrainingParameters.CUTOFF_PARAM, "200")
    
    NameFinderME.train("en", "person", sampleStream, trainingParameters, nameFinderFactory)

// From https://github.com/mccraigmccraig/opennlp/tree/master/src/test/resources/opennlp/tools/namefind
let trainDataFileName = "AnnotatedSentencesWithTypes.txt"
let model = train trainDataFileName

let nameFinder = NameFinderME(model)
let sentence = [| "My"; "name"; "is"; "Sergey"; "Tihon"; "." |]
let nameSpans = nameFinder.find(sentence);

printfn $"Input: %A{sentence}"
printfn $"Name spans: %A{nameSpans}"