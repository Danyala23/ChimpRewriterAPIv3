using System;
using System.Diagnostics;
using System.Globalization;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;
using System.IO;
using ChimpRewriterAPIv3.API;
using MySql.Data.MySqlClient;
using log4net;
using System.Security.Claims;
using System.Collections.Generic;

namespace ChimpRewriterAPIv3
{
    // NOTE: You can use the "Rename" command on the "Refactor" menu to change the class name "Service1" in code, svc and config file together.
    public class CRAPIService : ICRAPIService
    {
        public APIResult ChimpRewrite(Stream stream)
        {
            var parms = RequestTools.ParseBody(stream);

            var watch = new Stopwatch();
            watch.Start();
            if (WebOperationContext.Current != null)
                WebOperationContext.Current.OutgoingResponse.ContentType = "text/plain";

            //Extract params
            string text = parms["text"];
            string email = parms["email"];
            string apikey = parms["apikey"];
            string aid = parms["aid"]; 
            
            bool sentenceRewrite = APITools.CustomBoolParse(parms["sentencerewrite"], false);
            bool grammarCheck = APITools.CustomBoolParse(parms["grammarcheck"], false);
            int qual = APITools.CustomIntParse(parms["quality"], 4);
            int phrasequal = APITools.CustomIntParse(parms["phrasequality"], 3);
            int posmatch = APITools.CustomIntParse(parms["posmatch"], 3);
            string language = parms["language"];
            int rewrite = APITools.CustomIntParse(parms["rewrite"], 0);
            bool replacePhrasesWithPhrases = APITools.CustomBoolParse(parms["replacephraseswithphrases"],false);
            bool reorderParagraphs = APITools.CustomBoolParse(parms["reorderparagraphs"], false);
            bool spinTidy = APITools.CustomBoolParse(parms["spintidy"], true);
            int replaceFrequency = APITools.CustomIntParse(parms["replacefrequency"], 1);
            bool excludeOriginal = APITools.CustomBoolParse(parms["excludeoriginal"], false);
            int maxSyns = APITools.CustomIntParse(parms["maxsyns"], 10);
            bool spinWithinSpin = APITools.CustomBoolParse(parms["spinwithinspin"], false);
            int maxDepth = APITools.CustomIntParse(parms["maxspindepth"], 0);
            int instantUnique = APITools.CustomIntParse(parms["instantunique"], -1);
            string protectedterms = parms["protectedterms"];
            string tagprotect = parms["tagprotect"];

            // Run method
            APIUser user;
            APIApp app;
            int wordCount;
            var result = APIMethods.GlobalSynonyms(email, apikey, aid, text, qual, phrasequal, posmatch, language, protectedterms,
                                      tagprotect, rewrite, sentenceRewrite, grammarCheck, replacePhrasesWithPhrases,
                                      reorderParagraphs, spinTidy,
                                      replaceFrequency, excludeOriginal, maxSyns, spinWithinSpin, maxDepth,
                                      instantUnique, out user, out app, out wordCount);

            watch.Stop();
            Logging.RequestLog(user.ID, OperationContext.Current, app.ID, "GlobalSynonyms", wordCount,
                               watch.ElapsedMilliseconds);

            return result;
        }

        public APIResult CreateSpin(Stream stream)
        {
            var parms = RequestTools.ParseBody(stream);

            var watch = new Stopwatch();
            watch.Start();
            if (WebOperationContext.Current != null)
                WebOperationContext.Current.OutgoingResponse.ContentType = "text/plain";

            //Extract params
            string email = parms["email"];
            string apikey = parms["apikey"];
            string aid = parms["aid"];
            string text = parms["text"];
            bool dontUseOriginal = APITools.CustomBoolParse(parms["dontuseoriginal"],false);
            bool reorderParagraphs = APITools.CustomBoolParse(parms["reorderparagraphs"],false);

            APIUser user;
            APIApp app;
            int wordCount;
            var result = APIMethods.CreateSpin(email, apikey, aid, text, dontUseOriginal, reorderParagraphs,
                                               out user, out app, out wordCount);

            watch.Stop();
            Logging.RequestLog(user.ID, OperationContext.Current, app.ID, "CreateSpin", wordCount,
                               watch.ElapsedMilliseconds);

            return result;
        }

        public APIStats Statistics(Stream stream)
        {
            if (WebOperationContext.Current != null)
                WebOperationContext.Current.OutgoingResponse.ContentType = "text/plain";
            var parms = RequestTools.ParseBody(stream);
            //Extract params
            string email = parms["email"];
            string apikey = parms["apikey"];
            string aid = parms["aid"];
            return APIMethods.Statistics(email, apikey, aid);
        }

