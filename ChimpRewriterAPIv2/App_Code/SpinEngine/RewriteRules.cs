/**************************************************************************************************
Class:	RewriteRules
Author:   Mark Beljaars
Revision: 1.0
History:  27/12/13 Created
Notes:	Methods for rewriting sentences based on supplied rules. A rewrite rule consists of a 
		  list of rules and a list of replacement words. A rule consists of a list of commands.
		  Replacement words are used to swap the tense of a word.

(C) Copyright 2014 Aktura Technology.  All Rights Reserved.
**************************************************************************************************/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SpinEngine;
using opennlp.tools.util;

namespace ChimpRewriterAPIv3.SpinEngine
{
    public class RewriteRules
    {
        // list of rewrite rules for each language
        private static readonly ConcurrentDictionary<string, RewriteRules> _rewriteRules =
            new ConcurrentDictionary<string, RewriteRules>();

        private readonly List<Rule> _rules; // list of rules for current language

        private readonly ConcurrentDictionary<string, WordList> _words;
        // list of word replacements for current language

        /// <summary>
        ///     ctor
        /// </summary>
        public RewriteRules()
        {
            _rules = new List<Rule>();
            _words = new ConcurrentDictionary<string, WordList>();
        }

        /// <summary>
        ///     Load the given rewrite rules into memory.
        /// </summary>
        /// <param name="rulePath">Root path containing the rules</param>
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
        /// <returns>TRUE if successful</returns>
        public static bool Load(string rulePath, string lang = null, bool isEncrypted = false, bool forceReload = false)
        {
            // get rules path
            Trace.WriteLine("Loading rules...");

            // remove existing rules
            if (forceReload) _rewriteRules.Clear();

            // load rules for each language found
            try
            {
                // load all rules in the given directory
                if (string.IsNullOrEmpty(lang))
                {
                    foreach (string dir in Directory.EnumerateDirectories(rulePath))
                    {
                        try
                        {
                            var info = new DirectoryInfo(dir);
                            lang = info.Name.ToLower();
                            Trace.WriteLine("   Found " + lang + " rules directory");
                            string lines = isEncrypted
                                ? Encryption.Decrypt(Path.Combine(dir, "rules.dat"))
                                : File.ReadAllText(Path.Combine(dir, "rules.dat"));
                            RewriteRules rules = FromString(lines);
                            _rewriteRules[lang] = rules;
                            if (LoadComplete != null) LoadComplete(lang);
                            Trace.WriteLine("	  Rules loaded");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("!!! " + ex.Message);
                            if (LoadFailed != null) LoadFailed(lang);
                        }
                    }
                }

                    // otherwise just load the given language
                else
                {
                    if (_rewriteRules.ContainsKey(lang)) return true;
                    string lines = isEncrypted
                        ? Encryption.Decrypt(Path.Combine(rulePath, lang, "rules.dat"))
                        : File.ReadAllText(Path.Combine(rulePath, lang, "rules.dat"));
                    RewriteRules rules = FromString(lines);
                    _rewriteRules[lang] = rules;
                    if (LoadComplete != null) LoadComplete(lang);
                }


                if (_rewriteRules.Count == 0)
                {
                    Debug.WriteLine("!!! Could not find any rules");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("!!! " + ex.Message);
                if (LoadFailed != null) LoadFailed(lang);
                return false;
            }

            Trace.WriteLine("Load complete");
            return true;
        }

        /// <summary>
        ///     Load the given rewrite rules into memory.
        /// </summary>
        /// <param name="rulePath">Root path containing the rules</param>
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
        public static void LoadAsynch(string rulePath, string lang = null, bool isEncrypted = false,
            bool forceReload = false)
        {
            Task.Factory.StartNew(() => Load(rulePath, lang, isEncrypted, forceReload)).ContinueWith(t =>
            {
                if (t.Exception == null) return;
                AggregateException aggException = t.Exception.Flatten();
                foreach (Exception ex in aggException.InnerExceptions)
                    Debug.WriteLine("!!! RewriteRules.LoadAsynch:" + ex.Message);
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>
        ///     Rewrite all sentences in a given paragraph using the rules engine.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="paragraph">Source paragraph to rewrite</param>
        /// <param name="returnSpin">TRUE to return spin instead of spun text</param>
        /// <returns>Rewritten version of the paragraph</returns>
        public static string RewriteParagraph(string lang, string paragraph, bool returnSpin)
        {
            try
            {
                // return source paragraph if language does not contain a rules set
                if (!_rewriteRules.ContainsKey(lang)) return paragraph;

                // store protected ranges
                MatchCollection ranges = Regex.Matches(paragraph, @"##.([^##]+)##", RegexOptions.IgnoreCase);

                // copy the paragraph
                var newParagraph = new StringBuilder(paragraph);

                // split paragraph into sentences
                string[] sentences = PartOfSpeech.GetSentences(lang, paragraph);
                int offset = 0;
                foreach (string sentence in sentences)
                {
                    // record the start of the sentence within the paragraph
                    offset = newParagraph.ToString().IndexOf(sentence, offset, StringComparison.Ordinal);

                    // determine if the sentence overlaps a protected area
                    bool isProtected = false;
                    foreach (Match mathch in ranges)
                    {
                        if (mathch.Index + mathch.Length <= offset || mathch.Index > offset + sentence.Length) continue;
                        isProtected = true;
                        break;
                    }

                    // skip the protected sentence
                    if (isProtected)
                    {
                        offset += sentence.Length;
                        continue;
                    }

                    // rewrite the sentence
                    string newSentence = RewriteSentence(lang, sentence, returnSpin);

                    // replace the sentence within the paragraph
                    if (newSentence != sentence)
                    {
                        newParagraph.Remove(offset, sentence.Length);
                        newParagraph.Insert(offset, newSentence);
                    }

                    // move the offset to the start of the next sentence
                    offset += newSentence.Length;
                }

                // return the rewritten paragraph
                return newParagraph.ToString();
            }
            catch
            {
                return paragraph;
            }
        }

        /// <summary>
        ///     Rewrite a sentences using the rules engine.
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="sentence">Source sentence to rewrite</param>
        /// <returns>Rewritten version of the sentence</returns>
        /// <param name="returnSpin">TRUE to return spin instead of spun text</param>
        public static string RewriteSentence(string lang, string sentence, bool returnSpin)
        {
            try
            {
                // return source sentence if language does not contain a rules set
                if (!_rewriteRules.ContainsKey(lang)) return sentence;

                // strip html tags from the source sentence
                string strippedSentence = Regex.Replace(sentence, @"<[^>]*>", String.Empty);
                if (strippedSentence.Length > 0 && !char.IsPunctuation(strippedSentence[strippedSentence.Length - 1]))
                    strippedSentence += ".";

                // split each sentence into words, tags and locations. remove sentence terminator from the tokens list if one is found.
                string[] tokens = PartOfSpeech.GetTokens(lang, strippedSentence);
                int[] locations = PartOfSpeech.GetLocations(sentence, tokens);
                string[] tags = PartOfSpeech.GetTags(lang, tokens);
                Span[] parts = PartOfSpeech.GetSentenceParts(lang, strippedSentence);

                // return source sentence if no tokens found
                if (tokens == null || tokens.Length == 0) return sentence;

                // set span to entire sentence if parser not loaded or something went wrong with the part parser
                if (parts == null || parts.Length == 0 || parts[parts.Length - 1].getEnd() != tokens.Length - 1)
                    parts = new[] { new Span(0, tokens.Length - 1) };

#if VERBOSE && DEBUG
				{
					var sb = new StringBuilder();
					for (int i = 0; i < tokens.Length; i++) sb.Append(string.Format("{0}:{1} ", i, tokens[i]));
					Debug.WriteLine(sb);
					sb.Clear();
					for (int i = 0; i < tags.Length; i++) sb.Append(string.Format("{0}:{1} ", i, tags[i]));
					Debug.WriteLine(sb);
				}
#endif

                var matchingRules = new List<RuleRun>();
                foreach (Span part in parts)
                {
                    // determine start and end index reference
                    int start = part.getStart();
                    int end = Math.Min(locations.Length - 1, part.getEnd());
                    int len = end - start + 1;

                    // remove trailing punctuation
                    if (char.IsPunctuation(tokens[end][0])) len--;

                    // derive token and tag subsets for the active part
                    var subtokens = new string[len];
                    var subtags = new string[len];
                    Array.Copy(tokens, start, subtokens, 0, len);
                    Array.Copy(tags, start, subtags, 0, len);

                    // create a list of matching rules for all words in the current sentence
                    var partMatchRules = new List<RuleRun>();
                    foreach (Rule rule in _rewriteRules[lang]._rules)
                    {
                        try
                        {
                            // create list of rules for each word run. this function will return matching rules for each element of the sentence. 
                            // the matching algorithm starts at the first word, checks all rules and then moves to the next word and checks again. 
                            // a list of rules is then created for each run of words.
                            var matches = new List<RuleRun>();
                            for (int i = 0; i < subtags.Length - 1; i++)
                            {
                                var ruleRun = new RuleRun(rule);
                                if (
                                    !rule.SourceRules[0].GetMatch(lang, i, rule, 0, subtokens, subtags,
                                        _rewriteRules[lang]._words, ruleRun)) continue;

                                // only add new rules by disgarding matching rules with smaller wildcard start and end runs
                                if (matches.Count == 0 ||
                                    matches[matches.Count - 1].StartIndexTo != ruleRun.StartIndexTo ||
                                    matches[matches.Count - 1].EndIndexFrom != ruleRun.EndIndexFrom)
                                    matches.Add(ruleRun);
                            }
                            if (matches.Count != 0)
                            {
                                partMatchRules.AddRange(matches);
                                break;
                            }
                        }
                        catch
                        {
                        }
                    }

                    // select best matched rule
                    RuleRun match = partMatchRules.Count > 0 ? partMatchRules[0] : null;
                    if (match != null)
                    {
                        // add part offset
                        match.Offset(start);

                        // remove end of sentence
                        bool hasSentenceTerminator = tags[tags.Length - 1] == ".";
                        if (hasSentenceTerminator && match.EndIndexFrom == tokens.Length - 1) match.EndIndexFrom--;
                        if (hasSentenceTerminator && match.EndIndexTo == tokens.Length - 1) match.EndIndexTo--;

                        // add to rewrite rule list
                        matchingRules.Add(match);
                    }

#if VERBOSE && DEBUG
					Debug.WriteLine("Part: " +
									sentence.Substring(locations[start],
													   (locations[Math.Min(locations.Length - 1, end + 1)] > 0
															? locations[Math.Min(locations.Length - 1, end + 1)] : sentence.Length) -
													   locations[start]));
					if (match != null) Debug.WriteLine(match);
					else Debug.WriteLine(@"No matching rules found.");
#endif
                }

#if VERBOSE && DEBUG
				Debug.WriteLine("=");
				foreach (var rule in matchingRules) Debug.WriteLine(rule);
				Debug.WriteLine("=");
#endif

                // rewrite the sentence
                return rewriteSentence(lang, matchingRules, sentence, returnSpin, tokens, tags, locations,
                    _rewriteRules[lang]._words);
            }
            catch
            {
                return sentence;
            }
        }

        // rewrites a sentence using the supplied rules
        private static string rewriteSentence(string lang, IEnumerable<RuleRun> ruleRuns, string sentence,
            bool returnSpin, string[] tokens,
            string[] tags, int[] locations, ConcurrentDictionary<string, WordList> words)
        {
            var sb = new StringBuilder(sentence);

            // apply rules in reverse order in case rules modify the length of the sentence
            foreach (RuleRun ruleRun in ruleRuns.Reverse())
            {
                // rewrite the part of the sentence
                string rewrite = rewritePart(lang, ruleRun.Rule, sentence, ruleRun.StartIndexFrom, ruleRun.EndIndexTo,
                    tokens, tags, locations, words);
                if (string.IsNullOrEmpty(rewrite)) continue;

                // don't insert if the rewrite is the same as the original
                string source = sb.ToString(locations[ruleRun.StartIndexFrom],
                    locations[ruleRun.EndIndexTo] - locations[ruleRun.StartIndexFrom] +
                    tokens[ruleRun.EndIndexTo].Length);
                if (string.Equals(source.Replace(" ", string.Empty), rewrite.Replace(" ", string.Empty),
                    StringComparison.InvariantCultureIgnoreCase)) continue;

#if VERBOSE && DEBUG
				Debug.WriteLine("{0} = {1}", source, rewrite);
#endif

                // remove trailing punctuation
                while ((source.Length == 0 || char.IsLetterOrDigit(source[source.Length - 1])) && rewrite.Length > 0 &&
                       !char.IsLetterOrDigit(rewrite[rewrite.Length - 1])) rewrite = rewrite.Substring(0, rewrite.Length - 1);


                // add spin syntax if spin required
                if (returnSpin)
                {
                    // split source and rewrite into words so that the duplicate words can be detected at the start and end of the rewrite
                    string[] sourceTokens = PartOfSpeech.GetTokens(lang, source);
                    string[] rewriteTokens = PartOfSpeech.GetTokens(lang, rewrite);
                    int[] sourceLocations = PartOfSpeech.GetLocations(source, sourceTokens);

                    // strip of common starting and ending text
                    int startOffset = 0;
                    int endOffset = 0;
                    while (startOffset < sourceTokens.Length && startOffset < rewriteTokens.Length &&
                           sourceTokens[startOffset] == rewriteTokens[startOffset]) startOffset++;
                    while (endOffset < sourceTokens.Length && endOffset < sourceTokens.Length - startOffset &&
                           rewriteTokens.Length - endOffset > 0 &&
                           sourceTokens[sourceTokens.Length - endOffset - 1] ==
                           rewriteTokens[rewriteTokens.Length - endOffset - 1]) endOffset++;
                    string start = startOffset == 0 ? string.Empty : source.Substring(0, sourceLocations[startOffset]);
                    string end = endOffset == 0
                        ? string.Empty
                        : source.Substring(sourceLocations[sourceLocations.Length - endOffset]);
                    if (end.Length > 0 && end.Length < source.Length &&
                        char.IsWhiteSpace(source[source.Length - end.Length - 1]))
                        end = source[source.Length - end.Length - 1] + end;

                    // add common text and only wrap unique spin
                    rewrite = string.Format("{0}{{{1}|{2}}}{3}", start,
                        source.Length - start.Length - end.Length > 0
                            ? source.Substring(start.Length, source.Length - start.Length - end.Length)
                            : string.Empty,
                        rewrite.Length - start.Length - end.Length > 0
                            ? rewrite.Substring(start.Length, rewrite.Length - start.Length - end.Length)
                            : string.Empty, end);
                }

                // insert the part into the sentence
                sb.Remove(locations[ruleRun.StartIndexFrom], source.Length);
                sb.Insert(locations[ruleRun.StartIndexFrom], rewrite);
            }

#if VERBOSE && DEBUG
			Debug.WriteLine("=");
			Debug.WriteLine(sb);
			Debug.WriteLine(string.Empty);
#endif

            return sb.ToString();
        }

        // rewrites part of a sentence using a supplied rule
        private static string rewritePart(string lang, Rule rule, string sentence, int start, int end, string[] tokens,
            string[] tags, int[] locations, ConcurrentDictionary<string, WordList> words)
        {
            // create a list of words to insert for each command. also store the tag of the first word so we can keep case if it is a proper noun.
            var wordRuns = new List<Tuple<string, string>>();
            int index = start;
            for (int i = 0; i < rule.SourceRules.Count; i++)
            {
                if (index > end) break;

                // list command
                var list = rule.SourceRules[i] as Command.List;
                if (list != null)
                {
                    // retrieve the index within the source list and return the matching index in the result list 
                    for (int j = 0; j < words[list.Name][list.SourceIndex].Length; j++)
                    {
                        if (string.Equals(words[list.Name][list.SourceIndex][j], tokens[index],
                            StringComparison.InvariantCultureIgnoreCase))
                        {
                            wordRuns.Add(new Tuple<string, string>(words[list.Name][list.ResultIndex][j], string.Empty));
                            break;
                        }
                    }
                    index++;
                }
                else
                {
                    // ignore anchor
                    var anchor = rule.SourceRules[i] as Command.Anchor;
                    if (anchor != null) continue;

                    // word run command

                    // retrieve the end index
                    var ruleRun = new RuleRun(rule);
                    if (!rule.SourceRules[i].GetMatch(lang, index, rule, i, tokens, tags, words, ruleRun)) continue;

                    // get the words between the start and end index
                    int endIndex = ruleRun.StartIndexTo > end ? end : ruleRun.StartIndexTo;
                    wordRuns.Add(new Tuple<string, string>(endIndex < index
                        ? string.Empty
                        : sentence.Substring(locations[index],
                            locations[endIndex] - locations[index] + tokens[endIndex].Length), tags[index]));

                    index = endIndex + 1;
                }
            }

            // return the original sentence part if the rule does not match
            if (wordRuns.Count == 0)
                return sentence.Substring(locations[start], locations[end] - locations[start] + tokens[end].Length);

            // pad empty commands with empty strings
            while (wordRuns.Count < rule.SourceRules.Count)
                wordRuns.Add(new Tuple<string, string>(string.Empty, string.Empty));

            // reorder the sentence. believe it or not, most of this code is to determine whether to include a space after
            // the command or not. spaces are not inserted if the following tag is punctuation - but to determine this, we
            // need to skip over empty word runs. spaces are also not inserted after the last word.
            var sb = new StringBuilder();
            for (int i = 0; i < rule.ReplaceOrder.Count; i++)
            {
                int orderNumber = Int32.TryParse(rule.ReplaceOrder[i].ToString(), out orderNumber) ? orderNumber : -1;
                string insertionWords = orderNumber >= 0 ? wordRuns[orderNumber].Item1 : rule.ReplaceOrder[i] as string;
                if (string.IsNullOrEmpty(insertionWords)) continue;

                // match case of the insertion words
                if (start == 0 && sb.Length == 0)
                    insertionWords = char.ToUpper(insertionWords[0]) + insertionWords.Substring(1);
                else if (char.IsUpper(insertionWords[0]) && insertionWords != "I" &&
                         (orderNumber < 0 || wordRuns[orderNumber].Item2 != "NNP") &&
                         (insertionWords.Length <= 1 || !char.IsUpper(insertionWords[1])))
                    insertionWords = char.ToLower(insertionWords[0]) + insertionWords.Substring(1);

                // add the words
                sb.Append(insertionWords);

                // determine whether to insert a space - don't insert if following word is punctuation or at end of rule.
                for (int j = i + 1; j < rule.ReplaceOrder.Count; j++)
                {
                    // command
                    if (Int32.TryParse(rule.ReplaceOrder[j].ToString(), out orderNumber))
                    {
                        // first non-empty word run
                        if (orderNumber < wordRuns.Count && !string.IsNullOrEmpty(wordRuns[orderNumber].Item1))
                        {
                            if (!char.IsPunctuation(wordRuns[orderNumber].Item1[0])) sb.Append(' ');
                            break;
                        }
                    }
                    // word
                    else
                    {
                        sb.Append(' ');
                        break;
                    }
                }
            }
            string result = sb.ToString();

            // special conditional cases...
            // don't return a variant if reodering places punctuation at the start of the sentence
            if (result.Trim().Length > 0 && char.IsPunctuation(result.Trim()[0])) return null;

            return result;
        }

        /// <summary>
        ///     Convert the current class to a string.
        /// </summary>
        /// <returns>A string representation of the class</returns>
        public override string ToString()
        {
            var sb = new StringBuilder();
            foreach (Rule rule in _rules) sb.Append(rule + Environment.NewLine);
            foreach (var wordList in _words) sb.Append(wordList.Key + wordList.Value + Environment.NewLine);
            return sb.ToString();
        }

        /// <summary>
        ///     Convert string representation of the class into a class.
        /// </summary>
        /// <param name="ruleLines">A string representation of the class</param>
        /// <returns>A populated <c>RewriteRules</c> class</returns>
        /// <remarks>This method is used by the <c>Load</c> method.</remarks>
        public static RewriteRules FromString(string ruleLines)
        {
            var rewriteRules = new RewriteRules();
            foreach (string line in ruleLines.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                // comments start with a hash
                if (line.Length == 0 || line[0] == '#') continue;

                // commands start with digits and word lists don't
                if (char.IsDigit(line[0]))
                {
                    Rule rule = Rule.FromString(line);
                    if (rule != null) rewriteRules._rules.Add(rule);
                }
                else
                {
                    string key;
                    WordList wordList = WordList.FromString(line, out key);
                    if (wordList != null && key != null) rewriteRules._words[key] = wordList;
                }
            }
            return rewriteRules;
        }

        /// <summary>
        ///     The command class gathers all rule commands together into one static class.
        /// </summary>
        /// <remarks>All rule commands MUST implement the <c>ICommand</c> interface.</remarks>
        internal static class Command
        {
            // recursively test remaining commands within the rule
            private static bool getRemainingMatch(string lang, RuleRun ruleRun, int startIndex, int endIndex, Rule rule,
                int commandIndex, string[] tokens, string[] tags,
                ConcurrentDictionary<string, WordList> words)
            {
                // set end match index
                if (ruleRun != null && ruleRun.StartIndexFrom < 0)
                {
                    ruleRun.StartIndexFrom = startIndex;
                    ruleRun.StartIndexTo = endIndex;
                }

                // recursively match remaining commands
                if (rule != null && commandIndex < rule.SourceRules.Count - 1)
                {
                    // check match
                    // if command is an anchor, do not increment endindex
                    if (
                        !rule.SourceRules[commandIndex + 1].GetMatch(lang, rule.SourceRules[commandIndex].GetType() == typeof(Anchor) ? endIndex : endIndex + 1,
                        rule, commandIndex + 1, tokens,
                            tags, words, ruleRun)) return false;
                }
                else if (ruleRun != null)
                {
                    ruleRun.EndIndexFrom = startIndex;
                    ruleRun.EndIndexTo = endIndex;
                }

                if (ruleRun != null && ruleRun.EndIndexFrom == -1 && ruleRun.EndIndexTo == -1)
                {
                    ruleRun.EndIndexFrom = startIndex;
                    ruleRun.EndIndexTo = endIndex;
                }

                return true;
            }

            /// <summary>
            ///     Anchor to start or end of word.
            /// </summary>
            /// <remarks>
            ///     <para>
            ///         Use as the first rule to anchor to the start of a sentence or chunk. Use as last rule
            ///         to anchor to end of sentence or chunk.
            ///     </para>
            /// </remarks>
            internal class Anchor : ICommand
            {
                public const int COMMAND = 3;

                public bool GetMatch(string lang, int startIndex, Rule rule, int commandIndex, string[] tokens,
                    string[] tags,
                    ConcurrentDictionary<string, WordList> words, RuleRun ruleRun)
                {
                    if (!(startIndex <= tags.Length && rule != null && commandIndex < rule.SourceRules.Count &&
                          ((commandIndex == 0 && startIndex == 0) || (commandIndex > 0 && startIndex == tokens.Length) ||
                           (commandIndex > 0 && startIndex == tokens.Length - 1 && tags[tags.Length - 1].Length > 0 &&
                            char.IsPunctuation(tags[tags.Length - 1][0]))))) return false;
                    return getRemainingMatch(lang, ruleRun, startIndex, startIndex, rule, commandIndex, tokens, tags,
                        words);
                }

                public override string ToString()
                {
                    return string.Format(CultureInfo.InvariantCulture, "{0}", COMMAND);
                }

                // ReSharper disable MemberHidesStaticFromOuterClass
                public static ICommand FromString(string commandString)
                // ReSharper restore MemberHidesStaticFromOuterClass
                {
                    if (string.IsNullOrEmpty(commandString) ||
                        !commandString.StartsWith(COMMAND.ToString(CultureInfo.InvariantCulture))) return null;
                    try
                    {
                        var anchor = new Anchor();
                        return anchor;
                    }
                    catch
                    {
                        return null;
                    }
                }
            }

            internal interface ICommand
            {
                string ToString();

                bool GetMatch(string lang, int startIndex, Rule rule, int commandIndex, string[] tokens, string[] tags,
                    ConcurrentDictionary<string, WordList> words, RuleRun ruleRun);
            }

            /// <summary>
            ///     Match a word contained within a sublist. For each list word, a replacement rule will define which sublist will be
            ///     used
            ///     to replace the word.
            /// </summary>
            /// <remarks>Start list name with ! to return a match if the words in the list are NOT found.</remarks>
            internal class List : ICommand
            {
                public const int COMMAND = 1;

                public List()
                {
                    Name = string.Empty;
                    SourceIndex = 0;
                    ResultIndex = 0;
                }

                public string Name { get; set; }
                public int SourceIndex { get; set; }
                public int ResultIndex { get; set; }

                public bool GetMatch(string lang, int startIndex, Rule rule, int commandIndex, string[] tokens,
                    string[] tags,
                    ConcurrentDictionary<string, WordList> words, RuleRun ruleRun)
                {
                    // return non-match on overrun
                    if (startIndex >= tags.Length || (rule != null && commandIndex >= rule.SourceRules.Count))
                        return false;

                    // list match
                    string name = Name.StartsWith("!") ? Name.Substring(1) : Name;
                    if (!words.ContainsKey(name) || words[name].Count < SourceIndex || words[name].Count < ResultIndex)
                        return false;
                    bool found = false;
                    foreach (string word in words[name][SourceIndex])
                    {
                        if (string.Equals(tokens[startIndex], word, StringComparison.InvariantCultureIgnoreCase))
                        {
                            found = true;
                            break;
                        }
                    }

                    // list match
                    if (found ^ Name.StartsWith("!"))
                        return getRemainingMatch(lang, ruleRun, startIndex, startIndex, rule, commandIndex, tokens, tags,
                            words);

                    // no match
                    return false;
                }

                public override string ToString()
                {
                    return string.Format(CultureInfo.InvariantCulture, "{0}{1}{2}{3}", COMMAND, SourceIndex, ResultIndex,
                        Name);
                }

                // ReSharper disable MemberHidesStaticFromOuterClass
                public static ICommand FromString(string commandString)
                // ReSharper restore MemberHidesStaticFromOuterClass
                {
                    if (string.IsNullOrEmpty(commandString) ||
                        !commandString.StartsWith(COMMAND.ToString(CultureInfo.InvariantCulture))) return null;
                    try
                    {
                        var list = new List
                        {
                            SourceIndex = Convert.ToInt32(commandString[1].ToString(CultureInfo.InvariantCulture)),
                            ResultIndex = Convert.ToInt32(commandString[2].ToString(CultureInfo.InvariantCulture)),
                            Name = commandString.Substring(3)
                        };
                        return list;
                    }
                    catch
                    {
                        return null;
                    }
                }
            }

            /// <summary>
            ///     Match a specified word.
            /// </summary>
            /// <remarks>Start word with ! to return a match if the words is NOT found.</remarks>
            internal class Word : ICommand
            {
                public const int COMMAND = 2;

                public Word()
                {
                    Text = string.Empty;
                }

                public string Text { get; set; }

                public bool GetMatch(string lang, int startIndex, Rule rule, int commandIndex, string[] tokens,
                    string[] tags,
                    ConcurrentDictionary<string, WordList> words, RuleRun ruleRun)
                {
                    // return non-match on overrun
                    if (startIndex >= tags.Length || (rule != null && commandIndex >= rule.SourceRules.Count))
                        return false;

                    // word match
                    if (string.Equals(tokens[startIndex], Text.StartsWith("!") ? Text.Substring(1) : Text,
                        StringComparison.InvariantCultureIgnoreCase) ^ Text.StartsWith("!"))
                        return getRemainingMatch(lang, ruleRun, startIndex, startIndex, rule, commandIndex, tokens, tags,
                            words);

                    // no match
                    return false;
                }

                public override string ToString()
                {
                    return string.Format(CultureInfo.InvariantCulture, "{0}{1}", COMMAND, Text);
                }

                // ReSharper disable MemberHidesStaticFromOuterClass
                public static ICommand FromString(string commandString)
                // ReSharper restore MemberHidesStaticFromOuterClass
                {
                    if (string.IsNullOrEmpty(commandString) ||
                        !commandString.StartsWith(COMMAND.ToString(CultureInfo.InvariantCulture))) return null;
                    try
                    {
                        var word = new Word { Text = commandString.Substring(1) };
                        return word;
                    }
                    catch
                    {
                        return null;
                    }
                }
            }

            /// <summary>
            ///     Defines a run of words concluding when the next command is met. A minimum and maximum number of words may
            ///     be defined and a list of POS tags may also be given for which the matching words must achieve.
            /// </summary>
            /// <remarks>Start POS with ! to return a match if the POS is NOT found.</remarks>
            internal class WordRun : ICommand
            {
                public const int COMMAND = 0;

                private static readonly string[,] _exceptionList =
				{
					{@"RB", @"adverbexception"}, {@"VB", @"verbexception"},
					{@"NN", @"nounexception"}, {@"JJ", @"adjectiveexception"},
					{@"IN", @"prepositionexception"}
				};

                public WordRun()
                {
                    Min = 0;
                    Max = 0;
                    Pos = null;
                }

                public int Min { get; set; }
                public int Max { get; set; }
                public string[] Pos { get; set; }

                public bool GetMatch(string lang, int startIndex, Rule rule, int commandIndex, string[] tokens,
                    string[] tags,
                    ConcurrentDictionary<string, WordList> words, RuleRun ruleRun)
                {
                    // return non-match on overrun
                    if (startIndex >= tags.Length || (rule != null && commandIndex >= rule.SourceRules.Count))
                        return Min == 0 && Pos == null && rule != null && commandIndex >= rule.SourceRules.Count - 1;

                    // return non-match on conjunction
                    //if (PartOfSpeech.IsConjunction("en", tags[startIndex])) return false;

                    // POS doesn't create very good tags when abbreviations are used. for example "you're" is split into two tags
                    // "you" and "'re". to stop this, we will exit if the tag start with an apostraphe.
                    if (tokens[startIndex].Length > 0 &&
                        (tokens[startIndex][0] == '\'' || tokens[startIndex].ToLower() == "n't")) return false;

                    // match current command
                    int matches = 0;
                    int endIndex;
                    if (ruleRun == null) ruleRun = new RuleRun { Rule = rule, StartIndexFrom = startIndex };
                    for (endIndex = startIndex; endIndex < tokens.Length; endIndex++)
                    {
                        if (rule != null && matches >= Min &&
                            (commandIndex < rule.SourceRules.Count - 1 || endIndex >= tags.Length
                            //|| Min > 0 || (matches >= Max && Max > 0)) &&
                                ) &&
                            getRemainingMatch(lang, null, startIndex, endIndex - 1, rule, commandIndex, tokens, tags,
                                words))
                        {
                            endIndex--;
                            break;
                        }

                        bool isPosMatch = Pos == null || Pos.Length == 0;
                        bool isNegativePosMatch = false;
                        if (Pos != null)
                        {
                            for (int posIndex = 0; posIndex < Pos.Length; posIndex++)
                            {
                                var p = Pos[posIndex];
                                string pos = p.StartsWith("!") ? p.Substring(1) : p;
                                if (string.Equals(pos, "COMMA", StringComparison.InvariantCultureIgnoreCase)) pos = ",";

                                // POS not match
                                if (p.StartsWith("!"))
                                {
                                    if (string.Equals(tags[endIndex], pos, StringComparison.InvariantCultureIgnoreCase) ||
                                        (pos.EndsWith("*") &&
                                         tags[endIndex].StartsWith(pos.Substring(0, pos.Length - 1),
                                                                   StringComparison.InvariantCultureIgnoreCase)))
                                        break;
                                    // if not match, still need to check if there is another not match, unless this is the last
                                    if (posIndex < Pos.Length - 1) continue;
                                }
                                else
                                {
                                    // POS match
                                    if (!(string.Equals(tags[endIndex], pos, StringComparison.InvariantCultureIgnoreCase) ||
                                          (pos.EndsWith("*") &&
                                           tags[endIndex].StartsWith(pos.Substring(0, pos.Length - 1),
                                               StringComparison.InvariantCultureIgnoreCase)))) continue;
                                }
                                isPosMatch = true;

                                // dont set a match if found word is in an exception list
                                if (_exceptionList == null) break;
                                for (int i = 0; i < _exceptionList.GetLength(0); i++)
                                {
                                    if (!pos.StartsWith(_exceptionList[i, 0]) ||
                                        !words.ContainsKey(_exceptionList[i, 1]) ||
                                        words[_exceptionList[i, 1]].Count <= 0) continue;
                                    foreach (string exception in words[_exceptionList[i, 1]][0])
                                    {
                                        if (
                                            !string.Equals(tokens[endIndex], exception,
                                                StringComparison.InvariantCultureIgnoreCase))
                                            continue;
                                        isPosMatch = false;
                                        break;
                                    }
                                }

                                if (isPosMatch) break;
                            }
                        }
                        if (isPosMatch)
                            matches++;
                        else
                        {
                            if (matches < Min) return false;
                            endIndex--;
                            break;
                        }

                        // return true if required matches found
                        if (Max > 0 && matches >= Max) break;

                        // return true if minimum matches found and reached end of sentence or punctuation is found
                        if (matches >= Min && endIndex == tokens.Length - 1)
                            break; // || (tags[endIndex].Length > 0 && char.IsPunctuation(tags[endIndex][0])))) 
                    }

                    // return rule run if all commands match
                    return matches >= Min &&
                           getRemainingMatch(lang, ruleRun, startIndex, endIndex, rule, commandIndex, tokens, tags,
                               words);
                }

                public override string ToString()
                {
                    var sb = new StringBuilder();
                    if (Pos != null && Pos.Length > 0)
                        foreach (string p in Pos) sb.Append((sb.Length > 0 ? "," : "") + p);
                    return string.Format(CultureInfo.InvariantCulture, "{0}{1}{2}{3}", COMMAND, Min, Max, sb);
                }

                // ReSharper disable MemberHidesStaticFromOuterClass
                public static ICommand FromString(string commandString)
                // ReSharper restore MemberHidesStaticFromOuterClass
                {
                    if (string.IsNullOrEmpty(commandString) ||
                        !commandString.StartsWith(COMMAND.ToString(CultureInfo.InvariantCulture))) return null;
                    try
                    {
                        var wordRun = new WordRun
                        {
                            Min = Convert.ToInt32(commandString[1].ToString(CultureInfo.InvariantCulture)),
                            Max = Convert.ToInt32(commandString[2].ToString(CultureInfo.InvariantCulture))
                        };
                        if (commandString.Length > 3) wordRun.Pos = commandString.Substring(3).Split(new[] { ',' });
                        return wordRun;
                    }
                    catch
                    {
                        return null;
                    }
                }
            }
        }


        /// <summary>
        ///     A rule consists of a list of source commands and replacement order.
        /// </summary>
        /// <remarks>
        ///     A rule describes how a sentence part can be rewriten. The rule matches the sentence and breaks it down into parts.
        ///     The replacememnt
        ///     order then describes how each part can be reconstructed to create the rewritten sentence.
        /// </remarks>
        internal class Rule
        {
            public Rule()
            {
                SourceRules = new List<Command.ICommand>();
                ReplaceOrder = new List<object>();
            }

            public List<Command.ICommand> SourceRules { get; private set; }
            public List<object> ReplaceOrder { get; private set; }

            public override string ToString()
            {
                var sb = new StringBuilder();
                for (int i = 0; i < SourceRules.Count; i++)
                {
                    if (i > 0) sb.Append('/');
                    sb.Append(SourceRules[i]);
                }
                sb.Append(' ');
                for (int i = 0; i < ReplaceOrder.Count; i++)
                {
                    if (i > 0) sb.Append('/');
                    sb.Append(ReplaceOrder[i]);
                }

                return sb.ToString();
            }

            // ReSharper disable MemberHidesStaticFromOuterClass
            public static Rule FromString(string ruleString) // ReSharper restore MemberHidesStaticFromOuterClass
            {
                if (string.IsNullOrEmpty(ruleString)) return null;

                try
                {
                    var rule = new Rule();

                    // split rule string into source and replace rules
                    string[] splitRule = ruleString.Split(new[] { ' ' });

                    // convert source rules
                    string[] sourceRules = splitRule[0].Split(new[] { '/' });
                    foreach (string sourceRule in sourceRules)
                    {
                        Command.ICommand wordRun = Command.WordRun.FromString(sourceRule);
                        if (wordRun != null) rule.SourceRules.Add(wordRun);
                        else
                        {
                            Command.ICommand list = Command.List.FromString(sourceRule);
                            if (list != null) rule.SourceRules.Add(list);
                            else
                            {
                                Command.ICommand word = Command.Word.FromString(sourceRule);
                                if (word != null) rule.SourceRules.Add(word);
                                else
                                {
                                    Command.ICommand anchor = Command.Anchor.FromString(sourceRule);
                                    if (anchor != null) rule.SourceRules.Add(anchor);
                                }
                            }
                        }
                    }

                    // convert replace order rule
                    string[] replaceOrders = splitRule[1].Split(new[] { '/' });
                    foreach (string replaceOrder in replaceOrders) rule.ReplaceOrder.Add(replaceOrder);

                    return rule;
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>
        ///     This class is used to store which rules apply to which parts of a sentence.
        /// </summary>
        internal class RuleRun
        {
            public RuleRun()
            {
                StartIndexTo = -1;
                StartIndexFrom = -1;
                EndIndexFrom = -1;
                EndIndexTo = -1;
                Rule = null;
            }

            public RuleRun(Rule rule)
                : this()
            {
                Rule = rule;
            }

            public int StartIndexFrom { get; set; }
            public int StartIndexTo { get; set; }
            public int EndIndexFrom { get; set; }
            public int EndIndexTo { get; set; }
            public Rule Rule { get; set; }

            public override string ToString()
            {
                return string.Format("({0})({1}):({2})({3}) {4}", StartIndexFrom, StartIndexTo, EndIndexFrom, EndIndexTo,
                    Rule);
            }

            public void Offset(int offset)
            {
                StartIndexFrom += offset;
                StartIndexTo += offset;
                EndIndexFrom += offset;
                EndIndexTo += offset;
            }
        }

        /// <summary>
        ///     This class is used to store a list of list of words.
        /// </summary>
        internal class WordList : List<string[]>
        {
            public override string ToString()
            {
                var sb = new StringBuilder();
                foreach (var list in this)
                {
                    for (int i = 0; i < list.Length; i++)
                    {
                        sb.Append(i == 0 ? ';' : '/');
                        sb.Append(list[i]);
                    }
                }
                return sb.ToString();
            }

            // ReSharper disable MemberHidesStaticFromOuterClass
            public static WordList FromString(string wordListString, out string key)
            // ReSharper restore MemberHidesStaticFromOuterClass
            {
                if (string.IsNullOrEmpty(wordListString) || !wordListString.Contains(";"))
                {
                    key = null;
                    return null;
                }
                try
                {
                    var list = new WordList();
                    string[] wordLists = wordListString.Split(new[] { ';' });
                    for (int i = 1; i < wordLists.Length; i++) list.Add(wordLists[i].Split(new[] { '/' }));
                    key = wordLists[0];
                    return list;
                }
                catch
                {
                    key = null;
                    return null;
                }
            }
        }

        #region Events

        /// <summary>
        ///     Rewrite rules failed to load event.
        /// </summary>
        public static event RuleEventHandler LoadFailed;

        /// <summary>
        ///     Rewrite rules successfully loaded event.
        /// </summary>
        public static event RuleEventHandler LoadComplete;

        #endregion
    }

    /// <summary>
    ///     Generic Rules event handler
    /// </summary>
    /// <param name="language">Selected language</param>
    public delegate void RuleEventHandler(string language);
}