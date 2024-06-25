/**************************************************************************************************
Class:	PartOfSpeech
Author:   Mark Beljaars
Revision: 1.1
History:  13/02/11 Created
		  08/06/14 Added multi language support, additional models, concurrency locks
Notes:	Methods for determing parts of speech.

(C) Copyright 2014 Aktura Technology.  All Rights Reserved.
(For printing purposes, the maximum line length is 100 characters long)
**************************************************************************************************/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SpinEngine;
using java.io;
using javax.crypto;
using opennlp.tools.chunker;
using opennlp.tools.cmdline.parser;
using opennlp.tools.parser;
using opennlp.tools.postag;
using opennlp.tools.sentdetect;
using opennlp.tools.tokenize;
using opennlp.tools.util;
using File = System.IO.File;

namespace ChimpRewriterAPIv3.SpinEngine
{
    public static class PartOfSpeech
    {
        #region Public Constants

        /// <summary>
        ///     Part of speech unknown token id
        /// </summary>
        public const int UNKNOWN_TOKEN_ID = 0;

        /// <summary>
        ///     Part of speech unknown token id
        /// </summary>
        public const int PHRASE_TOKEN_ID = 1;

        /// <summary>
        ///     Part of speech unknown token id
        /// </summary>
        public const int MIX_TOKEN_ID = 2;

        /// <summary>
        ///     Part of speech unknown token tag
        /// </summary>
        public const string UNKNOWN_TOKEN_TAG = "UNK";

        /// <summary>
        ///     Part of speech phrase token tag
        /// </summary>
        public const string PHRASE_TOKEN_TAG = "PHS";

        /// <summary>
        ///     Part of speech phrase token tag
        /// </summary>
        public const string MIXED_TOKEN_TAG = "MIX";

        #endregion

        #region Private Fields

        private static readonly ConcurrentDictionary<string, PosModelData> _posModels =
            new ConcurrentDictionary<string, PosModelData>();

        private static string[] _sentences;
        private static string[] _tokens;
        private static string[] _tags;
        private static string _lastPara;
        private static string _lastSent;
        private static string[] _lastTokens;

        #endregion

        #region Load Models

        /// <summary>
        ///     TRUE if POS models successfully loaded.
        /// </summary>
        public static bool IsLoaded
        {
            get { return _posModels != null && _posModels.Keys.Count > 0 && _posModels.ContainsKey("en"); }
        }

        /// <summary>
        ///     Load part of speech models.
        /// </summary>
        /// <param name="modelPath">Root path containing the models</param>
        /// <param name="lang">Selected language</param>
        /// <param name="isEncrypted">TRUE if POS models and conversion data are encrypted</param>
        /// <param name="forceReload">TRUE to force reload of POS models already loaded into memory</param>
        /// <returns>TRUE if successful</returns>
        /// <remarks>
        ///     <para>
        ///         The root path must contain the POS models under a directory named after the two-letter
        ///         language code for each language culture.
        ///     </para>
        /// </remarks>
        public static bool Load(string modelPath, string lang = null, bool isEncrypted = false, bool forceReload = false)
        {
            Trace.WriteLine("Loading part of speech...");

            // remove existing models
            if (forceReload) _posModels.Clear();

            // load all models in the given directory
            if (string.IsNullOrEmpty(lang))
            {
                foreach (string dir in Directory.EnumerateDirectories(modelPath))
                {
                    var info = new DirectoryInfo(dir);
                    lang = info.Name.ToLower();
                    Trace.WriteLine("   Found " + lang + " model directory");
                    PosModelData modelData = loadPosModels(dir, isEncrypted);
                    if (modelData != null)
                    {
                        //loadOptionalPosModels(modelData, dir, isEncrypted);
                        _posModels[lang] = modelData;
                        if (LoadComplete != null) LoadComplete(lang);
                    }
                    else if (LoadFailed != null) LoadFailed(lang);
                }
            }

                // otherwise just load the given language
            else
            {
                if (_posModels.ContainsKey(lang)) return true;
                string dir = Path.Combine(modelPath, lang);
                PosModelData modelData = loadPosModels(dir, isEncrypted);
                if (modelData != null)
                {
                    _posModels[lang] = modelData;
                    //loadOptionalPosModels(_posModels[lang], dir, isEncrypted);
                    if (LoadComplete != null) LoadComplete(lang);
                }
                else
                {
                    if (lang != "en" && !_posModels.ContainsKey("en"))
                    {
                        if (LoadEnglishAttempt != null) LoadEnglishAttempt(lang);
                        return Load(modelPath, "en", isEncrypted);
                    }
                    if (LoadFailed != null) LoadFailed(lang);
                }
            }

            // nothing loaded
            if (_posModels.Count == 0)
            {
                Debug.WriteLine("!!! PartOfSpeech.Load:Could not find any models");
                return false;
            }

            Trace.WriteLine("Load complete");
            return true;
        }