        public string TestConnection()
        {
            if (WebOperationContext.Current != null)
                WebOperationContext.Current.OutgoingResponse.ContentType = "text/plain";
            return "OK";
        }

        public string Reload(Stream stream)
        {
            //string APIPassword = "dnJEJVZinFH2VoG0zkZjnBOvdgU8XUffy2QqxVesA80i8f6m3D";
            //bool success = false;
            //if (username != "admin@spinchimp.com" || password != APIPassword) return "No";
            //else success = APIMaintenance.LoadNewestThes();
            //if (success) return "OK";
            return "Failed";
        }
        public APIResult Login(string email, string password)
        {
            // Validate input parameters
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                return new APIResult(false, "Email and password are required.");
            }

            APIResult result;
            APIUser user;

            try
            {
                // Connect to the database
                using (MySqlConnection conn = new MySqlConnection(APIUsers.ConnectionString))
                {
                    conn.Open();

                    // Prepare the SQL statement to get user by email
                    string sql = "SELECT * FROM aktech_accounts.users WHERE email = @email LIMIT 1;";
                    using (MySqlCommand cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@email", email);

                        // Execute the query and get the user data
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                user = new APIUser
                                {
                                    ID = reader.GetInt32("id"),
                                    Email = reader.GetString("email"),
                                    PasswordHash = reader.GetString("password")
                                };

                                //Verify password hash(replace with your password hashing logic)
                                if (!VerifyPasswordHash(password, user.PasswordHash))
                                {
                                    result = new APIResult(false, "Invalid email or password.");
                                }
                                else
                                {
                                    List<Claim> claims = new List<Claim>{
                                          new Claim(ClaimTypes.Email,email)
                                    };

                                    var payload = new Dictionary<string, object>()
                                    {
                                        { "email", email }
                                    };
                                    var secretKey = "GQDstcKsx0NHjPOuXOYg5MbeJ1XT0uFiwDVvVBrk";
                                    string token = JWT.JsonWebToken.Encode(payload, secretKey, JWT.JwtHashAlgorithm.HS256);
                                    result = new APIResult(true,token);
                                }
                            }
                            else
                            {
                                result = new APIResult(false, "Invalid email or password.");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                errorLogger.Error("Error during login:", ex);
                result = new APIResult(false, "An error occurred during login.");
            }

            return result;
        }

        private bool VerifyPasswordHash(string password, string hashedPassword)
        {
            return Password.Check(password, hashedPassword);
            // Replace this with your actual password hashing logic (e.g., using bcrypt)
            // This example assumes a simple (insecure) string comparison
            //return password == hashedPassword;
        }


        #region Legacy

        public Stream GlobalSpin(string email, string apikey, string aid,
                                 string qual, string posmatch, string protectedterms, string rewrite,
                                 string replacephraseswithphrases, string spinwithinspin,
                                 string spinwithinhtml, string applyinstantunique, string fullcharset,
                                 string spintidy, string tagprotect, string maxspindepth, Stream content)
        {
            var watch = new Stopwatch();
            watch.Start();
            if (WebOperationContext.Current != null)
                WebOperationContext.Current.OutgoingResponse.ContentType = "text/plain";

            //Extract params
            string text = new StreamReader(content).ReadToEnd();
            bool bApplyInstantUnique = APITools.CustomBoolParse(applyinstantunique, false);
            bool bAutoSpinTidy = APITools.CustomBoolParse(spintidy, true);
            bool bRewrite = APITools.CustomBoolParse(rewrite, false);
            bool bReplacePhrasesWithPhrases = APITools.CustomBoolParse(replacephraseswithphrases, false);
            bool bSpinWithinSpin = APITools.CustomBoolParse(spinwithinspin, false);
            bool bFullCharSet = APITools.CustomBoolParse(fullcharset, false);
            int maxDepth = APITools.CustomIntParse(maxspindepth, 0);
            int iQual = APITools.CustomIntParse(qual, 4);
            int iPosMatch = APITools.CustomIntParse(posmatch, 3);

            // Run method
            APIUser user;
            APIApp app;
            int wordCount;
            var result = APIMethods.GlobalSynonyms(email, apikey, aid, text, iQual, iQual, iPosMatch, "en", protectedterms,
                tagprotect, bRewrite ? 1 : 0, false, false, bReplacePhrasesWithPhrases,
                                      false, bAutoSpinTidy,
                                      1, false, 0, bSpinWithinSpin, maxDepth,
                                      bApplyInstantUnique ? 0 : -1, out user, out app, out wordCount);

            watch.Stop();
            Logging.RequestLog(user.ID, OperationContext.Current, app.ID, "GlobalSynonyms", wordCount,
                               watch.ElapsedMilliseconds);

            if (result.status == "failure") return new MemoryStream(Encoding.UTF8.GetBytes("Failed:" + result.output));
            return new MemoryStream(Encoding.UTF8.GetBytes(result.output));
        }

        public Stream GenerateSpin(string email, string apikey, string aid,
                                   string dontuseoriginal, string reorderparagraphs, string tagprotect, Stream content)
        {
            var watch = new Stopwatch();
            watch.Start();
            if (WebOperationContext.Current != null)
                WebOperationContext.Current.OutgoingResponse.ContentType = "text/plain";

            //Extract params
            string text = new StreamReader(content).ReadToEnd();
            bool bDontUseOriginal = APITools.CustomBoolParse(dontuseoriginal, false);
            bool bReorderParagraphs = APITools.CustomBoolParse(reorderparagraphs, false);

            // Run method
            APIUser user;
            APIApp app;
            int wordCount;
            var result = APIMethods.CreateSpin(email, apikey, aid, text, bDontUseOriginal, bReorderParagraphs,
                                               out user, out app, out wordCount);

            watch.Stop();
            Logging.RequestLog(user.ID, OperationContext.Current, app.ID, "GenerateSpin", wordCount,
                               watch.ElapsedMilliseconds);

            if (result.status == "failure") return new MemoryStream(Encoding.UTF8.GetBytes("Failed:" + result.output));
            return new MemoryStream(Encoding.UTF8.GetBytes(result.output));
        }

        public Stream QueryStats(string email, string apikey, string aid, string simple)
        {
            if (WebOperationContext.Current != null)
                WebOperationContext.Current.OutgoingResponse.ContentType = "text/plain";
            var stats = APIMethods.Statistics(email, apikey, aid);

            if (!string.IsNullOrEmpty(stats.error)) return new MemoryStream(Encoding.UTF8.GetBytes("Failed:" +
                    stats.error));

            bool bSimple = APITools.CustomBoolParse(simple, true);

            MemoryStream ms = new MemoryStream();
            using (var sw = new StreamWriter(ms))
            {
                if (bSimple)
                {
                    int totalqueries = stats.prolimit + stats.apilimit - stats.usedthismonth;
                    sw.Write(totalqueries.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    int dailyLimit = (stats.prolimit/30);
                    int used = stats.usedthismonth;
                    int extended = 0;
                    int remDaily = stats.prolimit / 30;
                    // if has extended, subtract from this. Otherwise if used more than extended/api, subtract from daily
                    if (stats.apilimit > used)
                        extended = (stats.apilimit - used);
                    else
                        remDaily = (stats.prolimit - stats.apilimit)/30;
                        
                    sw.Write("Daily Limit," + dailyLimit.ToString(CultureInfo.InvariantCulture) + "|");
                    sw.Write("Remaining Daily Limit," + remDaily.ToString(CultureInfo.InvariantCulture) + "|");
                    sw.Write("Extended Quota," + extended.ToString(CultureInfo.InvariantCulture) + "|");
                    sw.Write("Bulk Quota,0");
                }
            }
            byte[] returnBytes = ms.ToArray();
            return new MemoryStream(returnBytes);
        }

        #endregion

        #region Testing
        //public System.IO.Stream GlobalSpinTest(string qual,
        //    string posmatch, string protectedterms, string rewrite, Stream content)
        //{
        //    WebOperationContext.Current.OutgoingResponse.ContentType = "text/plain";
        //    string text = new StreamReader(content).ReadToEnd();
        //    //text = System.Web.HttpUtility.UrlDecode(text);
        //    //Input Checking
        //    int iQual;
        //    if (string.IsNullOrEmpty(qual) || !int.TryParse(qual, out iQual)) iQual = 4;
        //    else
        //    {
        //        if (iQual > 5) iQual = 5;
        //        if (iQual < 1) iQual = 1;
        //    }
        //    int iPosMatch;
        //    if (string.IsNullOrEmpty(posmatch) || !int.TryParse(posmatch, out iPosMatch)) iPosMatch = 3;
        //    else
        //    {
        //        if (iPosMatch > 4) iPosMatch = 4;
        //        if (iPosMatch < 0) iPosMatch = 0;
        //    }
        //    bool bRewrite;
        //    if (string.IsNullOrEmpty(rewrite) || !bool.TryParse(rewrite, out bRewrite)) bRewrite = false;

        //    //string text = "";//RequestTools.GetHttpParam(HttpContext.Current.Request, "text", string.Empty);
        //    //check article meets constraints
        //    int wordCount = RequestTools.WordCount(text);
        //    if (wordCount < 1) return new System.IO.MemoryStream(Encoding.UTF8.GetBytes("Failed:" +
        //        "There are no words in your article!"));
        //    if (wordCount > 5000) return new System.IO.MemoryStream(Encoding.UTF8.GetBytes("Failed:"
        //        + "Article too long (" + wordCount.ToString() + " words). Max is 5000."));

        //    //Quality
        //    Thesaurus.QualityRating quality = Thesaurus.QualityRating.All;
        //    switch (iQual)
        //    {
        //        case 1: quality = Thesaurus.QualityRating.All;
        //            break;
        //        case 2: quality = Thesaurus.QualityRating.Average;
        //            break;
        //        case 3: quality = Thesaurus.QualityRating.Good;
        //            break;
        //        case 4: quality = Thesaurus.QualityRating.Better;
        //            break;
        //        case 5: quality = Thesaurus.QualityRating.Best;
        //            break;
        //    }

        //    //POS Match
        //    PartOfSpeech.POSTagMatchType posMatch = PartOfSpeech.POSTagMatchType.None;
        //    switch (iPosMatch)
        //    {
        //        case 0: posMatch = PartOfSpeech.POSTagMatchType.None;
        //            break;
        //        case 1: posMatch = PartOfSpeech.POSTagMatchType.ExtremelyLoose;
        //            break;
        //        case 2: posMatch = PartOfSpeech.POSTagMatchType.Loose;
        //            break;
        //        case 3: posMatch = PartOfSpeech.POSTagMatchType.Full;
        //            break;
        //        case 4: posMatch = PartOfSpeech.POSTagMatchType.FullSpin;
        //            break;
        //    }

        //    //Get protected terms list
        //    List<string> protectedTermsList = new List<string>();
        //    if (!string.IsNullOrEmpty(protectedterms))
        //    {
        //        string[] pt = protectedterms.Split(',');
        //        foreach (string term in pt)
        //            protectedTermsList.Add(term);
        //    }

        //    Random rand = new Random();
        //    string spunResult = SpinEngine.SpinAll(text, false, quality, rand, protectedTermsList,
        //        posMatch, false, 0, bRewrite);

        //    return new System.IO.MemoryStream(Encoding.UTF8.GetBytes(spunResult)); ;
        //}


        //      public Stream HelloWorld(string test, Stream content)
        //{
        //    var sr = new StreamReader(content);
        //    string text = sr.ReadToEnd();
        //    WebOperationContext.Current.OutgoingResponse.ContentType = "text/plain";
        //    return new MemoryStream(Encoding.UTF8.GetBytes("Hello World! " + text + test));
        //}

        public string DatabaseConnectionTest(string test)
        {
            WebOperationContext.Current.OutgoingResponse.ContentType = "text/plain";
            string result = "uninitialised";
            int hello = -1;
            try
            {
                Stopwatch watch = new Stopwatch();
                watch.Start();
                using (MySqlConnection conn = new MySqlConnection(APIUsers.ConnectionString))
                {
                    using (MySqlCommand cmd = new MySqlCommand("SELECT id,wordlimit FROM aktech_api.apiapps WHERE aid = " +
                                                               "100 LIMIT 1;", conn))
                    {
                        conn.Open();
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            result = "reader OK";
                            if (reader.Read())
                            {
                                hello = reader.GetInt32("id");
                                result = hello.ToString();
                            }
                        }
                    }
                }
                watch.Stop();
                result += "; Basic test: " + watch.ElapsedMilliseconds.ToString();
                watch.Reset();
                watch.Start();
                APIUsers.CheckAPICredentials("jamesroseaffiliate@gmail.com", "0f017d4cf567807b95667ff2233ae58e38597a3f",
                                             "ConnectionTest", 0);
                watch.Stop();
                result += "; Method test: " + watch.ElapsedMilliseconds.ToString();
            }
            catch (Exception exception)
            {
                result = exception.Message;
            }

            return result;
        }
        private static readonly ILog errorLogger = LogManager.GetLogger(typeof(CRAPIService));

        public void TestError(string key)
        {
            if (key != "WA1NK2IRBE0BK4R01OAZ8FV2PSJ8KON178QXFP5UFVBTQVK6LWLB8CMIOMMY9CYF") return;
            errorLogger.Error("How now brown cow");
        }

        #endregion
    }
}
