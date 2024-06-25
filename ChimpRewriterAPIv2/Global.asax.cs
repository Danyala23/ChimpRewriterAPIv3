using System;
using System.Diagnostics;
using System.IO;
using ChimpRewriterAPIv3.SpinEngine;

namespace ChimpRewriterAPIv3
{
    public class Global : System.Web.HttpApplication
    {

        protected void Application_Start(object sender, EventArgs e)
        {
            log4net.Config.XmlConfigurator.Configure();
            var watch = new Stopwatch();
            watch.Start();
            Thesaurus.Load();
            PartOfSpeech.Load(Path.Combine(AppDomain.CurrentDomain.GetData("DataDirectory").ToString(),"models"), null, true);
            RewriteRules.Load(Path.Combine(AppDomain.CurrentDomain.GetData("DataDirectory").ToString(), "rules"), null, true);
            watch.Stop();
            Logging.RequestLog(0,null,0,"AppLoad",0,watch.ElapsedMilliseconds);
            //APIUserAllowance limits;
            //APIUser user;
            //APIApp app;
            //APIUsers.CreateFastUser(new FastUser(new APIUser(-1, "jamesroseaffiliate@gmail.com", "0f017d4cf567807b95667ff2233ae58e38597a3f",
            //    5000, 400, 400, DateTime.Now.AddMonths(3), 0, DateTime.Now, 0), DateTime.Now));
            //APIUsers.VerifyAPICall("jamesroseaffiliate@gmail.com", "0f017d4cf567807b95667ff2233ae58e38597a3f", "app",
            //              1, out limits, out user, out app);
        }

        protected void Session_Start(object sender, EventArgs e)
        {

        }

        protected void Application_BeginRequest(object sender, EventArgs e)
        {

        }

        protected void Application_AuthenticateRequest(object sender, EventArgs e)
        {

        }

        protected void Application_Error(object sender, EventArgs e)
        {

        }

        protected void Session_End(object sender, EventArgs e)
        {

        }

        protected void Application_End(object sender, EventArgs e)
        {

        }
    }
}