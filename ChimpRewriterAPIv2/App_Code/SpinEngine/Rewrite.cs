/**************************************************************************************************
Class:	Rewrite
Author:   Mark Beljaars
Revision: 1.0
History:  27/12/13 Created
Notes:	Methods for adding synonyms to text for use by the API.

(C) Copyright 2013 Aktura Technology.  All Rights Reserved.
**************************************************************************************************/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SpinEngine;
using ikvm.extensions;

namespace ChimpRewriterAPIv3.SpinEngine
{
    /// <summary>
    ///     Instant Unique Character set type
    /// </summary>
    public enum InstantUniqueType
    {
        /// <summary>
        ///     Instant unique disabled
        /// </summary>
        None,

        /// <summary>
        ///     Best character set (minimum options)
        /// </summary>
        Best,

        /// <summary>
        ///     Full character set (maximum options)
        /// </summary>
        Full
    }

    /// <summary>
    ///     Spintax spin type
    /// </summary>
    public enum SpinType
    {
        /// <summary>
        ///     Spin disabled
        /// </summary>
        None,

        /// <summary>
        ///     Random spin
        /// </summary>
        Random,

        /// <summary>
        ///     Ordered spin
        /// </summary>
        Ordered,

        /// <summary>
        ///     Respin existing spin using supplied parameters
        /// </summary>
        Respin
    }

    public static class Rewrite
    {
        // Maximum phrase word length (minimize to increase performance)
        public const int MAX_PHRASE_LENGTH = 4;

        // Response returned on detected error
        private const string ERROR_RESPONSE = "<error>{0}</error>";

        // instant unique replacement strings
        public const string INSTANT_UNIQUE_SET1 =
            @"A|&#913;|&#1040;,B|&#914;|&#1042;,C|&#1057;,E|&#917;|&#1045;,H|&#919;|&#1053;,I|&#1030;,J|&#1032;,K|&#922;,M|&#924;|&#1052;,N|&#925;,O|&#927;|&#1054;,P|&#1056;,S|&#1029;,T|&#932;|&#1058;,X|&#1061;,Y|&#933;,a|&#1072;,c|&#1089;,e|&#1077;,i|&#1110;,j|&#1112;,o|&#959;|&#1086;,p|&#1088;,s|&#1109;,u|&#965;,v|&#957;,y|&#1091;";

        public const string INSTANT_UNIQUE_SET2 =
            @"A|&#192;|&#193;,C|&#262;,E|&#200;|&#201;,I|&#204;|&#205;,O|&#210;|&#211;,U|&#217;|&#218;,Y|&#221;,a|&#224;|&#225;,c|&#263;,e|&#233;|&#233;,i|&#237;,o|&#242;|&#243;,u|&#249;";

        // instant unique dictionaries
        private static ConcurrentDictionary<char, List<string>> _instantUniqueCharSet;
        private static ConcurrentDictionary<char, List<string>> _instantUniqueFullCharSet;

        // static constructor
        static Rewrite()
        {
            Rand = new Random();
        }

        public static Random Rand { get; private set; }

        private static ConcurrentDictionary<char, List<string>> instantUniqueCharSet
        {
            get { return _instantUniqueCharSet ?? (_instantUniqueCharSet = convertCharacterSet(INSTANT_UNIQUE_SET1)); }
        }

        // retrieve largest instant unique character set
        private static ConcurrentDictionary<char, List<string>> instantUniqueFullCharSet
        {
            get
            {
                return _instantUniqueFullCharSet ??
                       (_instantUniqueFullCharSet = convertCharacterSet(INSTANT_UNIQUE_SET1 + INSTANT_UNIQUE_SET2));
            }
        }

        /// <summary>
        ///     Returns TRUE if the previous rewrite request was cancelled.
        /// </summary>
        public static bool WasCancelled { get; private set; }

        /// <summary>
        ///     Add synonyms to given source text.
        /// </summary>
        /// <param name="source">Source text</param>
        /// <param name="spinType">Existing spin action: 2=Ordered, 1=Random, 0=None</param>
        /// <param name="orderedSpinSynonymNumber">
        ///     Synonym number used for ordered spin (0=Original, 1=First synonym, 2=Second
        ///     synonym etc)
        /// </param>
        /// <param name="sentenceRewrite">TRUE to auto rewrite sentences using rules engine</param>
        /// <param name="wordQuality">Single word synonym quality: 5=Best, 4=Better, 3=Good, 2=Average, 1=All, 0=None</param>
        /// <param name="phraseQuality">
        ///     Phrase synonym quality: 6=Same as word quality, 5=Best, 4=Better, 3=Good, 2=Average, 1=All,
        ///     0=None
        /// </param>
        /// <param name="posMatch">POS match: 4=FullSpin (deprecated), 3=Full, 2=Loose, 1=Extremely Loose, 0=None</param>
        /// <param name="instantUnique">Instant Unique character set: 2=Best, 1=Full, 0=None</param>
        /// <param name="checkGrammar">TRUE to check grammar before replacing. Will slow down spin</param>
        /// <param name="phraseSpin">TRUE to only replace phrases with other phrases</param>
        /// <param name="language">Two digit language code (eg "en" for english)</param>
        /// <param name="protectedTerms">Comma separated list of protected words and phrases</param>
        /// <param name="returnSpin">TRUE to return spin instead of spun text</param>
        /// <param name="replaceFq">Word replacement frequency (eg 2 means only replace every 2nd word on average)</param>
        /// <param name="excludeOrig">TRUE to exclude original word from spin to maximize uniqueness</param>
        /// <param name="dontSpinExistingSpin">TRUE to exclude spinning within existing spin</param>
        /// <param name="spinTidy">TRUE to enable spin tidy (eg replaces "a" with "an")</param>
        /// <param name="reorderPara">TRUE to randomly reorder paragraphs</param>
        /// <param name="maxSyns">Maximum number of synonyms to add per word (0 to disable)</param>
        /// <param name="instantUniqueFq">Instant unique replacement frequency</param>
        /// <param name="countFeatureUse">Set TRUE to store a feature use counters</param>
        /// <param name="inhibitSpinProperNoun">TRUE to inhibit spinning of proper nouns</param>
        /// <param name="replacementTags">
        ///     Comma/semicolon separated list of replacement tags in form:
        ///     tag1,item1,item2;tag2,item1,item2 etc
        /// </param>
        /// <param name="lockedLines">Comma separated list of locked line indexes</param>
        /// <param name="linkedLines">Comma separated list of linked line indexes</param>
        /// <param name="signatures">Semicolon separated list of signatures</param>
        /// <returns>The spun text or an error code</returns>
        /// <remarks>
        ///     <para>This method is designed for use by the api.</para>
        ///     <para>See <c>GetErrorDescription</c> for error code descriptions.</para>
        ///     <para>
        ///         The <c>lockedLines</c> and <c>linkedLines</c> are used to lock and link lines when
        ///         the <c>reorderPara</c> option is set. The index is a 0 based line index - the first line is 0, the second 1 and
        ///         so on.
        ///     </para>
        ///     <para>
        ///         User defined protected areas may be created by surrounding the area within double hashes (eg ##a protected
        ///         area##)
        ///     </para>
        ///     <para>
        ///         <c>replacementTags</c> contains a list of replacement replacementTags. Use #Tag_Name# to identify the tag
        ///         within the source.
        ///     </para>
        ///     <para>
        ///         If a list of <c>signatures</c> are supplied, one signature will be randomly appended to the end of the
        ///         document.
        ///     </para>
        /// </remarks>
        public static string GlobalSynonyms(string source, int spinType = 0, int orderedSpinSynonymNumber = 0,
            bool sentenceRewrite = true, int wordQuality = 4, int phraseQuality = 3,
            int posMatch = 3, int instantUnique = 0, bool checkGrammar = false,
            bool phraseSpin = false, string language = "en", string protectedTerms = null,
            bool returnSpin = false, int replaceFq = 1, bool excludeOrig = false,
            bool dontSpinExistingSpin = false, bool spinTidy = true, bool reorderPara = false,
            int maxSyns = 0, int instantUniqueFq = 3, string replacementTags = null,
            string lockedLines = null,
            string linkedLines = null, string signatures = null, bool countFeatureUse = true, bool inhibitSpinProperNoun = false)
        {
            SpinType existingSpinType;
            switch (spinType)
            {
                case 2:
                    existingSpinType = SpinType.Ordered;

                    break;
                case 1:
                    existingSpinType = SpinType.Random;
                    if (countFeatureUse) CounterLog.Instance.IncCounter("GlobalSynonyms.spinType.Random");
                    break;
                default:
                    existingSpinType = SpinType.None;
                    if (countFeatureUse) CounterLog.Instance.IncCounter("GlobalSynonyms.spinType.None");
                    break;
            }

            // determine single word spin quality 
            QualityRating wordQualityRating;
            switch (wordQuality)
            {
                case 5:
                    wordQualityRating = QualityRating.Best;
                    break;
                case 4:
                    wordQualityRating = QualityRating.Better;
                    break;
                case 3:
                    wordQualityRating = QualityRating.Good;
                    break;
                case 2:
                    wordQualityRating = QualityRating.Average;
                    break;
                case 1:
                    wordQualityRating = QualityRating.All;
                    break;
                default:
                    wordQualityRating = QualityRating.None;
                    break;
            }

            // determine phrase spin quality 
            QualityRating phraseQualityRating;
            switch (phraseQuality)
            {
                case 6:
                    phraseQualityRating = wordQualityRating;
                    break;
                case 5:
                    phraseQualityRating = QualityRating.Best;
                    break;
                case 4:
                    phraseQualityRating = QualityRating.Better;
                    break;
                case 3:
                    phraseQualityRating = QualityRating.Good;
                    break;
                case 2:
                    phraseQualityRating = QualityRating.Average;
                    break;
                case 1:
                    phraseQualityRating = QualityRating.All;
                    break;
                default:
                    phraseQualityRating = QualityRating.None;
                    break;
            }

            // determine pos match wordQuality
            PosTagMatchType posMatchType;
            switch (posMatch)
            {
                case 4:
                    posMatchType = PosTagMatchType.Full;
                    break;
                case 3:
                    posMatchType = PosTagMatchType.Full;
                    break;
                case 2:
                    posMatchType = PosTagMatchType.Loose;
                    break;
                case 1:
                    posMatchType = PosTagMatchType.ExtremelyLoose;
                    break;
                default:
                    posMatchType = PosTagMatchType.None;
                    break;
            }

            InstantUniqueType instantUniqueType;
            switch (instantUnique)
            {
                case 2:
                    instantUniqueType = InstantUniqueType.Best;
                    break;
                case 1:
                    instantUniqueType = InstantUniqueType.Full;
                    break;
                default:
                    instantUniqueType = InstantUniqueType.None;
                    break;
            }

            // detect errors and return an error code
            // no source text
            if (String.IsNullOrEmpty(source))
            {
                Trace.WriteLine("!!! " + GetErrorDescription(0));
                return String.Format(ERROR_RESPONSE, GetErrorDescription(0));
            }

            // no thesauri loaded
            if (!Thesaurus.IsLoaded)
            {
                Trace.WriteLine("!!! " + GetErrorDescription(1));
                return String.Format(ERROR_RESPONSE, GetErrorDescription(1));
            }

            // specified language not available
            language = language.ToLower();
            if (!Thesaurus.Thesauri.Keys.Contains(language))
            {
                Trace.WriteLine("!!! " + GetErrorDescription(2));
                return String.Format(ERROR_RESPONSE, GetErrorDescription(2));
            }

            // part of speach not loaded
            if (posMatchType != PosTagMatchType.None && !PartOfSpeech.IsLoaded)
            {
                Trace.WriteLine("!!! " + GetErrorDescription(3));
                return String.Format(ERROR_RESPONSE, GetErrorDescription(3));
            }

            // create a list of protected terms
            string[] protectedTermList = String.IsNullOrEmpty(protectedTerms)
                ? null
                : protectedTerms.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            // create list of linked lines
            int[] lockedLineArray = null;
            if (lockedLines != null)
            {
                var list = new List<int>();
                string[] indexes = lockedLines.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string index in indexes)
                {
                    int val;
                    if (int.TryParse(index, out val)) list.Add(val);
                }
                lockedLineArray = list.ToArray();
            }

