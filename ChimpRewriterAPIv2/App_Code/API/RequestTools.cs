using System;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Web;
using System.Globalization;

namespace ChimpRewriterAPIv3.API
{
    /// <summary>
    /// Summary description for RequestTools
    /// </summary>
    public class RequestTools
    {
        public static NameValueCollection ParseBody(Stream body)
        {
            string text = new StreamReader(body, Encoding.UTF8).ReadToEnd();
            return HttpUtility.ParseQueryString(text);
        }

        public static DateTime GetHttpParam(HttpRequest request, string paramName, string format, DateTime defaultValue)
        {
            string paramValue = request.Params[paramName];
            if (paramValue != null)
            {
                try
                {
                    return DateTime.ParseExact(paramValue, format, CultureInfo.InvariantCulture);
                }
                catch { }
            }

            return defaultValue;
        }

        public static int GetHttpParam(HttpRequest request, string paramName, int defaultValue)
        {
            string paramValue = request.Params[paramName];
            if (paramValue != null)
            {
                try
                {
                    return int.Parse(paramValue);
                }
                catch { }
            }

            return defaultValue;
        }

        public static Decimal GetHttpParam(HttpRequest request, string paramName, Decimal defaultValue)
        {
            string paramValue = request.Params[paramName];
            if (paramValue != null)
            {
                try
                {
                    return Decimal.Parse(paramValue);
                }
                catch { }
            }

            return defaultValue;
        }

        public static string GetHttpParam(HttpRequest request, string paramName, string defaultValue)
        {
            string paramValue = request.Params[paramName];
            if (paramValue != null)
            {
                return paramValue;
            }

            return defaultValue;
        }

        public static int WordCount(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int c = 0;
            for (int i = 1; i < s.Length; i++)
            {
                if (char.IsWhiteSpace(s[i - 1]) == true)
                {
                    if (char.IsLetterOrDigit(s[i]) == true ||
                        char.IsPunctuation(s[i]))
                    {
                        c++;
                    }
                }
            }
            if (s.Length > 2)
            {
                c++;
            }
            return c;
        }
    }
}
