using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using ChimpRewriterAPIv3.SpinEngine;
using log4net;
using SpinEngine;

namespace ChimpRewriterAPIv3.API
{
    public static class APIMethods
    {
        private static readonly ILog errorLogger = LogManager.GetLogger(typeof(APIMethods));

        public static APIResult GlobalSynonyms(string email, string apiKey, string aid, string text, int qual, int phrasequal, int posMatch,
            string language, string protectedTerms, string tagProtect,
            int rewrite, bool sentenceRewrite, bool grammarCheck, bool replacePhrasesWithPhrases, bool reorderParagraphs,
            bool spinTidy, int replaceFrequency, bool excludeOriginal, int maxSyns, bool spinWithinSpin, int maxDepth, int instantUnique,
            out APIUser user, out APIApp app, out int wordCount)
        {
            try
            {
                // Determine query cost
                APIUserAllowance limits;

                int wordLimit = (sentenceRewrite || grammarCheck) ? 500 : 5000;

                wordCount = RequestTools.WordCount(text);
                int queryCost = (int) decimal.Ceiling((wordCount/(decimal) wordLimit));
                if (queryCost == 0) queryCost = 1;

                // Verify call is OK to proceed
                var failureReason = APIUsers.VerifyAPICall(email, apiKey, aid, queryCost,
                                                           out limits, out user, out app);

                if (failureReason.Count < 1)
                {
                    //check article meets constraints                
                    if (wordCount < 1) failureReason.Add("There are no words in your article!");
                }
                //fail if any reason to
                if (failureReason.Count > 0)
                {
                    var failsb = new StringBuilder();
                    for (int i = 0; i < failureReason.Count; i++)
                    {
                        failsb.Append(failureReason[i]);
                        if (i < (failureReason.Count - 1)) failsb.Append("|");
                    }
                    return new APIResult(false, failsb.ToString());
                }

                //Input Checking
                if (string.IsNullOrEmpty(language)) language = "en";

                if (qual > 5) qual = 5;
                if (qual < 1) qual = 1;

                if (posMatch > 4) posMatch = 4;
                if (posMatch < 0) posMatch = 0;

                //Tag protection
                var taglist = new List<string[]>();
                if (!string.IsNullOrEmpty(tagProtect))
                {
                    APITools.ReplaceProtectTags(ref text, tagProtect, out taglist);
                }

                // rewrite count
                if (rewrite > 20) rewrite = 20;
                if (rewrite < 0) rewrite = 0;

                //
                //
                //TODO qual, phrasequal and posmatch are reveresed from the old API. Might cause confusion
                //
                //

                string spunResult = Rewrite.GlobalSynonyms(text, 0, 0, sentenceRewrite, qual, phrasequal, posMatch,
                                                        instantUnique, grammarCheck,
                                                        replacePhrasesWithPhrases,
                                                        language, protectedTerms, rewrite != 1, replaceFrequency,
                                                        excludeOriginal, !spinWithinSpin,
                                                        spinTidy, reorderParagraphs, maxSyns);

                var match = Regex.Match(spunResult, @"\A<error>(.*)</error>\Z", RegexOptions.Singleline);
                if (match.Success)
                {
                    string error = match.Groups.Count > 1 ? match.Groups[1].Value : "";
                    int iError;
                    if (int.TryParse(error, out iError)) error = Rewrite.GetErrorDescription(iError);
                    return new APIResult(false, string.IsNullOrEmpty(error) ? "Unknown rewriting error" : error);
                }

                //Remove tag protection
                if (!string.IsNullOrEmpty(tagProtect))
                    APITools.ReturnProtectTags(ref spunResult, taglist);

                // if multiple rewrites requested, process them
                if (rewrite < 2)
                {
                    return new APIResult(true, spunResult);
                }

                var results = new List<string>();
                var sb = new StringBuilder();
                for (int spinCount = 0; spinCount < rewrite; spinCount++)
                {
                    sb.AppendLine();
                    sb.AppendFormat("===== Version {0} =====", spinCount + 1);
                    sb.AppendLine();
                    sb.Append(Rewrite.SpinText(spunResult, SpinType.Random, 0, excludeOriginal));
                }
                return new APIResult(true, sb.ToString());


            }
            catch (Exception e)
            {
                errorLogger.Error("GlobalSynonyms failed", e);
            }
            app = new APIApp();
            user = new APIUser();
            wordCount = 0;
            return new APIResult(false, "Unknown error, please contact us at http://support.akturatech.com");
        }

        public static APIResult CreateSpin(string email, string apiKey, string aid, string text, bool dontUseOriginal, bool reorderParagraphs,
            out APIUser user, out APIApp app, out int wordCount)
        {
            try
            {
                // Determine query cost
                APIUserAllowance limits;

                int queryCost = 1;
                wordCount = RequestTools.WordCount(text);
                int multiplesOfArticle = (int) decimal.Ceiling((wordCount/(decimal) 5000));
                if (multiplesOfArticle == 0) multiplesOfArticle = 1;
                queryCost += (multiplesOfArticle - 1);

                // Verify call is OK to proceed
                var failureReason = APIUsers.VerifyAPICall(email, apiKey, aid, queryCost,
                                                           out limits, out user, out app);

                if (failureReason.Count < 1)
                {
                    //check article meets constraints                
                    if (wordCount < 1) failureReason.Add("There are no words in your article!");
                }
                //fail if any reason to
                if (failureReason.Count > 0)
                {
                    var failsb = new StringBuilder();
                    for (int i = 0; i < failureReason.Count; i++)
                    {
                        failsb.Append(failureReason[i]);
                        if (i < (failureReason.Count - 1)) failsb.Append("|");
                    }
                    return new APIResult(false, failsb.ToString());
                }

                string spunResult = Rewrite.SpinText(text, SpinType.Random, 0, dontUseOriginal);

                return new APIResult(true, spunResult);
            }
            catch (Exception e)
            {
                errorLogger.Error("CreateSpin failed", e);
            }
            app = new APIApp();
            user = new APIUser();
            wordCount = 0;
            return new APIResult(false, "Unknown error, please contact us at http://support.akturatech.com");
        }

        public static APIStats Statistics(string email, string apikey, string aid)
        {
            APIUserAllowance limits;
            APIUser user;
            APIApp app;
            var failureReason = APIUsers.VerifyAPICall(email, apikey, aid, 0,
                out limits, out user, out app);
            //fail if any reason
            if (failureReason.Count > 0)
            {
                StringBuilder failsb = new StringBuilder();
                for (int i = 0; i < failureReason.Count; i++)
                {
                    failsb.Append(failureReason[i]);
                    if (i < (failureReason.Count - 1)) failsb.Append("|");
                }
                return new APIStats(failsb.ToString());
            }

            return new APIStats(user.ProExpiry >= DateTime.UtcNow.AddDays(5) ? user.ProLimit : 0, 
                user.ProExpiry,
                user.ApiExpiry >= DateTime.UtcNow.AddDays(5) ? user.ApiLimit : 0, 
                user.ApiExpiry, 
                user.UsedToday, user.UsedThisMonth, user.UsedEver);
        }
    }
}