            // create list of linked lines
            int[] linkedLineArray = null;
            if (linkedLines != null)
            {
                var list = new List<int>();
                string[] indexes = linkedLines.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string index in indexes)
                {
                    int val;
                    if (int.TryParse(index, out val)) list.Add(val);
                }
                linkedLineArray = list.ToArray();
            }

            // create list of replacementTags
            Dictionary<string, List<string>> tagDict = null;
            if (replacementTags != null)
            {
                tagDict = new Dictionary<string, List<string>>();
                string[] tagList = replacementTags.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string tag in tagList)
                {
                    string[] tagItems = tag.Split(new[] { ',' });
                    if (string.IsNullOrEmpty(tagItems[0])) continue;
                    for (int i = 1; i < tagItems.Length; i++)
                    {
                        if (!tagDict.ContainsKey(tagItems[0])) tagDict.Add(tagItems[0], new List<string>());
                        tagDict[tagItems[0]].Add(tagItems[i]);
                    }
                }
            }

            // create a list of signatures
            string[] signatureArray = signatures == null
                ? null
                : signatures.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            if (countFeatureUse)
            {
                CounterLog.Instance.IncCounter("GlobalSynonyms.spinType." + existingSpinType);
                CounterLog.Instance.IncCounter("GlobalSynonyms.wordQuality." + wordQualityRating);
                CounterLog.Instance.IncCounter("GlobalSynonyms.phraseQuality." + phraseQualityRating);
                CounterLog.Instance.IncCounter("GlobalSynonyms.posMatch." + posMatchType);
                CounterLog.Instance.IncCounter("GlobalSynonyms.instantUnique." + instantUniqueType);
                CounterLog.Instance.IncCounter("GlobalSynonyms.language." + language);
                CounterLog.Instance.IncCounter("GlobalSynonyms.replaceFq." + replaceFq);
                CounterLog.Instance.IncCounter("GlobalSynonyms.maxSyns." + maxSyns);
                if (instantUnique != 0) CounterLog.Instance.IncCounter("GlobalSynonyms.instantUniqueFq." + instantUniqueFq);
                if (string.IsNullOrEmpty(protectedTerms)) CounterLog.Instance.IncCounter("GlobalSynonyms.protectedTerms");
                if (string.IsNullOrEmpty(replacementTags)) CounterLog.Instance.IncCounter("GlobalSynonyms.replacementTags");
                if (string.IsNullOrEmpty(lockedLines)) CounterLog.Instance.IncCounter("GlobalSynonyms.lockedLines");
                if (reorderPara && string.IsNullOrEmpty(linkedLines)) CounterLog.Instance.IncCounter("GlobalSynonyms.linkedLines");
                if (reorderPara && string.IsNullOrEmpty(signatures)) CounterLog.Instance.IncCounter("GlobalSynonyms.signatures");
                if (sentenceRewrite) CounterLog.Instance.IncCounter("GlobalSynonyms.sentenceRewrite");
                if (checkGrammar) CounterLog.Instance.IncCounter("GlobalSynonyms.checkGrammar");
                if (phraseSpin) CounterLog.Instance.IncCounter("GlobalSynonyms.phraseSpin");
                if (returnSpin) CounterLog.Instance.IncCounter("GlobalSynonyms.returnSpin");
                if (excludeOrig) CounterLog.Instance.IncCounter("GlobalSynonyms.excludeOrig");
                if (dontSpinExistingSpin) CounterLog.Instance.IncCounter("GlobalSynonyms.dontSpinExistingSpin");
                if (spinTidy) CounterLog.Instance.IncCounter("GlobalSynonyms.spinTidy");
                if (reorderPara) CounterLog.Instance.IncCounter("GlobalSynonyms.reorderPara");
            }

            try
            {
                // add synonyms
                string result = GlobalSynonyms(source, existingSpinType, orderedSpinSynonymNumber, sentenceRewrite,
                    wordQualityRating,
                    phraseQualityRating, posMatchType, instantUniqueType, checkGrammar, phraseSpin, language,
                    protectedTermList, returnSpin, replaceFq, excludeOrig, dontSpinExistingSpin, spinTidy,
                    reorderPara, maxSyns, instantUniqueFq, tagDict, lockedLineArray, linkedLineArray, signatureArray);
                return result;
            }
            catch (Exception ex)
            {
                // unknown error
                Trace.WriteLine("!!! " + GetErrorDescription(-1) + ":" + ex.Message);
                return String.Format(ERROR_RESPONSE, GetErrorDescription(-1));
            }
        }

        /// <summary>
        ///     Returns an error description for the given error code.
        /// </summary>
        /// <param name="errorCode">Error code</param>
        /// <returns>An error description for the given error code</returns>
        public static string GetErrorDescription(int errorCode)
        {
            switch (errorCode)
            {
                case 0:
                    return "There are no words in your article!";
                case 1:
                    return "No thesauri loaded on server. Please contact support if this problem persists.";
                case 2:
                    string languages = string.Empty;
                    foreach (string language in Thesaurus.Thesauri.Keys)
                        languages += (string.IsNullOrEmpty(languages) ? string.Empty : ",") + language;
                    return "The specified language not supported. Supported languages: " + languages;
                case 3:
                    return "POS models not loaded on server. Please contact support if this problem persists.";
                default:
                    return "An unknown error has occurred. Please contact support if this problem persists.";
            }
        }

        /// <summary>
        ///     Cancel the current rewrite operation.
        /// </summary>
        public static void Cancel()
        {
            WasCancelled = true;
        }

        /// <summary>
        ///     Add synonyms to given source text.
        /// </summary>
        /// <param name="source">Source text</param>
        /// <param name="spinType">Existing spin action</param>
        /// <param name="orderedSpinSynonymNumber">
        ///     Synonym number used for ordered spin (0=Original, 1=First synonym, 2=Second
        ///     synonym etc)
        /// </param>
        /// <param name="sentenceRewrite">TRUE to auto rewrite sentences using rules engine</param>
        /// <param name="wordQuality">Single word synonym quality</param>
        /// <param name="phraseQuality">Phrase synonym quality</param>
        /// <param name="posMatch">POS match</param>
        /// <param name="instantUnique">Instant unique character set</param>
        /// <param name="checkGrammar">TRUE to check grammar before replacing. Will slow down spin</param>
        /// <param name="phraseSpin">TRUE to only replace phrases with other phrases</param>
        /// <param name="language">Two digit language code (eg "en" for english)</param>
        /// <param name="protectedTerms">List of protected terms</param>
        /// <param name="returnSpin">TRUE to return spin instead of spun text</param>
        /// <param name="replaceFq">Word replacement frequency (eg 2 means only replace every 2nd word on average)</param>
        /// <param name="excludeOrig">TRUE to exclude original word from spin to maximize uniqueness</param>
        /// <param name="dontSpinExistingSpin">TRUE to exclude spinning within existing spin</param>
        /// <param name="spinTidy">TRUE to enable spin tidy (eg replaces "a" with "an")</param>
        /// <param name="reorderPara">TRUE to randomly reorder paragraphs</param>
        /// <param name="maxSyns">Maximum number of synonyms to add per word (0 to disable)</param>
        /// <param name="instantUniqueFq">Instant unique replacement frequency</param>
        /// <param name="inhibitSpinProperNoun">TRUE to inhibit spinning of proper nouns</param>
        /// <param name="lockedLines">List of locked line indexes</param>
        /// <param name="linkedLines">List of linked line indexes</param>
        /// <param name="replacementTags">List of replacement tags</param>
        /// <param name="signatures">List of post append signatures</param>
        /// <remarks>
        ///     <para>
        ///         The <c>lockedLines</c> and <c>linkedLines</c> are used to lock and link lines when
        ///         the <c>reorderPara</c> option is set. The index is a 0 based line index - the first line is 0, the second 1 and
        ///         so on.
        ///     </para>
        ///     <para>
        ///         User defined protected areas may be created by surrounding the area within double hashes (eg ##a protected
        ///         area##)
        ///     </para>
        ///     <para>
        ///         <c>replacementTags</c> contains a list of replacement replacementTags. Use #Tag_Name# to identify the tag
        ///         within the source.
        ///     </para>
        ///     <para>
        ///         If a list of <c>signatures</c> are supplied, one signature will be randomly appended to the end of the
        ///         document.
        ///     </para>
        /// </remarks>
        /// <returns>The spun text or a number representing the error</returns>
        public static string GlobalSynonyms(string source, SpinType spinType = SpinType.None,
            int orderedSpinSynonymNumber = 0,
            bool sentenceRewrite = true, QualityRating wordQuality = QualityRating.Better,
            QualityRating phraseQuality = QualityRating.Average,
            PosTagMatchType posMatch = PosTagMatchType.Full,
            InstantUniqueType instantUnique = InstantUniqueType.None,
            bool checkGrammar = false, bool phraseSpin = false, string language = "en",
            string[] protectedTerms = null, bool returnSpin = false, int replaceFq = 1,
            bool excludeOrig = false, bool dontSpinExistingSpin = false, bool spinTidy = true,
            bool reorderPara = false, int maxSyns = 0, int instantUniqueFq = 3,
            Dictionary<string, List<string>> replacementTags = null, int[] lockedLines = null, int[] linkedLines = null,
            string[] signatures = null, bool inhibitSpinProperNoun = false)
        {
            // reset cancel flag
            WasCancelled = false;

            // ensure language code is lower case
            language = language.ToLower();

            // create a guid code (base64 to reduce to 22 chars) to uniquely identify trace messages from
            // concurrent executions
            string guid = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            Trace.WriteLine(guid + ":Adding synonyms...");

            // trigger rewrite started event
            if (RewriteStarted != null) RewriteStarted(null, EventArgs.Empty);
            if (RewriteProgress != null) RewriteProgress(0f);

            // *-- PRE SPIN --*

            // add replacement replacementTags
            if (replacementTags != null && replacementTags.Count > 0)
            {
                int index = Rand.Next(5000);
                var tagResult = new StringBuilder(source);
                foreach (var tag in replacementTags)
                {
                    if (string.IsNullOrEmpty(tag.Key) || tag.Value == null || tag.Value.Count == 0) continue;
                    string tagReplace;
                    if (returnSpin)
                    {
                        var sb = new StringBuilder();
                        if (tag.Value.Count > 1) sb.Append("~{");
                        for (int i = 0; i < tag.Value.Count; i++)
                        {
                            if (i > 0) sb.Append('|');
                            sb.Append(tag.Value[i]);
                        }
                        if (tag.Value.Count > 1) sb.Append('}');
                        tagReplace = sb.ToString();
                    }
                    else tagReplace = tag.Value[index % tag.Value.Count];
                    tagResult.Replace(string.Format(@"#{0}#", tag.Key), tagReplace);
                }
                source = tagResult.ToString();
            }

            // spin source if it contains jetspin
            if (spinType != SpinType.None)
                source = SpinText(source, spinType, orderedSpinSynonymNumber, excludeOrig);

            // *-- SPIN --*

            // ensure each paragraph is terminated with a line feed
            source = source.Replace("\r", String.Empty).Replace("</p>\n", "</p>").Replace("</p>", "</p>\n");

            // split the source text into paragraphs
            var result = new StringBuilder();
            string[] sourceParagraphs = source.Split(new[] { '\n' });
            int paraLengthTotal = 0;

            // store total characters for use by percentage complete event
            var totalChars = (float)source.Length;
            foreach (string p in sourceParagraphs)
            {
                if (WasCancelled) return source;
                StringBuilder rewrite = spinParagraph(ref paraLengthTotal, p, guid, totalChars, sentenceRewrite,
                    wordQuality, phraseQuality,
                    posMatch, instantUnique, checkGrammar, phraseSpin, language, protectedTerms, returnSpin, replaceFq,
                    excludeOrig, dontSpinExistingSpin, spinTidy, maxSyns, instantUniqueFq, inhibitSpinProperNoun);
                if (rewrite != null) result.Append(rewrite);
                else result.Append(p);
            }

            // remove trailing superfolous carriage return
            result.Remove(result.Length - 2, 2);

            // *-- POST SPIN --*

            // remove protected area markers
            if (!returnSpin) result.Replace("##", string.Empty);

            // reorder result paragraphs
            if (reorderPara)
            {
                try
                {
                    // split result back into paragraphs. i do this rather than keeping paragraphs in the above step as 
                    // reordering is rare and making this less efficient is better than making normal spinning less 
                    // efficient.
                    string[] lines = result.Replace("\r", String.Empty).ToString().Split(new[] { '\n' });
                    result.Clear();
                    if (lines.Length > 1)
                    {
                        //  create a list of randome paragraphs
                        for (int y = 0; y < lines.Length; y++)
                        {
                            if (WasCancelled) return source;

                            // look for the next locked line
                            int z = y;
                            for (; z < lines.Length; z++)
                                if ((lockedLines != null && lockedLines.Contains(z)) ||
                                    lines[z].Trim().StartsWith("<h", StringComparison.InvariantCultureIgnoreCase) ||
                                    lines[z].Trim().StartsWith("&lt;h", StringComparison.InvariantCultureIgnoreCase)) break;

                            // current line is locked - move on
                            if (y == z)
                            {
                                result.Append(lines[z]);
                                result.Append(Environment.NewLine);
                                if (y >= lines.Length - 1 || lines[y + 1].Length > 0) continue;
                                result.Append(Environment.NewLine);
                                y++;
                                continue;
                            }
                            if (z < lines.Length - 1) z--;

                            // store lines
                            var listReorderedLines = new List<List<string>>();
                            bool wasLinked = false;
                            int spinAtEnd = 0;
                            for (int x = y; x <= Math.Min(lines.Length - 1, z); x++)
                            {
                                if (wasLinked) listReorderedLines[listReorderedLines.Count - 1].Add(lines[x]);
                                else listReorderedLines.Add(new List<string> { lines[x] });
                                foreach (char ch in lines[x])
                                {
                                    if (ch == '{') spinAtEnd++;
                                    else if (ch == '}') spinAtEnd--;
                                }
                                wasLinked = (linkedLines != null && linkedLines.Contains(x)) ||
                                            (x < lines.Length - 1 && lines[x + 1].Length == 0) || spinAtEnd > 0;
                            }

                            // randomally add them back in
                            while (listReorderedLines.Count > 0)
                            {
                                int x = Rand.Next(listReorderedLines.Count);
                                for (int i = 0; i < listReorderedLines[x].Count; i++)
                                {
                                    result.Append(listReorderedLines[x][i]);
                                    result.Append(Environment.NewLine);
                                }
                                listReorderedLines.RemoveAt(x);
                            }

                            y = z;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("!!! Rewrite.GlobalSynonyms:" + guid + ":Reorder fail :" + ex.Message);
                }
            }

            // add signatures
            if (signatures != null && signatures.Length > 0)
            {
                if (!result.ToString().EndsWith(Environment.NewLine)) result.Append(Environment.NewLine);
                if (returnSpin)
                {
                    if (signatures.Length > 1) result.Append('{');
                    for (int i = 0; i < signatures.Length; i++)
                    {
                        if (i > 0) result.Append('|');
                        result.Append(signatures[i]);
                    }
                    if (signatures.Length > 1) result.Append('}');
                }
                else
                {
                    int index = Rand.Next(signatures.Length);
                    result.Append(signatures[index]);
                }
                result.Append(Environment.NewLine);
            }

            // trigger rewrite complete event
            if (RewriteProgress != null) RewriteProgress(100f);
            if (RewriteComplete != null) RewriteComplete(null, EventArgs.Empty);

            Trace.WriteLine(guid + ":Adding synonyms complete");
            return result.ToString();
        }

        // spin the given paragraph
        private static StringBuilder spinParagraph(ref int paraLengthTotal, string p, string guid, float totalChars,
            bool sentenceRewrite, QualityRating wordQuality,
            QualityRating phraseQuality,
            PosTagMatchType posMatch,
            InstantUniqueType instantUnique,
            bool checkGrammar, bool phraseSpin, string language,
            IEnumerable<string> protectedTerms, bool returnSpin, int replaceFq,
            bool excludeOrig, bool dontSpinExistingSpin, bool spinTidy,
            int maxSyns, int instantUniqueFq, bool inhibitSpinProperNoun)
        {
            int completeChars = paraLengthTotal;
            paraLengthTotal += p.Length + 1;
            try
            {
                // the following allows us to modify a readonly variable
                string sourceParagraph = p;

                // rewrite each sentence in the source paragraph using the rules engine
                if (sentenceRewrite)
                    sourceParagraph = RewriteRules.RewriteParagraph(language, sourceParagraph, returnSpin);

                // get a list of ranges containing the protected terms
                List<int> protectedIndexes = getProtectedIndexes(sourceParagraph, protectedTerms, dontSpinExistingSpin);

                // this algorithm will first copy the entire source paragraph to the destination (spun) paragraph 
                // and will then parse over the source text and replace words or phrases in the destination
                // text. the source loactions of each tag in the source text is recorded and a destination offset
                // is maintained so that the words are replaced in the correct location. replacing is the best
                // method here (even though it may seem like "building from scratch" would be more efficient. the
                // reason is that if we replace, we keep all existing formatting, spacing, html replacementTags etc that aren't
                // converted into replacementTags.
                var resultParagraph = new StringBuilder(sourceParagraph);

                // derive the instant unique character set
                ConcurrentDictionary<char, List<string>> charSet = (instantUnique == InstantUniqueType.Best
                    ? instantUniqueCharSet
                    : (instantUnique == InstantUniqueType.Full ? instantUniqueFullCharSet : null));
                if (instantUniqueFq <= 0) instantUniqueFq = 1;

                // apply instant unique if no spin and instant unique enabled
                if (wordQuality == QualityRating.None && phraseQuality == QualityRating.None)
                {
                    createUniqueString(resultParagraph, charSet, instantUniqueFq, returnSpin, protectedIndexes);
                    if (RewriteProgress != null) RewriteProgress(Math.Min(100, (paraLengthTotal / totalChars) * 100f));
                }

                    // apply spin
                else
                {
                    // now split each paragraph into sentences
                    string[] sourceSentences = PartOfSpeech.GetSentences(language, sourceParagraph);

                    // the below variable is used to keep track of the number of the difference between the source and 
                    // destination pointers. the pointers will no longer equal when text is inserted or removed by the
                    // spin process. the below variable is the current difference in length between the modified text and 
                    // the source text.
                    int destOffset = 0;

                    // the variable below keeps track of the start location of the sentence within the paragraph. 
                    int sourceOffset = 0;

                    // spin each sentence
                    foreach (string sourceSentence in sourceSentences)
                    {
                        if (WasCancelled) return resultParagraph;
                        try
                        {
                            // strip html replacementTags from the source sentence
                            string strippedSentence = PartOfSpeech.StripHtmlTags(sourceSentence);

                            // split each sentence into words, replacementTags and locations
                            sourceOffset = sourceParagraph.IndexOf(sourceSentence, sourceOffset,
                                StringComparison.Ordinal);
                            string[] sourceTokens = PartOfSpeech.GetTokens(language, strippedSentence);
                            int[] sourceLocations = PartOfSpeech.GetLocations(sourceSentence, sourceTokens,
                                sourceOffset);
                            string[] sourceTags = posMatch == PosTagMatchType.None
                                ? null
                                : PartOfSpeech.GetTags(language, sourceTokens);

                            // to maximize use of phrase replacements, this algorithm will always start looking at the maximum
                            // word count and will reduce the word count if a synonym is not found. to reduce complexity in this
                            // code, the api spin feature does not support "spin in spin" - that is, it will only add syns
                            // to a maximum spin depth of one.
                            for (int i = 0; i < sourceTokens.Length; i++)
                            {
                                // tag not found
                                if (sourceLocations[i] < 0) continue;

                                for (int j = Math.Min(i + MAX_PHRASE_LENGTH, sourceTokens.Length - 1);
                                    j >= i;
                                    j--)
                                {
                                    try
                                    {
                                        // tag not found
                                        if (sourceLocations[j] < 0) continue;

                                        // stop one single word if phrase spin selected
                                        bool isSourcePhrase = j > i;
                                        if (phraseSpin && !isSourcePhrase) continue;

                                        // continue if phrase contains a protected term
                                        if (protectedIndexes != null && protectedIndexes.Count > 0)
                                        {
                                            bool isProtected = false;
                                            for (int k = i; k <= j; k++)
                                            {
                                                if (protectedIndexes.Contains(sourceLocations[k]))
                                                {
                                                    isProtected = true;
                                                    break;
                                                }
                                            }
                                            if (isProtected) continue;
                                        }

                                        // stop if replace frequency not met
                                        if (replaceFq > 1 && Rand.Next(replaceFq) != 0) continue;

                                        // stop if proper noun found and spinning of proper nouns is inhibited
                                        if (inhibitSpinProperNoun && sourceTags != null)
                                        {
                                            bool inhibit = false;
                                            for (int k = i; k <= j; k++)
                                            {
                                                if (!PartOfSpeech.IsProperNoun(language, sourceTags[k])) continue;
                                                inhibit = true;
                                                break;
                                            }
                                            if (inhibit) continue;
                                        }

                                        // extract the word run phrase
                                        int sourceTermStartPos = sourceLocations[i];
                                        int sourceTermLength = sourceLocations[j] - sourceLocations[i] +
                                                               sourceTokens[j].Length;
                                        string sourceTerm = sourceParagraph.Substring(sourceTermStartPos,
                                            sourceTermLength);

                                        // search for synonym
                                        CaseCode caseCode = DetermineCaseCode(language, sourceTerm, strippedSentence);
                                        Dictionary<string, FullSynonymInfo> syns = Thesaurus.GetSynonymDetails(
                                            language, sourceTerm, caseCode);

                                        // determine if any synonyms in the list are fit for purpose
                                        var replacements = new List<string>();
                                        string termBefore = i > 0 ? sourceTokens[i - 1] : String.Empty;
                                        string termAfter = j < sourceTokens.Length - 1
                                            ? sourceTokens[j + 1]
                                            : String.Empty;
                                        if (syns != null && syns.Count > 0)
                                        {
                                            foreach (var syn in syns)
                                            {
                                                // if word before or after the term is in the sysnonym then don't use the synonym
                                                if (spinTidy &&
                                                    (String.Equals(syn.Key, sourceTerm,
                                                        StringComparison.OrdinalIgnoreCase) ||
                                                     String.Equals(syn.Key, termBefore,
                                                         StringComparison.OrdinalIgnoreCase) ||
                                                     String.Equals(syn.Key, termAfter,
                                                         StringComparison.OrdinalIgnoreCase))) continue;

                                                // don't add the synonym if it already exists
                                                if (
                                                    replacements.Any(
                                                        replacement =>
                                                            String.Equals(syn.Key, replacement,
                                                                StringComparison.OrdinalIgnoreCase))) continue;

                                                // ignore bogus synonyms
                                                if (spinTidy && syn.Key.Contains("|")) continue;

                                                // ignore syn if phrase spin and syn is not a phrase
                                                bool isSynPhrase = syn.Key.Contains(" ");
                                                if (phraseSpin && !isSynPhrase) continue;

                                                // ignore phrases that end in " a" or " an". this is ok as most phrases that end
                                                // in a or an also contain a lesser variant that does not include the determiner.
                                                if (spinTidy && language == "en" &&
                                                    (syn.Key.EndsWith(" a", true, CultureInfo.InvariantCulture) ||
                                                     syn.Key.EndsWith(" an", true, CultureInfo.InvariantCulture) ||
                                                     sourceTerm.EndsWith(" a", true, CultureInfo.InvariantCulture) ||
                                                     sourceTerm.EndsWith(" an", true, CultureInfo.InvariantCulture)))
                                                    continue;

                                                // evaluate each synonym
                                                foreach (var kvp in syn.Value.Info)
                                                {
                                                    // check part of speech
                                                    if (!isSourcePhrase && sourceTags != null)
                                                    {
                                                        PosTagMatchType match = PartOfSpeech.TagMatchType(language,
                                                            PartOfSpeech.ToInt(language, sourceTags[i]),
                                                            kvp.Value.Pos);
                                                        if (match < posMatch) continue;
                                                    }

                                                    // don't use synonym if quality rating is set to none
                                                    if ((isSourcePhrase && isSynPhrase &&
                                                         phraseQuality == QualityRating.None) ||
                                                        ((!isSourcePhrase || !isSynPhrase) &&
                                                         wordQuality == QualityRating.None)) continue;

                                                    // check quality
                                                    if ((isSourcePhrase && isSynPhrase &&
                                                         kvp.Value.Quality < phraseQuality) ||
                                                        kvp.Value.Quality < wordQuality) continue;

                                                    // check grammar of replaced synonym to ensure grammar is maintained
                                                    if (checkGrammar && posMatch != PosTagMatchType.None &&
                                                        !isPosMatch(language, sourceSentence, sourceTags,
                                                            sourceTermStartPos - sourceOffset, sourceTermLength,
                                                            i, j, syn.Key, posMatch)) continue;

                                                    // add synonym to the list
                                                    replacements.Add(syn.Key);
                                                    break;
                                                }

                                                // exit if maximum synonyms reached
                                                if (maxSyns > 0 && replacements.Count >= maxSyns) break;
                                            }
                                        }

                                        // continue to next word or phrase if no viable synonyms found
                                        if (replacements.Count <= 0 && (charSet == null || isSourcePhrase))
                                            continue;
                                        string replacementText;

                                        // add instant unique strings
                                        if (replacements.Count <= 0)
                                            replacementText =
                                                createUniqueString(new StringBuilder(sourceTerm), charSet,
                                                    instantUniqueFq, returnSpin).ToString();
                                        // spin
                                        else
                                        {
                                            // add orginal word to the list of available synonyms
                                            if (!excludeOrig)
                                            {
                                                replacements.Insert(0,
                                                    charSet == null
                                                        ? sourceTerm
                                                        : createUniqueString(new StringBuilder(sourceTerm),
                                                            charSet, instantUniqueFq,
                                                            returnSpin).ToString());
                                            }

                                            // check if word before syn is an "a" or "an" and include in spin/replacement if it is
                                            if (spinTidy && language == "en" &&
                                                (String.Equals(termBefore, "a",
                                                    StringComparison.InvariantCultureIgnoreCase) ||
                                                 String.Equals(termBefore, "an",
                                                     StringComparison.InvariantCultureIgnoreCase)))
                                            {
                                                // include "a" or "an" in replacement
                                                sourceTermStartPos = sourceLocations[i - 1];
                                                sourceTermLength = sourceLocations[j] - sourceLocations[i - 1] +
                                                                   sourceTokens[j].Length;
                                                var tidyPhrase = sourceSentence.Substring(sourceTermStartPos, sourceTermLength);
                                                if (tidyPhrase.IndexOfAny(new[] { '{', '|', '}' }) >= 0) continue;
                                                // add to all replacements
                                                for (int k = 0; k < replacements.Count; k++)
                                                {
                                                    if (
                                                        replacements[k].StartsWith("a ",
                                                            StringComparison.CurrentCultureIgnoreCase) ||
                                                        replacements[k].StartsWith("an ",
                                                            StringComparison.CurrentCultureIgnoreCase))
                                                        continue;
                                                    if ("aeiou".IndexOf(Char.ToLower(replacements[k][0])) >= 0)
                                                        replacements[k] =
                                                            Thesaurus.GenerateCaseString("an ", caseCode) +
                                                            replacements[k];
                                                    else
                                                        replacements[k] =
                                                            Thesaurus.GenerateCaseString("a ", caseCode) +
                                                            replacements[k];
                                                }
                                            }

                                            // create replacement string
                                            if (!returnSpin)
                                            {
                                                // replace existign term with the synonym
                                                replacementText = replacements[Rand.Next(replacements.Count)];
                                            }
                                            else
                                            {
                                                // create spintax
                                                var sb = new StringBuilder();
                                                foreach (string replacement in replacements)
                                                {
                                                    if (sb.Length > 0) sb.Append("|");
                                                    sb.Append(replacement);
                                                }
                                                if (replacements.Count > 1)
                                                {
                                                    sb.Insert(0, '{');
                                                    sb.Append('}');
                                                }
                                                replacementText = sb.ToString();
                                            }
                                        }


                                        // replace the source text
                                        try
                                        {
                                            resultParagraph.Remove(sourceTermStartPos + destOffset,
                                                sourceTermLength);
                                            resultParagraph.Insert(sourceTermStartPos + destOffset,
                                                replacementText);
                                        }
                                        catch (Exception ex)
                                        {
                                            Debug.WriteLine("!!! Rewrite.GlobalSynonyms:" + guid +
                                                            ":Replacement fail :" + ex.Message);
                                        }

                                        // calculate offset to ensure modified text is always inserted into the correct location
                                        destOffset += replacementText.Length - sourceTermLength;

                                        // move word pointer to the start of the next word
                                        i = j;
                                        break;
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine("!!! Rewrite.GlobalSynonyms:" + guid +
                                                        ":Sentence phrase fail :" + ex.Message);
                                    }
                                }
                            }

                            // move paragraph offset pointer to the start of the next sentence
                            sourceOffset += sourceSentence.Length;

                            // trigger progress event
                            if (RewriteProgress == null) continue;
                            completeChars += sourceSentence.Length + 1;
                            RewriteProgress(Math.Min(100, (completeChars / totalChars) * 100f));
                            Debug.WriteLine("Complete: " + completeChars + "/" + totalChars + "=" +
                                            Math.Min(100, (completeChars / totalChars) * 100f));
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("!!! Rewrite.GlobalSynonyms:" + guid + ":Sentence spin fail :" +
                                            ex.Message);
                        }
                    }
                }

                // add the spun paragraph to the article
                resultParagraph.Append(Environment.NewLine);
                return resultParagraph;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("!!! Rewrite.GlobalSynonyms:" + guid + ":Paragraph spin fail :" + ex.Message);
            }
            return null;
        }

        /// <summary>
        /// Defines an action that is triggered after each spin group is spun.
        /// </summary>
        /// <param name="text">Current partially spun text</param>
        /// <param name="spinDepth">Spin depth of spin group being spun</param>
        /// <param name="unspunText">Text representing the spin group (ie the jetspin text)</param>
        /// <param name="spunText">Spin group spun text</param>
        /// <param name="startPos">Start position within the current text of the spin group</param>
        /// <param name="sourceStartPos">Start position within the source text of the spin group</param>
        /// <remarks><para>This action is called after the spin group has been spun and inserted into the current text.</para></remarks>
        public delegate void SpinAction(
            StringBuilder text, int spinDepth, string unspunText, string spunText, int startPos, int sourceStartPos);

        /// <summary>
        /// Cleans given text by applying spin tidy algorithgms.
        /// </summary>
        /// <param name="source">Source text to tidy</param>
        /// <param name="count">Number of spin tidy actions</param>
        /// <returns>Parsed and tidy text</returns>
        /// <remarks><para>The following rectifications are performed on the source text:
        /// <list type="bullet">
        /// <item>If all synonyms in spintax start with the same word then the word is moved before the spintax</item>
        /// <item>If all synonyms in spintax end with the same word then the word is moved after the spintax</item>
        /// <item>If the word before or after the spintax is in a synonym then remove the synonym</item>
        /// <item>If word before spintax is "a" or "an" then move into spintax and adjust</item>
        /// </list>
        /// </para></remarks>
        public static string SpinTidy(string source, out int count)
        {
            count = 0;
            if (string.IsNullOrEmpty(source)) return string.Empty;

            var sb = new StringBuilder(source);
            int depth = 0;
            int prevWordOffset = 0;
            int wordOffset = 0;
            int synonymOffset = 0;
            int spintaxOffset = 0;
            var synonyms = new List<string>();
            for (int i = 0; i < sb.Length; i++)
            {
                var ch = sb[i];
                if (char.IsWhiteSpace(ch) && depth == 0)
                {
                    if (wordOffset > 0) prevWordOffset = wordOffset;
                    wordOffset = -1;
                }
                else if (ch == '{')
                {
                    if (depth == 0)
                    {
                        spintaxOffset = i;
                        synonymOffset = i + 1;
                    }
                    depth++;
                }
                else if (ch == '|' && depth == 1)
                {
                    synonyms.Add(sb.ToString(synonymOffset, i - synonymOffset));
                    synonymOffset = i + 1;
                }
                else if (ch == '}')
                {
                    depth--;
                    if (depth != 0) continue;

                    try
                    {
                        // determine synonyms, word before spintax and word after spintax
                        synonyms.Add(sb.ToString(synonymOffset, i - synonymOffset));
                        var wordBefore = prevWordOffset >= 0
                            ? sb.ToString(prevWordOffset, spintaxOffset - prevWordOffset).Trim()
                            : string.Empty;
                        if (prevWordOffset < 0) prevWordOffset = spintaxOffset;
                        int trailingSpace = i >= sb.Length - 2 ? -1 : sb.ToString().IndexOfAny(new[] { ' ', '\r', '\n' }, i + 2);
                        var wordAfter =
                            (trailingSpace > 0
                                ? sb.ToString(i + 2, trailingSpace - i - 2)
                                : sb.ToString(Math.Min(i + 2, sb.Length), sb.Length - Math.Min(i + 2, sb.Length))).Trim();
                        if (wordAfter.Contains("{")) wordAfter = string.Empty;
                        bool modifiedSpin = false;

                        // If word before spintax is "a" or "an" then move into spintax and adjust
                        if (string.Equals(wordBefore, "a", StringComparison.CurrentCultureIgnoreCase) ||
                            string.Equals(wordBefore, "an", StringComparison.CurrentCultureIgnoreCase))
                        {
                            wordBefore = null;
                            for (int j = 0; j < synonyms.Count; j++)
                            {
                                if (synonyms[j].Length <= 0 || synonyms[j].StartsWith("a ", StringComparison.CurrentCultureIgnoreCase) ||
                                    synonyms[j].StartsWith(" an", StringComparison.CurrentCultureIgnoreCase)) continue;
                                if ("aeiou".indexOf(synonyms[j][0]) >= 0)
                                    synonyms[j] = (char.IsUpper(synonyms[j][0]) ? "An " : "an ") + synonyms[j].toLowerCase();
                                else synonyms[j] = (char.IsUpper(synonyms[j][0]) ? "A " : "a ") + synonyms[j].toLowerCase();
                                modifiedSpin = true;
                            }
                        }

                        // If the word before or after the spintax is in a synonym then remove the synonym
                        for (int j = 0; j < synonyms.Count; j++)
                        {
                            if (synonyms[j].Contains("{")) continue;
                            var words = synonyms[j].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (
                                !words.Any(
                                    word =>
                                        (!string.IsNullOrEmpty(wordBefore) &&
                                         string.Equals(wordBefore, word, StringComparison.CurrentCultureIgnoreCase)) ||
                                        (!string.IsNullOrEmpty(wordAfter) && string.Equals(wordAfter, word, StringComparison.CurrentCultureIgnoreCase))))
                                continue;
                            synonyms.RemoveAt(j);
                            j--;
                            modifiedSpin = true;
                        }

                        // If all synonyms in spintax start with the same word then the word is moved before the spintax
                        if (synonyms.Count > 1 && synonyms[0].IndexOf(' ') > 0)
                        {
                            var startWord = synonyms[0].Substring(0, synonyms[0].indexOf(' ') + 1);
                            if (!string.Equals(startWord, "a ", StringComparison.CurrentCultureIgnoreCase) &&
                                !string.Equals(startWord, "an ", StringComparison.CurrentCultureIgnoreCase) &&
                                synonyms.All(t => t.StartsWith(startWord, StringComparison.InvariantCultureIgnoreCase)))
                            {
                                wordBefore = ((wordBefore ?? string.Empty) + " " + startWord).Trim();
                                for (int j = 0; j < synonyms.Count; j++) synonyms[j] = synonyms[j].Substring(startWord.Length + 1).Trim();
                                modifiedSpin = true;
                            }
                        }

                        // If all synonyms in spintax end with the same word then the word is moved after the spintax
                        if (synonyms.Count > 1 && synonyms[0].LastIndexOf(' ') > 0)
                        {
                            var endWord = synonyms[0].Substring(synonyms[0].LastIndexOf(' '));
                            if (synonyms.All(t => t.EndsWith(endWord, StringComparison.InvariantCultureIgnoreCase)))
                            {
                                wordAfter = (endWord + " " + wordAfter).Trim();
                                for (int j = 0; j < synonyms.Count; j++)
                                    synonyms[j] = synonyms[j].Substring(0, synonyms[j].Length - wordAfter.Length).Trim();
                                modifiedSpin = true;
                            }
                        }

                        // update modified spin
                        if (modifiedSpin)
                        {
                            var spin = new StringBuilder();
                            foreach (var synonym in synonyms)
                            {
                                if (spin.Length > 0) spin.Append('|');
                                spin.Append(synonym);
                            }
                            if (synonyms.Count > 1)
                            {
                                spin.Insert(0, '{');
                                spin.Append("}");
                            }
                            if (!string.IsNullOrEmpty(wordBefore)) spin.Insert(0, wordBefore + " ");
                            if (!string.IsNullOrEmpty(wordAfter)) spin.Append(" " + wordAfter);
                            sb.Remove(prevWordOffset, i - prevWordOffset + 1);
                            sb.Insert(prevWordOffset, spin);
                            i = prevWordOffset + spin.Length;
                            count++;
                        }
                    }
                    catch
                    {
                    }

                    // reset for next
                    synonyms.Clear();
                    wordOffset = i + 1;
                }
                else if (wordOffset < 0 && !char.IsWhiteSpace(ch)) wordOffset = i;
            }

            return sb.ToString();
        }

        /// <summary>
        ///     Spin and return given source text.
        /// </summary>
        /// <param name="source">Source text to spin</param>
        /// <param name="spinType">Spin type</param>
        /// <param name="orderedSpinSynonymNumber">
        ///     Synonym number used for ordered spin (0=Original, 1=First synonym, 2=Second
        ///     synonym etc)
        /// </param>
        /// <param name="excludeOrig">TRUE to exclude original word from spin to maximize uniqueness</param>
        /// <param name="replacementTags">List of replacement tags</param>
        /// <param name="signatures">List of post append signatures</param>
        /// <param name="maxSpintaxDepth">Set maximum spin depth for respin action (0=unlimited)</param>
        /// <param name="spintaxTags">Spintax replacement tags</param>
        /// <param name="removeSpecialTags">Set TRUE to remove special spintax tags such as spin protection markers</param>
        /// <param name="reorderSpinVariations">Sets the number of variations for reorder spin spintax if Respin spin type is enabled</param>
        /// <param name="reportProgress">Set TRUE to trigger progress events</param>
        /// <param name="action">Action to run after each spin group has been spun</param>
        /// <returns>Spun text</returns>
        /// <remarks><para>The <c>spintaxTags</c> parameter is a two dimesional array with the first dimension representing
        /// the spin depth with the second containing exactly 3 elements representing the spintax open tag, spintax separator
        /// tag and spintax close tag respectively. The highest numbered first dimension element will be used for all spintax
        /// depths greater than the element count.</para>
        /// <para>This <c>maxSpintaxDepth</c> and <c>spintaxTags</c> parameters are only be used if <c>SpinType.Respin</c> is 
        /// selected.</para>
        /// <para>Set <c>reorderSpinVariations</c> to 0 or less to preserve spin reorder syntax.</para>
        /// </remarks>
        public static string SpinText(string source, SpinType spinType = SpinType.Random,
            int orderedSpinSynonymNumber = -1,
            bool excludeOrig = false, Dictionary<string, List<string>> replacementTags = null, string[] signatures = null,
            int maxSpintaxDepth = 0,
            string[,] spintaxTags = null, int reorderSpinVariations = 1, bool removeSpecialTags = false,
            bool reportProgress = false, SpinAction action = null)
        {
            const string PIPE_REPLACE = "@_PIPE_@";

            // reset cancel flag
            WasCancelled = false;

            // override ordered spin synonym number if random spin selected
            if (spinType == SpinType.Random || orderedSpinSynonymNumber < 0) orderedSpinSynonymNumber = Rand.Next(5000);

            string result;
            try
            {
                // report spin start
                if (reportProgress)
                {
                    if (RewriteStarted != null) RewriteStarted(null, EventArgs.Empty);
                    if (RewriteProgress != null) RewriteProgress(0f);
                }

                // move source into a string builder for speedy manipulation
                var sb = new StringBuilder(source);

                // add replacement tags
                if (replacementTags != null && replacementTags.Count > 0)
                {
                    foreach (var tag in replacementTags)
                    {
                        if (string.IsNullOrEmpty(tag.Key) || tag.Value == null || tag.Value.Count == 0) continue;
                        var tagSpin = new StringBuilder();
                        if (tag.Value.Count > 1) tagSpin.Append("~{");
                        for (int i = 0; i < tag.Value.Count; i++)
                        {
                            if (i > 0) tagSpin.Append('|');
                            tagSpin.Append(tag.Value[i]);
                        }
                        if (tag.Value.Count > 1) tagSpin.Append('}');
                        sb.Replace(string.Format(@"#{0}#", tag.Key), tagSpin.ToString());
                    }
                }

                // add signatures
                if (signatures != null && signatures.Length > 0)
                {
                    if (!source.EndsWith(Environment.NewLine)) sb.Append(Environment.NewLine);
                    if (signatures.Length > 1) sb.Append("~{");
                    for (int i = 0; i < signatures.Length; i++)
                    {
                        if (i > 0) sb.Append('|');
                        sb.Append(signatures[i]);
                    }
                    if (signatures.Length > 1) sb.Append('}');
                }

                // remove protection markers
                if (removeSpecialTags) sb.Replace("##", string.Empty);

                // store values for progress report
                float currentProgress = 0f;
                float totalChars = sb.Length;
                int offset = 0;

                // retrieve spintax from the source article. we use a stack here to ensure that we spin inside out.
                var spintaxStack = new Stack<int>();
                for (int i = 0; i < sb.Length; i++)
                {
                    if (WasCancelled) return source;

                    // find matching spintax replacementTags
                    if (sb[i] == '{') spintaxStack.Push(i);
                    if (sb[i] != '}' || spintaxStack.Count == 0) continue;

                    // retrieve the spintax
                    int spintaxDepth = spintaxStack.Count;
                    int startPos = spintaxStack.Pop();
                    try
                    {
                        int sourceLength = i - startPos + 1;
                        string spintax = sb.ToString(startPos, sourceLength);

                        // get all synonyms from the spintax
                        var synonyms = new List<string>();
                        var word = new StringBuilder();
                        bool hasSynonym = false;

                        // needed to ensure an empty syn is not added when spintax found and no synonyms
                        var synDepth = 0;
                        foreach (char ch in spintax)
                        {
                            if (ch == '{') synDepth++;
                            else if (ch == '}') synDepth--;

                            if ((ch == '{' && synDepth == 1) || (ch == '}' && synDepth == 0)) continue;
                            if (ch == '|' && synDepth == 1)
                            {
                                synonyms.Add(word.ToString());
                                word = new StringBuilder();
                                hasSynonym = true;
                            }
                            else word.Append(ch);
                        }
                        if (hasSynonym) synonyms.Add(word.ToString());

                        // determine if ordered spin override enabled
                        bool orderedOverride = startPos > 0 && sb[startPos - 1] == '~';
                        bool nSpin = startPos > 0 && sb[startPos - 1] == '!';
                        if (orderedOverride || nSpin)
                        {
                            startPos--;
                            sourceLength++;
                        }

                        // remove the original synonym
                        if (spinType != SpinType.None && ((excludeOrig && !orderedOverride && !nSpin && synonyms.Count > 1) ||
                            (spinType == SpinType.Respin && excludeOrig && synonyms.Count > 0))) synonyms.RemoveAt(0);

                        //select replacement string
                        string replacement;
                        if (spinType == SpinType.None) replacement = null;
                        else if (nSpin)
                        {
                            if (synonyms.Count > 1)
                            {
                                if (reorderSpinVariations < 1 && spinType == SpinType.Respin) replacement = sb.ToString(startPos, sourceLength);
                                else
                                {
                                    // get nspin control parameters in format (all parameters are optional):
                                    //    [separator][:[last separator][:[minimum list entries][:[maximum list entries]]]]]]
                                    var control = synonyms[0].Split(new[] { ':' });
                                    var separator = control.Length > 0 ? control[0] : string.Empty;
                                    var lastSeparator = control.Length > 1 ? control[1] : separator;
                                    int min = -1;
                                    if (control.Length > 2) int.TryParse(control[2], out min);
                                    int max = -1;
                                    if (control.Length > 3) int.TryParse(control[3], out max);
                                    synonyms.RemoveAt(0);

                                    // create variations
                                    if (reorderSpinVariations == 1 || spinType != SpinType.Respin)
                                    {
                                        replacement = ReoderSpin(synonyms, separator, lastSeparator, min, max);
                                    }
                                    else
                                    {
                                        var variations = new List<string>();
                                        for (int j = 0; j < reorderSpinVariations * 10; j++)
                                        {
                                            var variant = ReoderSpin(synonyms, separator, lastSeparator, min, max);
                                            if (maxSpintaxDepth > 0 && spintaxDepth == maxSpintaxDepth && variant.Contains("{"))
                                                variant = SpinText(variant);
                                            if (variations.Contains(variant)) continue;
                                            variations.Add(variant);
                                            if (variations.Count >= reorderSpinVariations) break;
                                        }
                                        if (variations.Count == 0) replacement = string.Empty;
                                        else if (variations.Count == 1) replacement = variations[0];
                                        else
                                        {
                                            var r = new StringBuilder();
                                            int tagDepth = spintaxTags == null
                                                ? 0
                                                : (spintaxDepth > spintaxTags.GetLength(0) ? spintaxTags.GetLength(0) : spintaxDepth) - 1;
                                            foreach (var variant in variations)
                                            {
                                                if (r.Length > 0) r.Append(spintaxTags == null ? "|" : spintaxTags[tagDepth, 1]);
                                                r.Append(variant);
                                            }
                                            replacement = (spintaxTags == null ? "{" : spintaxTags[tagDepth, 0]) + r.toString() +
                                                          (spintaxTags == null ? "}" : spintaxTags[tagDepth, 2]);
                                        }
                                    }
                                }
                            }
                            else replacement = string.Empty;
                        }
                        else if (spinType != SpinType.Respin || orderedOverride)
                        {
                            // select the synonym
                            int index = (spinType == SpinType.Random && !orderedOverride)
                                ? Rand.Next(synonyms.Count)
                                : (synonyms.Count == 0 ? 0 : orderedSpinSynonymNumber % synonyms.Count);
                            replacement = synonyms[index];
                        }
                        else
                        {
                            // reconstruct spin
                            if (synonyms.Count == 0) replacement = string.Empty;
                            else if (synonyms.Count == 1) replacement = synonyms[0];
                            else if (maxSpintaxDepth > 0 && spintaxDepth > maxSpintaxDepth) continue;
                            else
                            {
                                // flatten spin depth
                                if (maxSpintaxDepth > 0 && spintaxDepth == maxSpintaxDepth)
                                {
                                    var existingSpin = sb.ToString(startPos, sourceLength);
                                    if (existingSpin.Contains("{")) synonyms = flattenSpin(existingSpin);
                                }

                                // determine spin tags
                                var currentSpintaxTags = new[] { "{", "|", "}" };
                                if (spintaxTags != null && spintaxTags.GetLength(0) > 0 && spintaxTags.GetLength(1) == 3)
                                {
                                    int tagDepth = (spintaxDepth > spintaxTags.GetLength(0) ? spintaxTags.GetLength(0) : spintaxDepth) - 1;
                                    currentSpintaxTags[0] = spintaxTags[tagDepth, 0].Replace("|", PIPE_REPLACE);
                                    currentSpintaxTags[1] = spintaxTags[tagDepth, 1].Replace("|", PIPE_REPLACE);
                                    currentSpintaxTags[2] = spintaxTags[tagDepth, 2].Replace("|", PIPE_REPLACE);
                                }

                                // create spin
                                var sbRo = new StringBuilder();
                                foreach (string syn in synonyms)
                                {
                                    if (sbRo.Length > 0) sbRo.Append(currentSpintaxTags[1]);
                                    sbRo.Append(syn);
                                }
                                replacement = string.Format("{0}{1}{2}", currentSpintaxTags[0], sbRo, currentSpintaxTags[2]);
                            }
                        }

                        // replace the spin
                        if (replacement != null)
                        {
                            sb.Remove(startPos, sourceLength);
                            sb.Insert(startPos, replacement);
                            i += replacement.Length - sourceLength;
                        }

                        // trigger spin action
                        if (action != null)
                            action(sb, spintaxStack.Count + 1, spintax, replacement ?? sb.ToString(startPos, sourceLength), startPos,
                                startPos + offset);
                        if (replacement != null) offset += sourceLength - replacement.Length;

                        // report progress
                        if (reportProgress && RewriteProgress != null && i + sourceLength > currentProgress)
                        {
                            currentProgress = i + sourceLength;
                            RewriteProgress(Math.Min(100, (currentProgress / totalChars) * 100f));
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("!!! Rewrite.SpinText: Single spin failed :" + ex.Message);
                    }
                }
                result = sb.ToString().replace(PIPE_REPLACE, "|");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("!!! Rewrite.SpinText:Failed to spin :" + ex.Message);
                result = source;
            }

            // report spin complete
            if (reportProgress)
            {
                if (RewriteProgress != null) RewriteProgress(100f);
                if (RewriteComplete != null) RewriteComplete(null, EventArgs.Empty);
            }

            return result;
        }

        /// <summary>
        /// Returns a string representation of a randomly ordered list of values.
        /// </summary>
        /// <param name="values">List of values to reorder</param>
        /// <param name="separator">Separator to include between each value (space if omitted)</param>
        /// <param name="lastSeparator">Separator to include before the last value (same as <c>separator</c> if omitted)</param>
        /// <param name="min">Minimum number of values to include in the list (1 if omitted)</param>
        /// <param name="max">Maximum number of values to include in the list (all if omitted)</param>
        /// <returns>A string representation of a randomly ordered list of values</returns>
        public static string ReoderSpin(List<string> values, string separator = null, string lastSeparator = null, int min = -1, int max = -1)
        {
            if (values == null || values.Count == 0) return string.Empty;
            var list = new List<string>(values);

            // configure parameters
            if (min < 1) min = list.Count;
            if (max < 1) max = list.Count;
            max = Math.Max(min, max);
            if (string.IsNullOrEmpty(separator)) separator = " ";
            else if (string.Equals(separator, "\\n", StringComparison.InvariantCultureIgnoreCase)) separator = Environment.NewLine;
            else if (!char.IsWhiteSpace(separator[separator.Length - 1])) separator += " ";
            if (!char.IsWhiteSpace(separator[0]) && !separator.StartsWith(",")) separator = " " + separator;
            if (string.IsNullOrEmpty(lastSeparator)) lastSeparator = separator;
            else if (!char.IsWhiteSpace(lastSeparator[lastSeparator.Length - 1])) lastSeparator += " ";
            if (!char.IsWhiteSpace(lastSeparator[0]) && !lastSeparator.StartsWith(",")) lastSeparator = " " + lastSeparator;

            // calculate number of list items
            var remainingItems = Rand.Next(min, max + 1);

            // randomly add items
            var sb = new StringBuilder();
            while (list.Count > 0 && remainingItems > 0)
            {
                if (sb.Length > 0) sb.Append(list.Count == 1 || remainingItems == 1 ? lastSeparator : separator);
                var currItem = Rand.Next(list.Count);
                sb.Append(list[currItem]);
                list.RemoveAt(currItem);
                remainingItems--;
            }
            return sb.toString();
        }

        // return a list of all resulting spin from the given text
        private static List<string> flattenSpin(string text)
        {
            var list = new List<string>();

            // below is temporary and not the most efficient way to do this, but maybe it will do
            int missedCount = 0;
            for (int i = 0; i < 5000; i++)
            {
                var result = SpinText(text);
                if (!list.Contains(result)) list.Add(result);
                else missedCount++;
                if (missedCount > 100) break;
            }
            return list;
        }

        // performs two checks:
        // 1) check that the replacement synonym has the same POS as the original word
        // 2) check if the POS of all words other than the replacement term are equal after the replacement is applied
        private static bool isPosMatch(string language, string sourceSentence, string[] sourceTags,
            int sourceTermStartPos,
            int sourceTermLength, int i, int j, string synonym, PosTagMatchType matchType)
        {
            try
            {
                // create replacement sentence
                string replacementSentence = sourceSentence.Remove(sourceTermStartPos, sourceTermLength)
                    .Insert(sourceTermStartPos, synonym);

                // derive part of speech replacementTags
                string strippedSentence = PartOfSpeech.StripHtmlTags(replacementSentence);
                string[] replacementTokens = PartOfSpeech.GetTokens(language, strippedSentence);
                string[] replacementTags = PartOfSpeech.GetTags(language, replacementTokens);

                // check that the synonym before the insertion point remain the same
                for (int k = 0; k < i; k++)
                    if (!PartOfSpeech.TagMatch(language, sourceTags[k], replacementTags[k], matchType, true))
                        return false;

                // check the synonyms after the insertion point. if replacing a word with a word, also check the replaced word matches the pos.
                int diff = replacementTags.Length - sourceTags.Length;
                //for (int k = j + ((diff == 0 && i == j) ? 0 : 1); k < sourceTags.Length; k++) if (!PartOfSpeech.TagMatch(language, sourceTags[k], replacementTags[k + diff], matchType, true)) return false;
                for (int k = j + 1; k < sourceTags.Length; k++)
                    if (!PartOfSpeech.TagMatch(language, sourceTags[k], replacementTags[k + diff], matchType, true))
                        return false;

                // check that at least one of the replaced words is a loose POS match when replacing a single word with a phrase
                if (diff > 0 && i == j)
                {
                    bool isLooseMatch = false;
                    for (int k = i; k <= i + diff; k++)
                        if (PartOfSpeech.TagMatch(language, sourceTags[i], replacementTags[k], PosTagMatchType.Loose))
                            isLooseMatch = true;
                    if (!isLooseMatch) return false;
                }

                    // check that at least one of the replace words is a loose POS match when replacing a phrase with a single word
                else if (j > i && synonym.Length - synonym.Replace(" ", string.Empty).Length == 0)
                {
                    for (int k = i; k <= j; k++)
                        if (PartOfSpeech.TagMatch(language, sourceTags[k], replacementTags[i], PosTagMatchType.Loose))
                            return true;
                    return false;
                }

                // match found
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("!!! Rewrite.isPosMatch:Failed to test match :" + ex.Message);
                return false;
            }
        }

        /// <summary>
        ///     Determines the case code for a given word in a given sentence.
        /// </summary>
        /// <param name="lang">Current language ID</param>
        /// <param name="word">Source term</param>
        /// <param name="sentence">Source sentence</param>
        /// <returns>Case code representing the case of the given word</returns>
        public static CaseCode DetermineCaseCode(string lang, string word, string sentence)
        {
            try
            {
                if (String.IsNullOrEmpty(word) || String.IsNullOrEmpty(sentence)) return CaseCode.None;
                TextInfo textInfo = new CultureInfo(lang).TextInfo;
                bool isEnglish = lang == "en";

                if (String.Equals(textInfo.ToLower(word), word, StringComparison.InvariantCulture))
                    return isEnglish ? CaseCode.AllLower : CaseCode.None; // leave alone
                if (String.Equals(textInfo.ToUpper(word), word, StringComparison.InvariantCulture) && word.Length > 1)
                {
                    if (word.Length <= 3) return CaseCode.None; // TLA
                    return CaseCode.AllCaps; // emphasized word
                }
                // first cap
                if (Char.IsUpper(word[0]))
                {
                    if (String.Equals(textInfo.ToTitleCase(sentence), sentence, StringComparison.InvariantCulture))
                        return CaseCode.AllFirstCap; // title
                    if (sentence.IndexOf(word, StringComparison.InvariantCulture) == 0)
                        return CaseCode.FirstCap; // start of sentence
                    // here we need to do some magic - to determine if the word is part of a phrase we check if most words are capitalized. this
                    // accounts for titles with a's and is's etc - short words are often left lower case in english titles
                    int words = sentence.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
                    if (words >= 4)
                    {
                        int capitalizedWords = 0;
                        for (int i = 0; i < sentence.Length; i++)
                            if ((i == 0 || Char.IsWhiteSpace(sentence[i - 1])) && Char.IsLetterOrDigit(sentence[i]) &&
                                Char.IsUpper(sentence[i])) capitalizedWords++;
                        if (words - capitalizedWords <= 2) return CaseCode.AllFirstCap; // psuedo phrase
                    }
                    return word.Equals("I") ? CaseCode.AllLower : CaseCode.None; // leave alone
                }
                // no match - leave alone
                return isEnglish ? CaseCode.AllLower : CaseCode.None;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("!!! Rewrite.determineCaseCode:Failed to test match :" + ex.Message);
                return CaseCode.None;
            }
        }

        // create a list of all protected indexes
        private static List<int> getProtectedIndexes(string source, IEnumerable<string> protectedTerms,
            bool protectExistingSpin)
        {
            try
            {
                var results = new List<int>();
                if (string.IsNullOrEmpty(source)) return results;

                // protect terms
                if (protectedTerms != null)
                {
                    foreach (string term in protectedTerms)
                    {
                        MatchCollection mathches = Regex.Matches(source, string.Format(@"\b{0}\b", term),
                            RegexOptions.IgnoreCase);
                        foreach (Match mathch in mathches)
                            for (int i = mathch.Index; i < mathch.Index + mathch.Length; i++) results.Add(i);
                    }
                }

                // protect existing spin
                if (protectExistingSpin)
                {
                    MatchCollection mathches = Regex.Matches(source, @"\{([^\}])*\}");
                    foreach (Match mathch in mathches)
                        for (int i = mathch.Index; i < mathch.Index + mathch.Length; i++)
                            if (!results.Contains(i)) results.Add(i);
                    mathches = Regex.Matches(source, @"\}([^\}])*\}");
                    foreach (Match mathch in mathches)
                        for (int i = mathch.Index; i < mathch.Index + mathch.Length; i++)
                            if (!results.Contains(i)) results.Add(i);
                }

                // protection delimiters
                MatchCollection ranges = Regex.Matches(source, @"##.([^##]+)##", RegexOptions.IgnoreCase);
                foreach (Match mathch in ranges)
                    for (int i = mathch.Index; i < mathch.Index + mathch.Length; i++) results.Add(i);

                results.Sort();
                return results;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("!!! Rewrite.getProtectedIndexes:Failed to retrieve protected indexes :" + ex.Message);
                return new List<int>();
            }
        }

        // Load instant unique character set into memory
        private static ConcurrentDictionary<char, List<string>> convertCharacterSet(string characterSet)
        {
            try
            {
                string[] charSets = characterSet.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                var cs = new ConcurrentDictionary<char, List<string>>();
                foreach (string charSet in charSets)
                {
                    if (string.IsNullOrEmpty(charSet) || charSet.Length <= 1) continue;
                    char source = charSet[0];
                    string[] chars = charSet.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                    if (chars.Length <= 0) continue;
                    List<string> r = cs.ContainsKey(source) ? cs[source] : new List<string>();
                    for (int i = 1; i < chars.Length; i++) r.Add(chars[i]);
                    cs[source] = r;
                }
                return cs;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("!!! Rewrite.convertCharacterSet:Failed to create charset :" + ex.Message);
                return null;
            }
        }

        // replace character with an instant unique string
        private static StringBuilder createUniqueString(StringBuilder source, IDictionary<char, List<string>> charSet,
            int replaceFq, bool returnSpin,
            ICollection<int> protectedIndexes = null)
        {
            if (source == null || source.Length == 0 || charSet == null) return source;

            try
            {
                int destOffset = 0;
                for (int i = 0; i < source.Length; i++)
                {
                    if (protectedIndexes != null && protectedIndexes.Contains(i - destOffset)) continue;
                    char ch = source[i];

                    // continue if not a replacable character or exceeded replacement frequency
                    if (!charSet.ContainsKey(ch) || (replaceFq > 1 && Rand.Next(replaceFq) != 0)) continue;

                    // create replacement text
                    string replacementText;
                    if (!returnSpin) replacementText = charSet[ch][Rand.Next(charSet[ch].Count)];
                    else
                    {
                        // create spintax
                        var sb = new StringBuilder();
                        sb.Append('{');
                        sb.Append(ch);
                        foreach (string replacement in charSet[ch])
                        {
                            sb.Append('|');
                            sb.Append(replacement);
                        }
                        sb.Append('}');
                        replacementText = sb.ToString();
                    }

                    // replace character with unique string
                    source.Remove(i, 1);
                    source.Insert(i, replacementText);
                    destOffset += replacementText.Length - 1;
                    i += replacementText.Length;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("!!! Rewrite.createUniqueString:Failed to create unique string :" + ex.Message);
            }
            return source;
        }

        #region Events

        /// <summary>
        ///     Rewrite progress event handler.
        /// </summary>
        /// <param name="pctComplete">Percentage complete (0 - 100)</param>
        public delegate void RewriteProgressEventHandler(float pctComplete);

        /// <summary>
        ///     Triggers on rewrite progress.
        /// </summary>
        public static event RewriteProgressEventHandler RewriteProgress;

        /// <summary>
        ///     Triggers after the rewrite has completed.
        /// </summary>
        public static event EventHandler RewriteComplete;

        /// <summary>
        ///     Triggers when a rewrite has started.
        /// </summary>
        public static event EventHandler RewriteStarted;

        #endregion
    }

}