using System.ServiceModel;
using System.ServiceModel.Channels;
using log4net;

namespace ChimpRewriterAPIv3
{
    public static class Logging
    {
        private static readonly ILog requestsLogger = LogManager.GetLogger("RequestsLogger");

        #region Request Logging

        /// <summary>
        /// Logs a request to the database in a new thread
        /// </summary>
        /// <param name="userID">User ID</param>
        /// <param name="operationContext">Current request context</param>
        /// <param name="aid">Application ID</param>
        /// <param name="method">Method called</param>
        /// <param name="wordCount">Words passed</param>
        /// <param name="processingTime">Time spent on operation in milliseconds</param>
        public static void RequestLog(int userID, OperationContext operationContext, int aid, string method, int wordCount, long processingTime)
        {
            string ip = "";
            if (operationContext != null)
            {
                MessageProperties messageProperties = operationContext.IncomingMessageProperties;
                RemoteEndpointMessageProperty endpointProperty =
                    messageProperties[RemoteEndpointMessageProperty.Name] as RemoteEndpointMessageProperty;
                ip = endpointProperty == null ? "" : endpointProperty.Address;
            }
            requestsLogger.Info("User ID: " + userID + ";IP: " + ip + ";Aid: " + aid + ";Method: " + method + ";WordCount: " +
                wordCount + ";ProcessingTime: " + processingTime);
            //new Thread(() => RequestLogTask(userID, ip, aid, method, wordCount, processingTime)).Start();
        }

        ///// <summary>
        ///// Logs a request to the database
        ///// </summary>
        ///// <param name="userID">User ID</param>
        ///// <param name="ip">IP request came from</param>
        ///// <param name="aid">Application ID</param>
        ///// <param name="method">Method called</param>
        ///// <param name="wordCount">Words passed</param>
        ///// <param name="processingTime">Time spent on operation in milliseconds</param>
        //public static void RequestLogTask(int userID, string ip, int aid, string method, int wordCount, long processingTime)
        //{
            
            //try
            //{

                //using (MySqlConnection conn = new MySqlConnection(APIUsers.ConnectionString))
                //{
                //    using (MySqlCommand cmd = new MySqlCommand())
                //    {
                //        cmd.Connection = conn;
                //        cmd.CommandText =
                //            "INSERT INTO requestlog (userid,IP,aid,method,wordcount,processingtime) VALUES (" +
                //            userID.ToString(CultureInfo.InvariantCulture) + "," +
                //            "'" + ip + "'," + aid.ToString(CultureInfo.InvariantCulture) + ",'" + method + "'," +
                //            wordCount.ToString(CultureInfo.InvariantCulture) + "," +
                //            processingTime.ToString(CultureInfo.InvariantCulture) + ");";
                //        conn.Open();
                //        cmd.ExecuteNonQuery();
                //    }
                //}
            //}
            //catch
            //{
            //}
        //}
        #endregion

        #region Error Logging

        ///// <summary>
        ///// Logs an error to the database in a new thread so user recieves result instantly
        ///// </summary>
        ///// <param name="errortext"></param>
        ///// <param name="exceptionmessage"></param>
        //public static void LogError(string errortext, string exceptionmessage)
        //{
        //    new Thread(() => LogErrorTask(errortext, exceptionmessage)).Start();
        //}

        ///// <summary>
        ///// Logs an error to the database in a new thread so user recieves result instantly
        ///// </summary>
        ///// <param name="errortext"></param>
        ///// <param name="exception"></param>
        //public static void LogError(string errortext, Exception exception)
        //{
        //    LogError(errortext, exception.Message);
        //}

        ///// <summary>
        ///// Logs an error to the database in a new thread so user recieves result instantly
        ///// </summary>
        ///// <param name="errortext"></param>
        //public static void LogError(string errortext)
        //{
        //    LogError(errortext, "");
        //}

        ///// <summary>
        ///// Logs an error to the database in a new thread so user recieves result instantly
        ///// </summary>
        ///// <param name="errortext"></param>
        ///// <param name="exceptionmessage"></param>
        //public static void LogErrorTask(string errortext, string exceptionmessage)
        //{
        //    try
        //    {
        //        using (MySqlConnection conn = new MySqlConnection(APIUsers.ConnectionString))
        //        {
        //            using (MySqlCommand cmd = new MySqlCommand())
        //            {
        //                cmd.Connection = conn;
        //                if (exceptionmessage != null && exceptionmessage.Length > 0)
        //                {
        //                    errortext = errortext.Replace(@"\", @"\\");
        //                    errortext = errortext.Replace("'", @"\'");
        //                    exceptionmessage = exceptionmessage.Replace(@"\", @"\\");
        //                    exceptionmessage = exceptionmessage.Replace("'", @"\'");

        //                    cmd.CommandText =
        //                        "INSERT INTO errorlog (errortext,exceptionmessage) VALUES (@errortext,@exceptionmessage);";
        //                    cmd.Parameters.Add("@errortext", MySqlDbType.String).Value = errortext;
        //                    cmd.Parameters.Add("@exceptionmessage", MySqlDbType.String).Value = exceptionmessage;
        //                }
        //                else
        //                {
        //                    errortext = errortext.Replace(@"\", @"\\");
        //                    errortext = errortext.Replace("'", @"\'");
        //                    cmd.CommandText =
        //                        "INSERT INTO errorlog (errortext) VALUES (@errortext);";
        //                    cmd.Parameters.Add("@errortext", MySqlDbType.String).Value = errortext;
        //                }
        //                conn.Open();
        //                cmd.ExecuteNonQuery();
        //            }
        //        }
        //    }
        //    catch
        //    {
        //    }
        //}

        #endregion
    }
}