        /// <summary>
        ///     Asynchronously load all POS tag models from a specific root directory.
        /// </summary>
        /// <param name="modelPath">Root path containing the models</param>
        /// <param name="lang">Selected language</param>
        /// <param name="isEncrypted">TRUE if POS models and conversion data are encrypted</param>
        /// <param name="forceReload">TRUE to force reload of POS models already loaded into memory</param>
        /// <remarks>
        ///     <para>
        ///         The root path must contain the POS models under a directory named after the two-letter
        ///         language code for each language culture.
        ///     </para>
        /// </remarks>
        public static void LoadAsynch(string modelPath, string lang = null, bool isEncrypted = false,
            bool forceReload = false)
        {
            Task.Factory.StartNew(() => Load(modelPath, lang, isEncrypted, forceReload)).ContinueWith(t =>
            {
                if (t.Exception == null) return;
                AggregateException aggException = t.Exception.Flatten();
                foreach (Exception ex in aggException.InnerExceptions)
                    Debug.WriteLine("!!! PartOfSpeech.LoadAsynch:" + ex.Message);
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        // load all POS models in a given path
        [Obfuscation(Feature = "encryptmethod", Exclude = false)]
        private static PosModelData loadPosModels(string modelPath, bool isEncrypted)
        {
            string tokFilename = Path.Combine(modelPath, "tok.bin");
            string posFilename = Path.Combine(modelPath, "pos.bin");
            string sentFilename = Path.Combine(modelPath, "sent.bin");
            string convertFilename = Path.Combine(modelPath, "convertv2.csv");

            try
            {
                var posModelData = new PosModelData();

                // load tokenizer model
                if (isEncrypted)
                {
                    using (CipherInputStream fileInputStream = Encryption.DecryptStream(tokFilename))
                    {
                        var posModel = new TokenizerModel(fileInputStream);
                        posModelData.Tokenizer = new TokenizerME(posModel);
                    }
                }
                else
                {
                    using (var fileInputStream = new FileInputStream(tokFilename))
                    {
                        var posModel = new TokenizerModel(fileInputStream);
                        posModelData.Tokenizer = new TokenizerME(posModel);
                    }
                }
                Trace.WriteLine("	  Tokenizer model loaded");

                // load tagger model
                if (isEncrypted)
                {
                    using (CipherInputStream fileInputStream = Encryption.DecryptStream(posFilename))
                    {
                        var posModel = new POSModel(fileInputStream);
                        posModelData.Tagger = new POSTaggerME(posModel, 5, 100);
                    }
                }
                else
                {
                    using (var fileInputStream = new FileInputStream(posFilename))
                    {
                        var posModel = new POSModel(fileInputStream);
                        posModelData.Tagger = new POSTaggerME(posModel, 5, 100);
                    }
                }
                Trace.WriteLine("	  Tagger model loaded");

                // load sentence tagger model
                if (isEncrypted)
                {
                    using (CipherInputStream fileInputStream = Encryption.DecryptStream(sentFilename))
                    {
                        var posModel = new SentenceModel(fileInputStream);
                        posModelData.SentenceDetector = new SentenceDetectorME(posModel);
                    }
                }
                else
                {
                    using (var fileInputStream = new FileInputStream(sentFilename))
                    {
                        var posModel = new SentenceModel(fileInputStream);
                        posModelData.SentenceDetector = new SentenceDetectorME(posModel);
                    }
                }
                Trace.WriteLine("	  Sentence model loaded");

                // load conversion table
                posModelData.ConverstionData = new ConcurrentDictionary<string, ConversionData>();
                string conversionData;
                if (isEncrypted)
                {
                    var sb = new StringBuilder();
                    using (CipherInputStream s = Encryption.DecryptStream(convertFilename))
                    {
                        int ch;
                        while ((ch = s.read()) >= 0) sb.Append((char)ch);
                        s.close();
                    }
                    conversionData = sb.ToString();
                }
                else
                {
                    conversionData = File.ReadAllText(convertFilename);
                }

                // create conversion table
                string[] lines = conversionData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 1; i < lines.Length; i++)
                {
                    string[] values = lines[i].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (values.Length != 4) continue;
                    posModelData.ConverstionData[values[0]] = new ConversionData(Convert.ToInt32(values[1]),
                        Convert.ToInt32(values[2]), values[3]);
                }
                Trace.WriteLine("	  Conversion data loaded");

                return posModelData;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("!!! PartOfSpeech.loadPosModels:" + ex.Message);
                return null;
            }
        }

        /*private static void loadOptionalPosModels(PosModelData posModelData, string modelPath, bool isEncrypted)
        {
            string chunkFilename = Path.Combine(modelPath, "chunk.bin");
            string parserFilename = Path.Combine(modelPath, "parser.bin");

            // load chunker chunker model
            try
            {
                if (File.Exists(chunkFilename))
                {
                    if (isEncrypted)
                    {
                        using (CipherInputStream fileInputStream = Encryption.DecryptStream(chunkFilename))
                        {
                            var posModel = new ChunkerModel(fileInputStream);
                            posModelData.Chunker = new ChunkerME(posModel);
                        }
                    }
                    else
                    {
                        using (var fileInputStream = new FileInputStream(chunkFilename))
                        {
                            var posModel = new ChunkerModel(fileInputStream);
                            posModelData.Chunker = new ChunkerME(posModel);
                        }
                    }
                    Trace.WriteLine("	  Chunker model loaded");
                }
            }
            catch
            {
            }


            // load parser model
            try
            {
                if (File.Exists(chunkFilename))
                {
                    if (isEncrypted)
                    {
                        using (CipherInputStream fileInputStream = Encryption.DecryptStream(parserFilename))
                        {
                            var posModel = new ParserModel(fileInputStream);
                            posModelData.Parser = ParserFactory.create(posModel);
                        }
                    }
                    else
                    {
                        using (var fileInputStream = new FileInputStream(parserFilename))
                        {
                            var posModel = new ParserModel(fileInputStream);
                            posModelData.Parser = ParserFactory.create(posModel);
                        }
                    }
                    Trace.WriteLine("	  Parser model loaded");
                }
            }
            catch
            {
            }
        }*/

        #endregion

        #region Tokenizer and Tagger

        /// <summary>
        ///     Convert given text into an array of sentences.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="para">Paragraph containing the sentences</param>
        /// <returns>An array of detected sentences</returns>
        public static string[] GetSentences(string lang, string para)
        {
            if (para.Equals(_lastPara)) return _sentences;
            lock ("PartOfSpeech")
            {
                lang = lang.ToLower();
                if (!_posModels.ContainsKey(lang)) lang = "en";
                _lastPara = para;
                _sentences = _posModels[lang].SentenceDetector.sentDetect(para);
            }
            return _sentences;
        }

        /// <summary>
        ///     Convert given sentence into an array of tokens.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="sent">Sentence containing the tokens</param>
        /// <returns>An array of detected tokens</returns>
        public static string[] GetTokens(string lang, string sent)
        {
            if (sent.Equals(_lastSent)) return _tokens;
            lock ("PartOfSpeech")
            {
                lang = lang.ToLower();
                if (!_posModels.ContainsKey(lang)) lang = "en";
                _lastSent = sent;
                _tokens = _posModels[lang].Tokenizer.tokenize(sent);

                // tokens sometimes includes the sentence terminator attached to the last tag. the following code
                // fixes this issue.
                if (_tokens != null && _tokens.Length > 0 && _tokens[_tokens.Length - 1].Length > 1 &&
                    _tokens[_tokens.Length - 1].EndsWith("."))
                {
                    var tokens = new string[_tokens.Length + 1];
                    Array.Copy(_tokens, tokens, _tokens.Length - 1);
                    tokens[_tokens.Length - 1] = _tokens[_tokens.Length - 1].Substring(0, _tokens[_tokens.Length - 1].Length - 1);
                    tokens[tokens.Length - 1] = ".";
                    _tokens = tokens;
                }
            }
            return _tokens;
        }

        /// <summary>
        ///     Convert given tokens into an array of tags.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="tokens">Array of tokens</param>
        /// <returns>An array of detected tags</returns>
        public static string[] GetTags(string lang, string[] tokens)
        {
            if (tokens.Equals(_lastTokens)) return _tags;
            lock ("PartOfSpeech")
            {
                lang = lang.ToLower();
                if (!_posModels.ContainsKey(lang)) lang = "en";
                _lastTokens = tokens;
                _tags = _posModels[lang].Tagger.tag(tokens);
            }
            return _tags;
        }

        /// <summary>
        ///     Gets the location of each token within a paragraph.
        /// </summary>
        /// <param name="sent">Source sentence</param>
        /// <param name="tokens">Array of tokens</param>
        /// <param name="offset">Offset to add to each token location</param>
        /// <returns>An array of token locations</returns>
        public static int[] GetLocations(string sent, string[] tokens, int offset = 0)
        {
            var locations = new int[tokens.Length];
            int index = 0;
            for (int i = 0; i < tokens.Length; i++)
            {
                int foundIndex = sent.IndexOf(tokens[i], index, StringComparison.Ordinal);
                locations[i] = foundIndex < 0 ? -1 : (foundIndex + offset);
                if (foundIndex >= 0) index = foundIndex;
                index += tokens[i].Length;
            }
            return locations;
        }

        /// <summary>
        ///     Convert given tokens into an array of chunks.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="tokens">Array of tokens</param>
        /// <param name="tags">Array of tags</param>
        /// <returns>An array of detected tokens</returns>
        public static Span[] GetChunks(string lang, string[] tokens, string[] tags)
        {
            lock ("PartOfSpeech")
            {
                lang = lang.ToLower();
                if (!_posModels.ContainsKey(lang)) lang = "en";
                return _posModels[lang].Chunker == null ? null : _posModels[lang].Chunker.chunkAsSpans(tokens, tags);
            }
        }

        /// <summary>
        ///     Convert given sentence into a parse tree.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="sentence">Sentence to parse</param>
        /// <returns>An array of detected tokens</returns>
        public static Parse[] Parse(string lang, string sentence)
        {
            lock ("PartOfSpeech")
            {
                lang = lang.ToLower();
                if (!_posModels.ContainsKey(lang)) lang = "en";
                return _posModels[lang].Parser == null
                    ? null
                    : ParserTool.parseLine(sentence, _posModels[lang].Parser, 1);
            }
        }

        /// <summary>
        ///     Gets the spans of each part of the sentence.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="sentence">Sentence to parse</param>
        /// <returns>An array of spans containing each part of the sentence</returns>
        public static Span[] GetSentenceParts(string lang, string sentence)
        {
            // parser is option and will revert to full sentence if not loaded
            if (_posModels[lang].Parser == null) return null;

            // create a parse object
            Parse[] parse = Parse(lang, sentence);
            if (parse == null || parse.Length == 0 || parse[0].getChildCount() == 0) return null;

            // get parts of sentence (level 0 = top; 1 = sentence; 2 = parts)
            parse = parse[0].getChildren();
            if (parse != null && parse.Length > 0 && parse[0].getChildCount() > 0) parse = parse[0].getChildren();
            if (parse == null || parse.Length == 0 || parse[0].getChildCount() == 0) return null;

            // get parts of the sentence. here we will look at the next 2 depths only and will only store different
            // parts if any of the depths includes multiple children with a single child represented as a coordinating
            // conjunction that is preceeded by and followed by a part with multiple children.
            var parts = new List<Span>();
            int start = -1;
            foreach (Parse t in parse)
            {
                string partText = sentence.Substring(t.getSpan().getStart(),
                    t.getSpan().getEnd() - t.getSpan().getStart());
                if (t.getChildCount() < 3)
                {
                    // if sub part consists of three or more words then add as part otherwise record the location so
                    // it can be merged into the next part
                    if (partText.Length - partText.Replace(" ", string.Empty).Length > 2)
                    {
                        if (start < 0) start = t.getSpan().getStart();
                        parts.Add(new Span(start, t.getSpan().getEnd()));
                        start = -1;
                    }
                    else if (start < 0) start = t.getSpan().getStart();
                    continue;
                }
                Parse[] part = t.getChildren();
                bool hasConjunction = false;
                for (int j = 1; j < part.Length - 1; j++)
                {
                    if (!string.Equals(part[j].getType(), "CC") || part[j - 1].getChildCount() <= 1 ||
                        part[j + 1].getChildCount() <= 1) continue;
                    hasConjunction = true;
                    break;
                }
                if (hasConjunction)
                {
                    foreach (Parse child in part)
                    {
                        if (start < 0) start = child.getSpan().getStart();
                        parts.Add(new Span(start, child.getSpan().getEnd()));
                        start = -1;
                    }
                }
                else
                {
                    if (start < 0) start = t.getSpan().getStart();
                    parts.Add(new Span(start, t.getSpan().getEnd()));
                }
                start = -1;
            }

            // add trailing words
            if (start >= 0)
            {
                // if 3 words or more remaining then add as own part otherwise tack on to the end of the last part
                string partText = sentence.Substring(start, sentence.Length - start);
                if (parts.Count > 0 && partText.Length - partText.Replace(" ", string.Empty).Length <= 2)
                    parts[parts.Count - 1] = new Span(parts[parts.Count - 1].getStart(), sentence.Length);
                else parts.Add(new Span(start, sentence.Length));
            }

            // return token span of each part
            if (parts.Count == 0) return null;
            var spans = new List<Span>();
            int index = 0;
            foreach (Span part in parts)
            {
                string[] tokens = GetTokens(lang, sentence.Substring(part.getStart(), part.getEnd() - part.getStart()));
                spans.Add(new Span(index, index + tokens.Length - 1));
                index += tokens.Length;
            }
            return spans.ToArray();
        }

        /// <summary>
        ///     Removes html tags and spin identifiers from the source text.
        /// </summary>
        /// <param name="source">Source text</param>
        /// <returns>Source text with html and spin identifiers removed</returns>
        /// <remarks>Use for accurate part of speach identification.</remarks>
        public static string StripHtmlTags(string source)
        {
            try
            {
                string returnValue = Regex.Replace(source, @"<[^>]*>", String.Empty);
                returnValue =
                    Regex.Replace(returnValue, @"\|[^\}]*\}", String.Empty)
                        .Replace("{", string.Empty)
                        .Replace("|", string.Empty)
                        .Replace("}", string.Empty)
                        .Replace("~", string.Empty);
                return returnValue;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("!!! Rewrite.stripSpinAndHtmlTags:Failed to strip html :" + ex.Message);
                return source;
            }
        }

        #endregion

        #region Match

        /// <summary>
        ///     Returns the POS tag match type of two POS tags.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="posId1">First POS ID to compare</param>
        /// <param name="posId2">Second POS ID to comare</param>
        /// <returns>The POS tag match type of two POS tags</returns>
        public static PosTagMatchType TagMatchType(string lang, int posId1, int posId2)
        {
            return TagMatchType(lang, FromInt(lang, posId1), FromInt(lang, posId2));
        }

        /// <summary>
        ///     Returns the POS tag match type of two POS tags.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="posTag1">First POS tag to compare</param>
        /// <param name="posTag2">Second POS tag to comare</param>
        /// <returns>The POS tag match type of two POS tags</returns>
        public static PosTagMatchType TagMatchType(string lang, string posTag1, string posTag2)
        {
            if (TagMatch(lang, posTag1, posTag2, PosTagMatchType.Full)) return PosTagMatchType.Full;
            if (TagMatch(lang, posTag1, posTag2, PosTagMatchType.Loose)) return PosTagMatchType.Loose;
            if (TagMatch(lang, posTag1, posTag2, PosTagMatchType.ExtremelyLoose)) return PosTagMatchType.ExtremelyLoose;
            return PosTagMatchType.None;
        }

        /// <summary>
        ///     Returns TRUE if two POS tags are of similar types.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="posId1">First POS ID to compare</param>
        /// <param name="posId2">Second POS ID to compare</param>
        /// <param name="type">POS tag comparison type</param>
        /// <param name="ignoreLooseGroups">Set TRUE to override loose match requirement on group IDs less than 10</param>
        /// <returns>TRUE if two POS tags are of similar types</returns>
        public static bool TagMatch(string lang, int posId1, int posId2, PosTagMatchType type,
            bool ignoreLooseGroups = false)
        {
            return TagMatch(lang, FromInt(lang, posId1), FromInt(lang, posId2), type, ignoreLooseGroups);
        }

        /// <summary>
        ///     Returns TRUE if two POS tags are of similar types.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="posTag1">First POS tag to compare</param>
        /// <param name="posTag2">Second POS tag to comare</param>
        /// <param name="type">POS tag comparison type</param>
        /// <param name="ignoreLooseGroups">Set TRUE to override loose match requirement on group IDs less than 10</param>
        /// <returns>TRUE if two POS tags are of similar types</returns>
        public static bool TagMatch(string lang, string posTag1, string posTag2, PosTagMatchType type,
            bool ignoreLooseGroups = false)
        {
            lang = lang.ToLower();
            if (!_posModels.ContainsKey(lang)) lang = "en";

            // get tag groups
            int groupTag1Id = _posModels[lang].ConverstionData.ContainsKey(posTag1)
                ? _posModels[lang].ConverstionData[posTag1].GroupId
                : 0;
            int groupTag2Id = _posModels[lang].ConverstionData.ContainsKey(posTag2)
                ? _posModels[lang].ConverstionData[posTag2].GroupId
                : 0;

            // phrase match
            if (type == PosTagMatchType.None || posTag1 == PHRASE_TOKEN_TAG && posTag1 == posTag2) return true;

            // check full match
            // don't full match on groups < 10 unless extremely loose matching
            if (!ignoreLooseGroups && type != PosTagMatchType.ExtremelyLoose && (groupTag1Id < 10 || groupTag2Id < 10))
                return false;
            // always a match if groups >= 10 and tags are the same or no match required
            if (posTag1 == posTag2) return true;
            // tags don't equal - no match if full is selected
            if (type == PosTagMatchType.Full) return false;

            // check loose match
            if (groupTag1Id == groupTag2Id) return true;

            // no match
            return false;
        }

        #endregion

        #region Converters

        /// <summary>
        ///     Convert integer number to POS Tag.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="posInt">The integer to convert</param>
        /// <returns>Corresponding POS Tag</returns>
        public static string FromInt(string lang, int posInt)
        {
            lang = lang.ToLower();
            if (!_posModels.ContainsKey(lang)) lang = "en";
            if (_posModels.ContainsKey(lang) && _posModels[lang].ConverstionData != null)
                foreach (var data in _posModels[lang].ConverstionData)
                    if (data.Value.Id == posInt) return data.Key;
            return UNKNOWN_TOKEN_TAG;
        }

        /// <summary>
        ///     Convert POS Tag to an integer number.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="posTag">The POS tag</param>
        /// <returns>An integer representing the POS tag</returns>
        public static int ToInt(string lang, string posTag)
        {
            lang = lang.ToLower();
            if (!_posModels.ContainsKey(lang)) lang = "en";
            if (_posModels[lang].ConverstionData != null && _posModels[lang].ConverstionData.ContainsKey(posTag))
                return _posModels[lang].ConverstionData[posTag].Id;
            return UNKNOWN_TOKEN_ID;
        }

        /// <summary>
        ///     Convert POS tag to an English description.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="posTag">The POS tag</param>
        /// <returns>The POS tag English description</returns>
        public static string ToString(string lang, string posTag)
        {
            lang = lang.ToLower();
            if (!_posModels.ContainsKey(lang)) lang = "en";
            if (_posModels[lang].ConverstionData != null && _posModels[lang].ConverstionData.ContainsKey(posTag))
                return _posModels[lang].ConverstionData[posTag].Description;
            return @"???";
        }

        /// <summary>
        ///     Convert POS tag to an English description.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="posInt">The POS identifier</param>
        /// <returns>The POS tag English description</returns>
        public static string ToString(string lang, int posInt)
        {
            return ToString(lang, FromInt(lang, posInt));
        }

        /// <summary>
        ///     Converts the POS tag to an icon.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="posTag">The POS tag</param>
        /// <param name="fontSize">The size of the font in pixels</param>
        /// <param name="height">The height of the icon in pixels</param>
        /// <returns>The POS tag icon</returns>
        /// <remarks>
        ///     The width is variable depending upon the size of the text and the number
        ///     of characters represented.
        /// </remarks>
        public static Image ToIcon(string lang, string posTag, int height = 16, int fontSize = 9)
        {
            return ToIcon(lang, ToInt(lang, posTag), height, fontSize);
        }

        /// <summary>
        ///     Converts the POS tag to an icon.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="posInt">The POS tag</param>
        /// <param name="fontSize">The size of the font in pixels</param>
        /// <param name="height">The height of the icon in pixels</param>
        /// <returns>The POS tag icon</returns>
        /// <remarks>
        ///     The width is variable depending upon the size of the text and the number
        ///     of characters represented.
        /// </remarks>
        public static Image ToIcon(string lang, int posInt, int height = 16, int fontSize = 9)
        {
            if (height < 1 || fontSize < 1) return null;
            lang = lang.ToLower();
            if (!_posModels.ContainsKey(lang)) lang = "en";

            // get group id
            string posTag = FromInt(lang, posInt);
            int groupId = (_posModels.ContainsKey(lang) && _posModels[lang].ConverstionData != null &&
                           _posModels[lang].ConverstionData.ContainsKey(posTag))
                ? _posModels[lang].ConverstionData[posTag].GroupId
                : UNKNOWN_TOKEN_ID;

            // determine fill color based on group id
            Color fillColor;
            switch (groupId % 33)
            {
                case 1:
                case 6:
                case 23:
                    fillColor = Color.LightCoral;
                    break;
                case 2:
                case 7:
                case 24:
                    fillColor = Color.PaleVioletRed;
                    break;
                case 3:
                case 8:
                case 25:
                    fillColor = Color.LightSlateGray;
                    break;
                case 4:
                case 9:
                case 26:
                    fillColor = Color.IndianRed;
                    break;
                case 10:
                case 16:
                case 27:
                    fillColor = Color.LightBlue;
                    break;
                case 11:
                case 17:
                case 28:
                    fillColor = Color.Orange;
                    break;
                case 12:
                case 18:
                case 29:
                    fillColor = Color.Violet;
                    break;
                case 13:
                case 19:
                case 30:
                    fillColor = Color.Yellow;
                    break;
                case 14:
                case 20:
                case 31:
                    fillColor = Color.Cyan;
                    break;
                case 15:
                case 21:
                case 32:
                    fillColor = Color.Olive;
                    break;
                default:
                    fillColor = Color.LightGreen;
                    break;
            }

            // create a rectangle
            var img = new Bitmap(1, 1);
            Graphics g = Graphics.FromImage(img);
            using (var fnt = new Font("Arial", fontSize, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                img = new Bitmap(img, new Size((int)g.MeasureString(posTag, fnt).Width + 6, height));
                g = Graphics.FromImage(img);
                g.Clear(Color.Transparent);
                var bounds = new Rectangle(0, 0, img.Width, img.Height);
                using (var pen = new Pen(Color.Gray)) drawRoundedRectangle(g, bounds, 6, pen, fillColor, Color.White);

                // add the text
                using (
                    var fmt = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    }) g.DrawString(posTag, fnt, Brushes.Black, bounds, fmt);
                g.Flush();
            }
            return img;
        }

        // draw a rectangle with rounded corners and apply a linear fill
        private static void drawRoundedRectangle(Graphics gfx, Rectangle bounds, int cornerRadius, Pen drawPen,
            Color color1,
            Color color2)
        {
            int strokeOffset = Convert.ToInt32(Math.Ceiling(drawPen.Width));
            bounds = Rectangle.Inflate(bounds, -strokeOffset, -strokeOffset);

            drawPen.EndCap = drawPen.StartCap = LineCap.Round;

            using (var gfxPath = new GraphicsPath())
            {
                gfxPath.AddArc(bounds.X, bounds.Y, cornerRadius, cornerRadius, 180, 90);
                gfxPath.AddArc(bounds.X + bounds.Width - cornerRadius, bounds.Y, cornerRadius, cornerRadius, 270, 90);
                gfxPath.AddArc(bounds.X + bounds.Width - cornerRadius, bounds.Y + bounds.Height - cornerRadius,
                    cornerRadius,
                    cornerRadius, 0, 90);
                gfxPath.AddArc(bounds.X, bounds.Y + bounds.Height - cornerRadius, cornerRadius, cornerRadius, 90, 90);
                gfxPath.CloseAllFigures();

                using (var lgb = new LinearGradientBrush(bounds, color1, color2, LinearGradientMode.ForwardDiagonal))
                    gfx.FillPath(lgb, gfxPath);
                gfx.DrawPath(drawPen, gfxPath);
            }
        }

        #endregion

        #region Is

        /// <summary>
        ///     Returns TRUE if pos tag is a noun.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="posTag">The POS tag</param>
        /// <returns>TRUE if pos tag is a noun</returns>
        public static bool IsNoun(string lang, string posTag)
        {
            return isTagGroupId(lang, posTag, 10);
        }

        /// <summary>
        ///     Returns TRUE if pos tag is a proper noun.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="posTag">The POS tag</param>
        /// <returns>TRUE if pos tag is a proper noun</returns>
        public static bool IsProperNoun(string lang, string posTag)
        {
            return (string.Equals("es", lang, StringComparison.InvariantCultureIgnoreCase) &&
                    string.Equals("NP", posTag, StringComparison.InvariantCultureIgnoreCase)) ||
                   (string.Equals("de", lang, StringComparison.InvariantCultureIgnoreCase) &&
                    string.Equals("NE", posTag, StringComparison.InvariantCultureIgnoreCase)) ||
                   (string.Equals("en", lang, StringComparison.InvariantCultureIgnoreCase) &&
                    string.Equals("NNP", posTag, StringComparison.InvariantCultureIgnoreCase));
        }

        /// <summary>
        ///     Returns TRUE if pos tag is an adjective.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="posTag">The POS tag</param>
        /// <returns>TRUE if pos tag is an adjective</returns>
        public static bool IsAdjective(string lang, string posTag)
        {
            return isTagGroupId(lang, posTag, 11);
        }

        /// <summary>
        ///     Returns TRUE if pos tag is an adverb.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="posTag">The POS tag</param>
        /// <returns>TRUE if pos tag is an adverb</returns>
        public static bool IsAdverb(string lang, string posTag)
        {
            return isTagGroupId(lang, posTag, 12);
        }

        /// <summary>
        ///     Returns TRUE if pos tag is a verb.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="posTag">The POS tag</param>
        /// <returns>TRUE if pos tag is a verb</returns>
        public static bool IsVerb(string lang, string posTag)
        {
            return isTagGroupId(lang, posTag, 13);
        }

        /// <summary>
        ///     Returns TRUE if pos tag is a pronoun.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="posTag">The POS tag</param>
        /// <returns>TRUE if pos tag is a pronoun</returns>
        public static bool IsPronoun(string lang, string posTag)
        {
            return isTagGroupId(lang, posTag, 14);
        }

        /// <summary>
        ///     Returns TRUE if pos tag is a symbol.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="posTag">The POS tag</param>
        /// <returns>TRUE if pos tag is a symbol</returns>
        public static bool IsSymbol(string lang, string posTag)
        {
            return isTagGroupId(lang, posTag, 1);
        }

        /// <summary>
        ///     Returns TRUE if pos tag is a conjunction.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="posTag">The POS tag</param>
        /// <returns>TRUE if pos tag is a conjunction</returns>
        public static bool IsConjunction(string lang, string posTag)
        {
            return isTagGroupId(lang, posTag, 2);
        }

        /// <summary>
        ///     Returns TRUE if pos tag is a conjunction.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="posTag">The POS tag</param>
        /// <returns>TRUE if pos tag is a conjunction</returns>
        public static bool IsDeterminer(string lang, string posTag)
        {
            return isTagGroupId(lang, posTag, 3);
        }

        // returns TRUE if the pos tag is in the given group 
        private static bool isTagGroupId(string lang, string posTag, int groupId)
        {
            lang = lang.ToLower();
            if (!_posModels.ContainsKey(lang)) lang = "en";
            return ((_posModels[lang].ConverstionData != null && _posModels[lang].ConverstionData.ContainsKey(posTag))
                ? _posModels[lang].ConverstionData[posTag].GroupId
                : 0) == groupId;
        }

        #endregion

        #region Events

        /// <summary>
        ///     POS Models failed to load event.
        /// </summary>
        public static event PosEventHandler LoadFailed;

        /// <summary>
        ///     POS Models successfully loaded event.
        /// </summary>
        public static event PosEventHandler LoadComplete;

        /// <summary>
        ///     POS Models failed to load and now trying English event.
        /// </summary>
        public static event PosEventHandler LoadEnglishAttempt;

        #endregion
    }

    #region POS Model Data

    // data used to convert language specific pos into groups, strings and ids
    internal class ConversionData
    {
        public ConversionData(int id, int groupId, string description)
        {
            Id = id;
            GroupId = groupId;
            Description = description;
        }

        public int Id { get; private set; }
        public int GroupId { get; private set; }
        public string Description { get; private set; }
    }

    // pos model data for a specific language
    internal class PosModelData
    {
        public ConcurrentDictionary<string, ConversionData> ConverstionData { get; internal set; }
        public POSTaggerME Tagger { get; internal set; }
        public TokenizerME Tokenizer { get; internal set; }
        public SentenceDetectorME SentenceDetector { get; internal set; }
        public ChunkerME Chunker { get; internal set; }
        public Parser Parser { get; internal set; }
    }

    #endregion

    #region PosTagMatchType enum

    /// <summary>
    ///     Determines the POS tag match type.
    /// </summary>
    public enum PosTagMatchType
    {
        /// <summary>
        ///     alays returns a positive match.
        /// </summary>
        None = 0,

        /// <summary>
        ///     any noun=any noun, any adjective=any adjective, any verb=any verb,
        ///     any adverb=any adverb, any other=any other
        /// </summary>
        ExtremelyLoose = 1,

        /// <summary>
        ///     any noun=any noun, any adjective=any adjective, any verb=any verb,
        ///     any adverb=any adverb, other=other (other must equal)
        /// </summary>
        Loose = 2,

        /// <summary>
        ///     Requires an exact match
        /// </summary>
        Full = 3
    }

    #endregion

    #region Event Handler Delegates

    /// <summary>
    ///     Generic POS event handler
    /// </summary>
    /// <param name="language">Selected language</param>
    public delegate void PosEventHandler(string language);

    #endregion
}