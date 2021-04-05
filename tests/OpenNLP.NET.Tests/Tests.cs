using System.IO;
using System.Linq;

using NUnit.Framework;

namespace OpenNLP.NET.Tests
{
    public class Tests
    {
        private const string DownloadsFolders = @"../../../../../paket-files/opennlp.sourceforge.net";
        private string GetModel(string fileName)
        {
            var asmFolder = Path.GetDirectoryName(GetType().Assembly.Location);
            var filePath = Path.GetFullPath(Path.Combine(asmFolder, DownloadsFolders, fileName));
            if (!File.Exists(filePath))
                throw new FileNotFoundException(filePath);
            return filePath;
        }

        [Test]
        public void SentenceDetection()
        {
            using var modelIn = new java.io.FileInputStream(GetModel("en-sent.bin"));

            var model = new opennlp.tools.sentdetect.SentenceModel(modelIn);
            var sentenceDetector = new opennlp.tools.sentdetect.SentenceDetectorME(model);

            var sentences = sentenceDetector.sentDetect("  First sentence. Second sentence. ");
            System.Console.WriteLine(string.Join(";", sentences));

            Assert.AreEqual(2, sentences.Length);
        }

        [Test]
        public void Tokenization()
        {
            using var modelIn = new java.io.FileInputStream(GetModel("en-token.bin"));

            var model = new opennlp.tools.tokenize.TokenizerModel(modelIn);
            var tokenizer = new opennlp.tools.tokenize.TokenizerME(model);

            var tokens = tokenizer.tokenize("An input sample sentence.");
            System.Console.WriteLine(string.Join(";", tokens));

            Assert.AreEqual(5, tokens.Length);
        }

        [Test]
        public void NameFinder()
        {
            using var modelIn = new java.io.FileInputStream(GetModel("en-ner-person.bin"));

            var model = new opennlp.tools.namefind.TokenNameFinderModel(modelIn);
            var nameFinder = new opennlp.tools.namefind.NameFinderME(model);

            var sentence = new[] {"Pierre", "Vinken", "is", "61", "years", "old", "."};
            var nameSpans = nameFinder.find(sentence);
            System.Console.WriteLine(string.Join(";", nameSpans.Select(x=>x.toString())));

            Assert.AreEqual(1, nameSpans.Length);
        }

        [Test]
        public void PartOfSpeechTagger()
        {
            using var modelIn = new java.io.FileInputStream(GetModel("en-pos-maxent.bin"));

            var model = new opennlp.tools.postag.POSModel(modelIn);
            var tagger = new opennlp.tools.postag.POSTaggerME(model);

            var sentence = new[]
            {
                "Most", "large", "cities", "in", "the", "US", "had",
                "morning", "and", "afternoon", "newspapers", "."
            };
            var tags = tagger.tag(sentence);
            System.Console.WriteLine(string.Join(";", tags));
            Assert.AreEqual(12, tags.Length);

            var probs = tagger.probs();
            System.Console.WriteLine(string.Join(";", probs));
            Assert.AreEqual(12, probs.Length);
        }

        [Test]
        public void Chunker()
        {
            using var modelIn = new java.io.FileInputStream(GetModel("en-chunker.bin"));

            var model = new opennlp.tools.chunker.ChunkerModel(modelIn);
            var chunker = new opennlp.tools.chunker.ChunkerME(model);

            var sent = new[]
            {
                "Rockwell", "International", "Corp.", "'s", "Tulsa", "unit", "said", "it", "signed",
                "a", "tentative", "agreement", "extending", "its", "contract", "with", "Boeing", "Co.",
                "to", "provide", "structural", "parts", "for", "Boeing", "'s", "747", "jetliners", "."
            };
            var pos = new[]
            {
                "NNP", "NNP", "NNP", "POS", "NNP", "NN", "VBD", "PRP", "VBD", "DT", "JJ", "NN", "VBG", "PRP$",
                "NN", "IN", "NNP", "NNP", "TO", "VB", "JJ", "NNS", "IN", "NNP", "POS", "CD", "NNS", "."
            };

            var tags = chunker.chunk(sent, pos);
            System.Console.WriteLine(string.Join(";", tags));
            Assert.AreEqual(28, tags.Length);

            var probs = chunker.probs();
            System.Console.WriteLine(string.Join(";", probs));
            Assert.AreEqual(28, probs.Length);
        }

        [Test]
        public void Parser()
        {
            using var modelIn = new java.io.FileInputStream(GetModel("en-parser-chunking.bin"));

            var model = new opennlp.tools.parser.ParserModel(modelIn);
            var parser = opennlp.tools.parser.ParserFactory.create(model);

            var sentence = "The quick brown fox jumps over the lazy dog .";
            var topParses = opennlp.tools.cmdline.parser.ParserTool.parseLine(sentence, parser, 1);
            System.Console.WriteLine(string.Join(";", topParses.Select(x=>x.toString())));
            Assert.AreEqual(1, topParses.Length);

            var tree = topParses[0];
            Assert.IsNull(tree.getLabel());
        }
    }
}
