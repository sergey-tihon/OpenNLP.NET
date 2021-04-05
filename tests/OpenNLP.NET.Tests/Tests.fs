module OpenNLP.NET.Tests

open NUnit.Framework
open FsUnit
open java.io

let [<Literal>] downloads = __SOURCE_DIRECTORY__ + @"/../../paket-files/opennlp.sourceforge.net"
type Models = FSharp.Management.FileSystem<downloads>

open opennlp.tools.sentdetect
let [<Test>] ``Sentence Detection``() =
    use modelIn = new FileInputStream(Models.``en-sent.bin``)
    let model = SentenceModel(modelIn)
    let sentenceDetector = SentenceDetectorME(model)
    let sentences = sentenceDetector.sentDetect("  First sentence. Second sentence. ")
    printfn "%A" sentences
    sentences |> should haveLength 2

open opennlp.tools.tokenize
let [<Test>]``Tokenization``() =
    use modelIn = new FileInputStream(Models.``en-token.bin``)
    let model = TokenizerModel(modelIn)
    let tokenizer = TokenizerME(model)
    let tokens = tokenizer.tokenize("An input sample sentence.")
    printfn "%A" tokens
    tokens |> should haveLength 5

open opennlp.tools.namefind
let [<Test>]``Name Finder``() =
    use modelIn = new FileInputStream(Models.``en-ner-person.bin``)
    let model = TokenNameFinderModel(modelIn)
    let nameFinder = new NameFinderME(model)
    let sentence = [|"Pierre"; "Vinken"; "is"; "61"; "years"; "old"; "."|]
    let nameSpans = nameFinder.find(sentence)
    printfn "%A" nameSpans
    nameSpans |> should haveLength 1

open opennlp.tools.postag
let [<Test>]``Part-of-Speech Tagger``()=
    use modelIn = new FileInputStream(Models.``en-pos-maxent.bin``)
    let model = POSModel(modelIn)
    let tagger = new POSTaggerME(model)
    let sent = [|"Most"; "large"; "cities"; "in"; "the"; "US"; "had";
                 "morning"; "and"; "afternoon"; "newspapers"; "."|]
    let tags = tagger.tag(sent)
    printfn "%A" tags
    tags |> should haveLength 12
    let probs = tagger.probs()
    printfn "%A" probs
    probs |> should haveLength 12

open opennlp.tools.chunker
let [<Test>]``Chunker``() =
    use modelIn = new FileInputStream(Models.``en-chunker.bin``)
    let model = ChunkerModel(modelIn)
    let chunker = ChunkerME(model)
    let sent =
        [| "Rockwell"; "International"; "Corp."; "'s"; "Tulsa"; "unit"; "said"; "it"; "signed";
           "a"; "tentative"; "agreement"; "extending"; "its"; "contract"; "with"; "Boeing"; "Co.";
           "to"; "provide"; "structural"; "parts"; "for"; "Boeing"; "'s"; "747";  "jetliners"; "." |]
    let pos =
        [| "NNP"; "NNP"; "NNP"; "POS"; "NNP"; "NN"; "VBD"; "PRP"; "VBD"; "DT"; "JJ"; "NN"; "VBG"; "PRP$";
           "NN"; "IN"; "NNP"; "NNP"; "TO"; "VB"; "JJ"; "NNS"; "IN"; "NNP"; "POS"; "CD"; "NNS"; "." |]
    let tag = chunker.chunk(sent, pos)
    printfn "%A" tag
    tag |> should haveLength 28
    let probs = chunker.probs()
    printfn "%A" probs
    probs |> should haveLength 28

open opennlp.tools.parser
open opennlp.tools.cmdline.parser
let [<Test>]``Parser``() =
    use modelIn  = new FileInputStream(Models.``en-parser-chunking.bin``)
    let model = new ParserModel(modelIn)
    let parser = ParserFactory.create(model)
    let sentence = "The quick brown fox jumps over the lazy dog ."
    let topParses = ParserTool.parseLine(sentence, parser, 1)
    printfn "%A" topParses
    topParses |> should haveLength 1
    let tree = topParses.[0]
    tree.getLabel() |> should be Null