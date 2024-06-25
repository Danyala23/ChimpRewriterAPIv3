// Super simple class to accumulate counters and store to a log file at regular intervals.
// by: Mark Beljaars
// created: 10/03/2014

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Timers;
using System.Xml;

namespace SpinEngine
{
    /// <summary>
    ///     Class for storing counter logs to an xml file at regular intervals
    /// </summary>
    internal class CounterLog
    {
        private static CounterLog _instance;
        private readonly Timer _storageTimer;
        private Dictionary<string, int> _counters = new Dictionary<string, int>();

        public CounterLog()
        {
            LogFilename = "CounterLog.xml";
            _storageTimer = new Timer(30000);
            _storageTimer.Elapsed += delegate { Store(); };
            load();
        }

        /// <summary>
        ///     Counter singleton
        /// </summary>
        public static CounterLog Instance
        {
            get { return _instance ?? (_instance = new CounterLog()); }
        }

        /// <summary>
        ///     Counter log file name and path
        /// </summary>
        public string LogFilename { get; set; }

        /// <summary>
        ///     Automatic storage interval (or 0 to disable)
        /// </summary>
        public double StoreInterval
        {
            get { return _storageTimer.Interval; }
            set { _storageTimer.Interval = value; }
        }

        ~CounterLog()
        {
            Store();
        }

        /// <summary>
        ///     Add a new counter to the log
        /// </summary>
        /// <param name="counterName">Counter ID</param>
        public void AddCounter(string counterName)
        {
            if (_counters.ContainsKey(counterName)) return;
            _counters.Add(counterName, 0);
            if (_storageTimer.Interval > 0) _storageTimer.Start();
        }

        /// <summary>
        ///     Increment a counter
        /// </summary>
        /// <param name="counterName">Counter ID</param>
        /// <returns>The counter value</returns>
        /// <remarks>The Counter ID is added if not found.</remarks>
        public int IncCounter(string counterName)
        {
            AddCounter(counterName);
            _counters[counterName]++;
            if (_storageTimer.Interval > 0) _storageTimer.Start();
            return _counters[counterName];
        }

        /// <summary>
        ///     Reset an existing counter to zero
        /// </summary>
        /// <param name="counterName">Counter ID</param>
        public void ResetCounter(string counterName)
        {
            if (_counters.ContainsKey(counterName)) _counters[counterName] = 0;
            if (_storageTimer.Interval > 0) _storageTimer.Start();
        }

        /// <summary>
        ///     Force log file storage
        /// </summary>
        /// <remarks>Log file is automatically stored when the destructor is called.</remarks>
        public void Store()
        {
            try
            {
                var s = new DataContractSerializer(_counters.GetType());
                using (var f = new StreamWriter(LogFilename))
                {
                    using (var w = new XmlTextWriter(f))
                    {
                        w.Formatting = Formatting.Indented;
                        s.WriteObject(w, _counters);
                        w.Flush();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error: CounterLog.ForceStore=" + ex.Message);
            }
        }

        // load the logfile into memory
        private void load()
        {
            try
            {
                if (!File.Exists(LogFilename)) return;
                var s = new DataContractSerializer(_counters.GetType());
                using (var f = new StreamReader(LogFilename))
                using (var r = new XmlTextReader(f)) _counters = (Dictionary<string, int>)s.ReadObject(r);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error: CounterLog.load=" + ex.Message);
            }
        }
    }
}