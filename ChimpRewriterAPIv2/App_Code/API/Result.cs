using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

// ReSharper disable InconsistentNaming
namespace ChimpRewriterAPIv3.API
{
    [DataContract]
    public class APIResult
    {
        [DataMember]
        public string status;
        [DataMember]
        public string output;

        public APIResult(bool success, string output)
        {
            status = success ? "success" : "failure";
            this.output = output;
        }
    }

    [DataContract]
    public class APIStats
    {
        [DataMember]
        public int remainingthismonth;
        [DataMember]
        public int prolimit;
        [DataMember]
        public string proexpiry;
        [DataMember]
        public int apilimit;
        [DataMember]
        public string apiexpiry;
        [DataMember] 
        public string error;
        [DataMember]
        public int usedtoday;
        [DataMember]
        public int usedthismonth;
        [DataMember]
        public int usedever;


        public APIStats(int prolimit, DateTime proexpiry, int apilimit, DateTime apiexpiry, int usedtoday, int usedthismonth, int usedever)
        {
            remainingthismonth = (proexpiry.AddDays(5) > DateTime.UtcNow ? prolimit : 0)
                                 + (apiexpiry.AddDays(5) > DateTime.UtcNow ? apilimit : 0)
                                 - usedthismonth;
            if (remainingthismonth < 0) remainingthismonth = 0;
            error = "";
            this.usedtoday = usedtoday;
            this.usedthismonth = usedthismonth;
            this.usedever = usedever;
            this.prolimit = prolimit;
            this.proexpiry = proexpiry.ToString("dd/MM/yyyy");
            this.apilimit = apilimit;
            this.apiexpiry = apiexpiry.ToString("dd/MM/yyyy");
        }

        public APIStats(string error)
        {
            this.error = error;
            remainingthismonth = 0;
            usedtoday = 0;
            usedthismonth = 0;
            usedever = 0;
            prolimit = 0;
            proexpiry = "None";
            apilimit = 0;
            apiexpiry = "None";
        }
    }
}
// ReSharper restore InconsistentNaming