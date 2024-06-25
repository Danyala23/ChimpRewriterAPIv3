using System.ServiceModel;
using System.ServiceModel.Web;
using System.IO;
using ChimpRewriterAPIv3.API;

namespace ChimpRewriterAPIv3
{
    // NOTE: You can use the "Rename" command on the "Refactor" menu to change the interface name "IService1" in both code and config file together.
    [ServiceContract]
    public interface ICRAPIService
    {
        [OperationContract]
        [WebInvoke(Method = "POST", BodyStyle = WebMessageBodyStyle.WrappedRequest, UriTemplate = "ChimpRewrite", ResponseFormat = WebMessageFormat.Json)]
        APIResult ChimpRewrite(Stream stream);

        [OperationContract]
        [WebInvoke(Method = "POST", BodyStyle = WebMessageBodyStyle.WrappedRequest, UriTemplate = "CreateSpin", ResponseFormat = WebMessageFormat.Json)]
        APIResult CreateSpin(Stream stream);

        [OperationContract]
        [WebInvoke(Method = "POST", BodyStyle = WebMessageBodyStyle.WrappedRequest, UriTemplate = "Statistics", ResponseFormat = WebMessageFormat.Json)]
        APIStats Statistics(Stream stream);

        [OperationContract]
        [WebInvoke(Method = "POST", BodyStyle = WebMessageBodyStyle.WrappedRequest, UriTemplate = "TestConnection", ResponseFormat = WebMessageFormat.Json)]
        string TestConnection();

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = "Reload")]
        string Reload(Stream stream);

        [WebInvoke(Method = "POST", UriTemplate = "Login", BodyStyle = WebMessageBodyStyle.WrappedRequest)]
        APIResult Login(string email, string password);

        #region Legacy
        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = "GlobalSpin?email={email}&apikey={apikey}" +
            "&aid={aid}&quality={qual}&posmatch={posmatch}&protectedterms={protectedterms}" +
            "&rewrite={rewrite}&phraseignorequality={replacephraseswithphrases}" +
            "&spinwithinspin={spinwithinspin}&spinwithinhtml={spinwithinhtml}" +
            "&applyinstantunique={applyinstantunique}&fullcharset={fullcharset}" +
            "&spintidy={spintidy}&tagprotect={tagprotect}&maxspindepth={maxspindepth}")]
        Stream GlobalSpin(string email, string apikey, string aid, string qual,
            string posmatch, string protectedterms, string rewrite,
            string replacephraseswithphrases, string spinwithinspin, string spinwithinhtml,
            string applyinstantunique, string fullcharset, string spintidy, string tagprotect,
            string maxspindepth, Stream content);

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = "GenerateSpin?email={email}&apikey={apikey}" +
            "&aid={aid}&dontuseoriginal={dontuseoriginal}&reorderparagraphs={reorderparagraphs}" +
            "&tagprotect={tagprotect}")]
        Stream GenerateSpin(string email, string apikey, string aid,
            string dontuseoriginal, string reorderparagraphs, string tagprotect, Stream content);

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = "QueryStats?email={email}&apikey={apikey}" +
            "&aid={aid}&simple={simple}")]
        Stream QueryStats(string email, string apikey, string aid, string simple);

        #endregion

        #region Testing

        [OperationContract]
        [WebGet(UriTemplate = "DatabaseConnectionTest?test={test}")]
        string DatabaseConnectionTest(string test);
        [OperationContract]
        [WebGet(UriTemplate = "TestError?key={key}")]
        void TestError(string key);
        #endregion
    }

}
