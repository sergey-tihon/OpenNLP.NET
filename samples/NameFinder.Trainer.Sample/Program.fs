
// The training data should contain at least 15000 sentences to create a model which performs well. 
let train (inputFile: string) =
    let factory = {
        new InputStreamFactory with
            member _.createInputStream() =
                new FileInputStream(inputFile) :> InputStream
    }
    let lineStream = PlainTextByLineStream(factory, StandardCharsets.UTF_8)
    use sampleStream = NameSampleDataStream(lineStream)
    let nameFinderFactory = TokenNameFinderFactory()
    
    let trainingParameters = TrainingParameters()
    trainingParameters.put(TrainingParameters.ITERATIONS_PARAM, "5")
    trainingParameters.put(TrainingParameters.CUTOFF_PARAM, "200")
    
    NameFinderME.train("en", "person", sampleStream, traningParameters, nameFinderFactory)
