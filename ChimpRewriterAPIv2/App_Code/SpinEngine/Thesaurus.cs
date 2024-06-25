using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ChimpRewriterAPIv3.SpinEngine
{
    public static class Thesaurus
    {
        private const string KEY = "11588085";
        private const string IV = "12508770";

        public static ConcurrentDictionary<string, ConcurrentDictionary<string, SpinChimpThesResult>> Thesauri { get; private set; }

        public static bool Load()
        {
            Trace.WriteLine("Loading thesauri...");
            //var loc = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var loc = AppDomain.CurrentDomain.GetData("DataDirectory").ToString();
            if (String.IsNullOrEmpty(loc))
            {
                Trace.WriteLine("!!! Could not determine thesauri location");
                return false;
            }
            var path = Path.Combine(loc, @"thesauri\");
            Thesauri = new ConcurrentDictionary<string, ConcurrentDictionary<string, SpinChimpThesResult>>();
            foreach (string dir in Directory.EnumerateDirectories(path))
            {
                var info = new DirectoryInfo(dir);
                var lang = info.Name.ToLower();
                Trace.WriteLine("   Found " + lang + " thesaurus directory");
                var thes = load(Path.Combine(dir, "SCThesaurus.dat"), KEY, IV, false);
                if (thes != null) Thesauri[lang] = thes;
            }
            if (Thesauri.Count == 0)
            {
				Trace.WriteLine("!!! Could not find any thesauri");
                return false;
            }
            Trace.WriteLine("Load complete");
            return true;
        }

        public static bool IsLoaded { get { return Thesauri != null && Thesauri.Keys.Count > 0; } }

        /// <summary>
        /// Fetches synonyms from the SpinChimp thesaurus and any loaded local thesaurui
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="word">Lookup word</param>
        /// <param name="matchToCase">Case to match to. e.g. match to title case or start of sentence </param>
        /// <returns>Dictionary of synonyms with bool specifing favourite status</returns>
        public static Dictionary<string, FullSynonymInfo> GetSynonymDetails(string lang, string word, CaseCode matchToCase)
        {
            Dictionary<string, FullSynonymInfo> scresults = GetSynonyms(lang, word, PartOfSpeech.UNKNOWN_TOKEN_ID);

            var results = new Dictionary<string, FullSynonymInfo>();
            foreach (var syn in scresults)
            {
                var key = GenerateCaseString(syn.Key, matchToCase);
                if (!results.ContainsKey(key)) results.Add(key, new FullSynonymInfo(syn.Value));
                else results[key].Combine(syn.Value);
            }

            if (results.Count == 0) return null;
            return results;
        }

        /// <summary>
        /// Gets the synonyms for a word, filtering by quality and ordering by quality
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="word">Lookup word</param>
        /// <param name="pos">POS to match</param>
        /// <returns>Dictionary of synonyms</returns>
        public static Dictionary<string, FullSynonymInfo> GetSynonyms(string lang, string word, int pos)
        {
            SpinChimpThesResult result;
            if (!Thesauri[lang].TryGetValue(word.ToLower(), out result))
            {
                return new Dictionary<string, FullSynonymInfo>();
            }
            var filteredresults = new Dictionary<string, FullSynonymInfo>();
            foreach (var syn in result.Synonyms)
            {
                //If quality is met, and word isn't excluded add to result list
                {
                    // If the syn already exists in the list, see if it has a higher quality. If the new one
                    // has a higher quality, remove the old one and add, otherwise don't add.
                    bool add = true;
                    var matchingResults = filteredresults.FirstOrDefault(pair => pair.Key.ToLower() == syn.Key.ToLower());
                    if (!string.IsNullOrEmpty(matchingResults.Key))
                    {
                        if (matchingResults.Value.OverallScore(lang, pos) < syn.Value.OverallScore(lang, pos))
                            filteredresults.Remove(matchingResults.Key);
                        else
                        {
                            add = false;
                        }
                    }
                    //bool foundSyn = filteredresults.Keys.Any(key => key.ToLower() == syn.Key.ToLower());
                    //if(!foundSyn)
                    if (add)
                    {
                        filteredresults.Add(syn.Key, new FullSynonymInfo(syn.Value, true));
                    }
                }
            }
            return filteredresults;
        }
        
        /// <summary>
        /// Generates a case string for the phrase given the associated case code infomation.
        /// </summary>
        /// <param name="phrase">Phrase to apply case code to</param>
        /// <param name="code">Case code</param>
        /// <returns>The case coded string</returns>
        public static string GenerateCaseString(string phrase, CaseCode code)
        {
            if (code == CaseCode.None) return phrase;
            string caseString = "";
            bool newWord = true;
            bool firstChar = true;
            if (code == (code | CaseCode.AllCaps)) return phrase.ToUpper();
            //If matching to all first cap and word is allcaps, most likely an acronym
            if ((code == (code | CaseCode.AllFirstCap)) && phrase.ToUpper() == phrase) return phrase;
            foreach (char pos in phrase)
            {
                if (Char.IsLetterOrDigit(pos))
                {
                    if (firstChar)
                    {
                        firstChar = false;
                        if (code == (code | CaseCode.FirstCap)) caseString += pos.ToString(CultureInfo.InvariantCulture).ToUpper();
                        else caseString += pos.ToString(CultureInfo.InvariantCulture).ToLower();
                        newWord = false;
                    }
                    else if (newWord)
                    {
                        newWord = false;
                        if (code == (code | CaseCode.AllFirstCap)) caseString += pos.ToString(CultureInfo.InvariantCulture).ToUpper();
                        else caseString += pos.ToString(CultureInfo.InvariantCulture).ToLower();
                    }
                    else caseString += pos.ToString(CultureInfo.InvariantCulture).ToLower();
                }
                else caseString += pos.ToString(CultureInfo.InvariantCulture);
                if (Char.IsWhiteSpace(pos))
                {
                    newWord = true;
                }
            }

            //account for "* i *" always being "* I *" or "* i" for "* I"
            caseString = caseString.Replace(" i ", " I ");
            if (caseString.IndexOf("i ", StringComparison.Ordinal) == 0) caseString = caseString.Replace("i ", "I ");
            if (caseString.IndexOf(" i", StringComparison.Ordinal) == (caseString.Length - 2)) caseString = caseString.Replace(" i", " I");

            return caseString;
        }

        private static ConcurrentDictionary<string, SpinChimpThesResult> load(string filepath, string key, string iv, bool lowMemoryMode)
        {
            // load the thes
            try
            {
                var spinchimpdict = new ConcurrentDictionary<string, SpinChimpThesResult>();
                if (!String.IsNullOrEmpty(filepath) &&
                    (DecryptToLines(spinchimpdict, filepath, key, iv, AddLineToDict, lowMemoryMode) == null &&
                     spinchimpdict.Count > 0))
                {
                    Trace.WriteLine("      Thesaurus loaded");
                    return spinchimpdict;
                }
                Trace.WriteLine(">>> Failed to load thesaurus");
            }
            catch(Exception ex)
            {
                Trace.WriteLine(">>> " + ex.Message);
            }
            return null;
        }

        public static void AddLineToDict(ConcurrentDictionary<string, SpinChimpThesResult> dict, byte[] line, int lineNum, string thes, bool lowMemoryMode)
        {
            var encoding = new UTF8Encoding();
            int currentPos = 0;

            if (line.Length == 0) return;
            if (lineNum == 1)
            {
                //first line is thesaurus info
                //ThesaurusInfo pendingThesInfo;
                //ThesaurusInfo.FromString(Encoding.UTF8.GetString(line), thes, out pendingThesInfo);
                //BaseThesaurusInfo = pendingThesInfo;
                return;
            }
            int wordlength = line[currentPos++];
            var word = encoding.GetString(line, currentPos, wordlength).Trim();

            currentPos += wordlength;
            int numSyns = (line[currentPos++] << 8) | (line[currentPos++] << 0);
            var syns = new Dictionary<string, FullSynonymInfo>();

            for (int i = 1; i <= numSyns; i++)
            {
                int synlength = Convert.ToInt32(line[currentPos++]);
                string synonym = encoding.GetString(line, currentPos, synlength);
                currentPos += synlength;
                int numPos = (line[currentPos++] << 8) | (line[currentPos++] << 0);

                var info = new FullSynonymInfo();
                for (int j = 1; j <= numPos; j++)
                {
                    int pos = (line[currentPos++] << 8) | (line[currentPos++] << 0);
                    int qr = (line[currentPos++] << 8) | (line[currentPos++] << 0);
                    //if (qr < 0x10) return;
                    var qualityRating = (QualityRating)(qr);
                    if (!lowMemoryMode || (qualityRating & QualityRating.All) != QualityRating.All) info.Info.Add(pos, new SynonymInfo(qualityRating, pos));
                    currentPos++; //Flags placeholder
                }

                if (!lowMemoryMode || info.Info.Count > 0) syns.Add(synonym, info);
            }

            //if 0 syns, dont add
            if (syns.Count > 0 && dict != null)
            {
                var results = new SpinChimpThesResult(syns.Keys, syns.Values);
                //lock (_dictionaryLock)
                //{
                //if (!_spinchimpdict.ContainsKey(word.ToLower()))
                //    _spinchimpdict.Add(word.ToLower(), results);
                //}
                if (!dict.ContainsKey(word.ToLower()))
                    dict[word.ToLower()] = results;
                else
                    dict[word.ToLower()].Combine(results);
            }
        }

        public delegate void ReadLine(ConcurrentDictionary<string, SpinChimpThesResult> dict, byte[] line, int lineNum, string fileName, bool lowMemoryMode);

        public static Exception DecryptToLines(ConcurrentDictionary<string, SpinChimpThesResult> dict, string fileName, string key, string iv, ReadLine readLine, bool lowMemoryMode, int maxLines = -1)
        {
            if (!File.Exists(fileName)) throw new FileNotFoundException("Specified file not found");
            Exception success = null;
            try
            {
                //Encryption Setup
                using (var des = new DESCryptoServiceProvider { Key = Encoding.ASCII.GetBytes(key), IV = Encoding.ASCII.GetBytes(iv) }
                    )
                {
                    //Stream Setup
                    using (var inFile = new FileStream(fileName, FileMode.Open, FileAccess.Read))
                    {
                        using (var cs = new CryptoStream(inFile, des.CreateDecryptor(), CryptoStreamMode.Read))
                        {
                            //because CryptoStream doesnt support seeking/length
                            long fileLength = inFile.Length + des.BlockSize - (inFile.Length % des.BlockSize);
                            int currentPos = 0;
                            int lineNum = 0;
                            while (currentPos < fileLength && (maxLines < 0 || lineNum < maxLines))
                            {
                                int linelength = (cs.ReadByte() << 8) | (cs.ReadByte() << 0);
                                if (linelength < 0) break;
                                var line = new byte[linelength];
                                cs.Read(line, 0, linelength);
                                readLine(dict, line, ++lineNum, fileName, lowMemoryMode);
                                currentPos += linelength + 2;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                success = ex;
            }
            return success;
        }
    }

    public struct SpinChimpThesResult
    {
        //private readonly MemoryEncryptedDictionary<FullSynonymInfo> _synonyms;
        private readonly Dictionary<string, FullSynonymInfo> _synonyms;

        #region Constructors

        public SpinChimpThesResult(IEnumerable<string> synonyms, IEnumerable<FullSynonymInfo> info)
        {
            //_synonyms = new MemoryEncryptedDictionary<FullSynonymInfo>(word);
            _synonyms = new Dictionary<string, FullSynonymInfo>();
            var synlist = synonyms.ToList();
            var infolist = info.ToList();
            if (synlist.Count != infolist.Count) throw new ArgumentException("Syns Enumerable and Info Enumerable must be same size");
            string[] synarray = synlist.ToArray();
            FullSynonymInfo[] infoarray = infolist.ToArray();

            for (int i = 0; i < synlist.Count; i++)
            {
                if (!_synonyms.ContainsKey(synarray[i])) _synonyms.Add(synarray[i], new FullSynonymInfo(infoarray[i]));
            }
        }

        public SpinChimpThesResult(string synonym, FullSynonymInfo info)
        {
            var synlist = new Dictionary<string, FullSynonymInfo> { { synonym, new FullSynonymInfo(info) } };
            _synonyms = synlist;
        }

        #endregion

        public Dictionary<string, FullSynonymInfo> Synonyms
        {
            get { return _synonyms; }
        }

        /// <summary>
        /// Extends this SpinChimpThesResult to include the results from another
        /// </summary>
        /// <param name="addition"></param>
        public void Combine(SpinChimpThesResult addition)
        {
            foreach (var syn in addition.Synonyms)
                AddSynonym(syn.Key, syn.Value);
        }

        public void AddSynonym(string synonym, FullSynonymInfo info)
        {
            if (!_synonyms.ContainsKey(synonym))
            {
                _synonyms.Add(synonym, new FullSynonymInfo(info));
            }
        }
    }

    public class FullSynonymInfo
    {
        //private Dictionary<int, SynonymInfo> _syninfo = new Dictionary<int, SynonymInfo>();
        private readonly Dictionary<int, SynonymInfo> _syninfo = new Dictionary<int, SynonymInfo>();

        #region Constructors

        public FullSynonymInfo()
        {
            _syninfo.Clear();
        }

        public FullSynonymInfo(Dictionary<int, SynonymInfo> info)
        {
            _syninfo.Clear();
            foreach (var i in info) _syninfo.Add(i.Key, i.Value);
        }

        public FullSynonymInfo(IEnumerable<SynonymInfo> info)
        {
            _syninfo.Clear();
            foreach (var q in info) if (!_syninfo.ContainsKey(q.Pos)) _syninfo.Add(q.Pos, q);
        }

        public FullSynonymInfo(QualityRating quality)
        {
            _syninfo.Clear();
            _syninfo.Add(0, new SynonymInfo(quality, 0));
        }

        public FullSynonymInfo(FullSynonymInfo info, bool includePOS)
        {
            copy(info, includePOS);
        }

        public FullSynonymInfo(FullSynonymInfo info)
        {
            copy(info, true);
        }

        /*public FullSynonymInfo(FullLocalSynonymInfo localInfo)
        {
            _syninfo.Clear();
            foreach (var i in localInfo.Info)
            {
                _syninfo.Add(i.Key, new SynonymInfo(i.Value.Quality, i.Value.POS));
            }
        }*/

        /// <summary>
        /// Combines another FullSynonymInfo into this one
        /// </summary>
        /// <param name="info">Info to combine in</param>
        public void Combine(FullSynonymInfo info)
        {
            foreach (var i in info.Info)
            {
                if (_syninfo.ContainsKey(i.Key)) _syninfo[i.Key].Quality |= i.Value.Quality;
                else _syninfo.Add(i.Key, new SynonymInfo(i.Value.Quality, i.Value.Pos));
            }
        }

        private void copy(FullSynonymInfo info, bool includePOS)
        {
            _syninfo.Clear();
            if (includePOS)
            {
                foreach (var i in info.Info) _syninfo.Add(i.Key, i.Value);
            }
            else _syninfo.Add(0, new SynonymInfo(info.MaxQualityIgnorePOS, 0));
        }

        #endregion

        #region Get/Set

        public Dictionary<int, SynonymInfo> Info
        {
            get { return _syninfo; }
            set
            {
                _syninfo.Clear();
                foreach (var i in value) _syninfo.Add(i.Key, i.Value);
            }
        }

        public bool KnownPos
        {
            get
            {
                bool result = false;
                foreach (var i in _syninfo)
                {
                    if (i.Key != PartOfSpeech.UNKNOWN_TOKEN_ID)
                    {
                        result = true;
                        break;
                    }
                }
                return result;
            }
        }

        public QualityRating MaxQualityIgnorePOS
        {
            get
            {
                var result = QualityRating.None;
                foreach (var i in _syninfo)
                {
                    if (i.Value.Quality > result) result = i.Value.Quality;
                }
                return result;
            }
        }

        /*public SynScores GetScores(int pos)
        {
            var scores = new SynScores();
            scores.MaxQuality = QualityRating.All;
            scores.OverallScore = 0;
            int firstFullMatch = -1, firstLooseMatch = -1, firstExtremelyLooseMatch = -1;
            foreach (var info in _syninfo)
            {
                if (PartOfSpeech.TagMatchType(pos, info.Key) >= PartOfSpeech.PosTagMatchType.Full)
                {
                    //scores.MaxQuality = info.Value.Quality;
                    firstFullMatch = pos;
                    //First found is what we want
                    break;
                }
                //Only fire if not already found Loose match
                if (firstLooseMatch < 0 && PartOfSpeech.TagMatchType(pos, info.Key) >=
                   PartOfSpeech.PosTagMatchType.Loose)
                {
                    //scores.MaxQuality = info.Value.Quality;
                    firstLooseMatch = info.Key;
                }
                //Only fire if not already found loose or extremely loose match
                if (firstLooseMatch < 0 && firstExtremelyLooseMatch < 0 &&
                   PartOfSpeech.TagMatchType(pos, info.Key) >=
                   PartOfSpeech.PosTagMatchType.ExtremelyLoose)
                {
                    //scores.MaxQuality = info.Value.Quality;
                    firstExtremelyLooseMatch = info.Key;
                }
                int thisScore = (int)info.Value.Quality + (int)PartOfSpeech.TagMatchType(pos, info.Key);
                if (thisScore > scores.OverallScore) scores.OverallScore = thisScore;
            }
            scores.BestPOS = (firstFullMatch > 0
                                ? firstFullMatch
                                : (firstLooseMatch > 0
                                    ? firstLooseMatch
                                    : (firstExtremelyLooseMatch > 0 ? firstExtremelyLooseMatch : 0)));
            if (scores.BestPOS == 0)
            {
                foreach (var otherPos in _syninfo.Where(otherPos => otherPos.Key > 0))
                {
                    scores.BestPOS = otherPos.Key;
                    break;
                }
            }

            scores.MaxQuality = MaxQualityIgnorePOS;

            return scores;
        }

        /// <summary>
        /// Gets the best POS match for the synonym
        /// </summary>
        /// <param name="pos">POS to match to</param>
        /// <returns>Best match for all POS options for this synonym</returns>
        public PartOfSpeech.PosTagMatchType BestPOSMatch(int pos)
        {
            PartOfSpeech.PosTagMatchType bestMatch = PartOfSpeech.PosTagMatchType.None;
            foreach (var s in _syninfo)
            {
                PartOfSpeech.PosTagMatchType thisMatch = PartOfSpeech.TagMatchType(s.Value.POS, pos);
                if (thisMatch > bestMatch)
                {
                    bestMatch = thisMatch;
                }
            }
            return bestMatch;
        } 

        /// <summary>
        /// Returns a numerical weight for the best POS match of the synonym
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        public int BestPOSMatchWeight(int pos)
        {
            return (int)BestPOSMatch(pos);
        }

        public struct SynScores
        {
            public int BestPOS;
            public QualityRating MaxQuality;
            public int OverallScore;
        }*/

        /// <summary>
        /// Weighted total of best POS match and quality for this synonym
        /// </summary>
        /// <param name="lang">Selected language</param>
        /// <param name="pos">POS to match to</param>
        /// <returns>Best score for all POS options for this synonym</returns>
        public int OverallScore(string lang, int pos)
        {
            int bestScore = 0;
            foreach (var s in _syninfo)
            {
                int thisScore = (int)s.Value.Quality + (int)PartOfSpeech.TagMatchType(lang, pos, s.Value.Pos);
                if (thisScore > bestScore) bestScore = thisScore;
            }
            return bestScore;
        }

        #endregion

        public void UpdateWeighting(QualityRating quality, int pos)
        {
            if (_syninfo.ContainsKey(pos)) _syninfo[pos] = new SynonymInfo(quality, pos);
            else _syninfo.Add(pos, new SynonymInfo(quality, pos));
        }

        public void UpdateWeighting(IEnumerable<SynonymInfo> info)
        {
            foreach (var i in info) UpdateWeighting(i.Quality, i.Pos);
        }
    }

    public class SynonymInfo
    {
        public int Pos;
        public QualityRating Quality;

        public SynonymInfo(QualityRating quality, int pos)
        {
            Quality = quality;
            Pos = pos;
        }
    }

    [Flags]
    public enum QualityRating
    {
        ExtendedSyn = 0x2000,
        Favorite = 0x1000,
        UserAdded = 0x800,
        TeamFav = 0x400,
        CommonFav = 0x200,
        Best = 0x100,
        Better = 0x80,
        Good = 0x40,
        Average = 0x20,
        All = 0x10,
        Deleted = 0x8,
        Hated = 0x4,
        None = 0x0
    }

    [Flags]
    public enum CaseCode : short
    {
        None,
        AllLower,
        FirstCap,
        AllFirstCap,
        AllCaps,
        //multiWord = 8
    };
}
