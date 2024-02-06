/*
****************************************************************************
*  Copyright (c) 2023,  Skyline Communications NV  All Rights Reserved.    *
****************************************************************************

By using this script, you expressly agree with the usage terms and
conditions set out below.
This script and all related materials are protected by copyrights and
other intellectual property rights that exclusively belong
to Skyline Communications.

A user license granted for this script is strictly for personal use only.
This script may not be used in any way by anyone without the prior
written consent of Skyline Communications. Any sublicensing of this
script is forbidden.

Any modifications to this script by the user are only allowed for
personal use and within the intended purpose of the script,
and will remain the sole responsibility of the user.
Skyline Communications will not be responsible for any damages or
malfunctions whatsoever of the script resulting from a modification
or adaptation by the user.

The content of this script is confidential information.
The user hereby agrees to keep this confidential information strictly
secret and confidential and not to disclose or reveal it, in whole
or in part, directly or indirectly to any person, entity, organization
or administration without the prior written consent of
Skyline Communications.

Any inquiries can be addressed to:

	Skyline Communications NV
	Ambachtenstraat 33
	B-8870 Izegem
	Belgium
	Tel.	: +32 51 31 35 69
	Fax.	: +32 51 31 01 29
	E-mail	: info@skyline.be
	Web		: www.skyline.be
	Contact	: Ben Vandenberghe

****************************************************************************
Revision History:

DATE		VERSION		AUTHOR			COMMENTS

11/12/2023	1.0.0.1		PHE, Skyline	Initial version
05/02/2024  1.0.0.2     PHE, Skyline	User subscription mechanism
****************************************************************************
*/

namespace DriverUpdate_1
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Skyline.DataMiner.Automation;
    using Skyline.DataMiner.Core.DataMinerSystem.Automation;
    using Skyline.DataMiner.Core.DataMinerSystem.Common;
    using Skyline.DataMiner.Net;
    using Skyline.DataMiner.Net.Helper;
    using Skyline.DataMiner.Net.Messages;
    using Skyline.DataMiner.Net.Messages.Advanced;

    /// <summary>
    /// Represents a DataMiner Automation script.
    /// </summary>
    public class Script
    {
        // delay betwwen 2 stopping batches
        private const int DELAY_STOP_BATCH = 5000;

        // delay between 2 starting batches
        private const int DELAY_START_BATCH = 5000;

        private readonly Log log = Log.OpenLog();

        private IEngine engine;
        private IDms dms;

        /// <summary>
        /// The script entry point.
        /// </summary>
        /// <param name="engine">Link with SLAutomation process.</param>
        public void Run(IEngine engine)
        {
            this.engine = engine;
            dms = engine.GetDms();

            // script timeout: 4 hours
            engine.Timeout = TimeSpan.FromHours(4);

            // script input arguments
            string driverName = engine.GetScriptParam("Driver name").Value.Trim();
            string driverVersion = engine.GetScriptParam("Driver Version").Value.Trim();
            int batchSize = int.Parse(engine.GetScriptParam("Batch Size").Value.Trim());

            engine.GenerateInformation("Driver name: " + driverName);
            engine.GenerateInformation("Driver Version: " + driverVersion);
            engine.GenerateInformation("Batch Size: " + batchSize);

            log.LogLevel = Log.Level.INFO;

            log.WriteLine(Log.Level.INFO, "Driver name: " + driverName);
            log.WriteLine(Log.Level.INFO, "Driver Version: " + driverVersion);
            log.WriteLine(Log.Level.INFO, "Batch Size: " + batchSize);

            if (CheckInputParameters(driverName, driverVersion))
            {
                Element[] elements = engine.FindElementsByProtocol(driverName, "Production").Where(x => x.IsActive && !x.RawInfo.IsDerivedElement).ToArray();
                log.WriteLine(Log.Level.INFO, "Number of active elements running Production version: " + elements.Count());

                StopElements(elements, batchSize);

                Thread.Sleep(1000);

                SetProductionVersion(driverName, driverVersion);

                Thread.Sleep(1000);

                StartElements(elements, batchSize);
            }

            log.WriteLine(Log.Level.INFO, "Finished.");
        }

        private bool CheckInputParameters(string driverName, string driverVersion)
        {
            try
            {
                IDmsProtocol newProtocol = dms.GetProtocol(driverName, driverVersion);
                IDmsProtocol prodProtocol = dms.GetProtocol(driverName, "Production");
                engine.GenerateInformation("Current Production version: " + prodProtocol.ReferencedVersion);
                log.WriteLine(Log.Level.INFO, "Current Production version: " + prodProtocol.ReferencedVersion);

                if (driverVersion.ToLower() == prodProtocol.ReferencedVersion.ToLower())
                {
                    engine.GenerateInformation("ERR: Version " + driverVersion + " is already in Production. Aborting.");
                    log.WriteLine(Log.Level.ERROR, "Version " + driverVersion + " is already in Production. Aborting.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                engine.GenerateInformation("ERR: " + ex.Message + ". Aborting.");
                log.WriteLine(Log.Level.ERROR, ex.Message + ". Aborting.");
                return false;
            }

            return true;
        }

        private void StartElements(Element[] elements, int batchSize)
        {
            StartAndStopElements action = new StartAndStopElements(log);

            int totalBatches = (int)Math.Ceiling((double)elements.Count() / batchSize);

            int number = 0;
            foreach (var batch in elements.Batch(batchSize))
            {
                log.WriteLine(Log.Level.INFO, "Proceesing start batch #" + ++number + " out of " + totalBatches);
                action.Start(engine, batch.ToArray());
                Thread.Sleep(DELAY_START_BATCH);
            }
        }

        private void StopElements(Element[] elements, int batchSize)
        {
            StartAndStopElements action = new StartAndStopElements(log);

            int totalBatches = (int)Math.Ceiling((double)elements.Count() / batchSize);

            int number = 0;
            foreach (var batch in elements.Batch(batchSize))
            {
                log.WriteLine(Log.Level.INFO, "Proceesing stop batch #" + ++number + " out of " + totalBatches);
                action.Stop(engine, batch.ToArray());
                Thread.Sleep(DELAY_STOP_BATCH);
            }
        }


        private void SetProductionVersion(string driverName, string driverVersion)
        {
            log.WriteLine(Log.Level.INFO, "Setting " + driverName + "_" + driverVersion + " as Production.");

            DMSMessage msg = new SetDataMinerInfoMessage
            {
                DataMinerID = -1,
                ElementID = -1,
                HostingDataMinerID = -1,
                bInfo1 = Int32.MaxValue,
                bInfo2 = 0,
                IInfo1 = Int32.MaxValue,
                IInfo2 = Int32.MaxValue,

                Sa1 = new SA(new string[] { driverName, driverVersion }),
                What = (int)NotifyType.SetAsCurrentProtoocol,
            };

            engine.SendSLNetMessage(msg);
        }
    }

    public class Log
    {
        private readonly StreamWriter file;

        public Level LogLevel { get; set; }

        public Log(string logFileName)
        {
            file = new StreamWriter(logFileName, false)
            {
                AutoFlush = true,
            };
            LogLevel = Level.INFO;
        }

        public enum Level
        {
            DEBUG,
            INFO,
            WARN,
            ERROR,
        }

        public static Log OpenLog()
        {
            string path = @"C:\Skyline_Data\DriverUpdate_Log";

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            string logFileName = path + @"\\driverupdate_" + DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss") + ".txt";
            return new Log(logFileName);
        }

        public void Close()
        {
            file.Close();
        }

        public Log WriteLine(Level level, String line)
        {

            if (level >= LogLevel)
            {
                string timeStamp = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss.ff");

                file.WriteLine(string.Format("{0}|{1}|{2}", timeStamp, level, line));
            }

            return this;
        }

        public Log WriteLine()
        {
            file.WriteLine();
            return this;
        }
    }


    public class StartAndStopElements
    {
        // delay between stopping 2 elements
        private const int DELAY_STOP = 100;

        // delay between starting 2 elements
        private const int DELAY_START = 100;

        private TimeSpan AddRemoveSubscriptionTimeout = TimeSpan.FromSeconds(30);

        private readonly int ElementStartStopTimeout = 10*60000; // 10 minutes. is it enough?

        private readonly object _lock = new object();
        private readonly Log log;


        public StartAndStopElements(Log log)
        {
            this.log = log;
        }

        public bool Start(IEngine engine, Element[] elems)
        {

            List<Element> stopped = new List<Element>();
            HashSet<string> ids = new HashSet<string>();

            foreach (Element el in elems)
            {
                // Find the elements that need to be started
                var elemInfo = engine.SendSLNetSingleResponseMessage(new GetElementByIDMessage(el.DmaId, el.ElementId)) as ElementInfoEventMessage;
                if (elemInfo != null && elemInfo.State != Skyline.DataMiner.Net.Messages.ElementState.Active)
                {
                    stopped.Add(el);
                    ids.Add($"{el.DmaId}/{el.ElementId}");
                }
            }

            int nr_elems = stopped.Count;

            if (nr_elems == 0)
            {
                // engine.GenerateInformation("No elements to stop!");
                log.WriteLine(Log.Level.INFO, "No elemments active to stop in this batch");
                return false;
            }

            // set the subscriptions
            var connection = engine.GetUserConnection();

            // intiialize wait event
            ManualResetEvent _waitEvent = new ManualResetEvent(false);


            // subsription id to be used in the request
            string subscriptionId = $"{Guid.NewGuid()}_{DateTime.Now:yyyy_MM_dd_HH_mm_ss}";

            // create the subscription filters, one for each element
            SubscriptionFilter[] elementStateSubscriptionFilters = new SubscriptionFilterElement[nr_elems];
            for (int i = 0; i < nr_elems; i++)
            {
                int dmaid = stopped[i].DmaId;
                int eid = stopped[i].ElementId;

                elementStateSubscriptionFilters[i] = new SubscriptionFilterElement(typeof(ElementStateEventMessage), dmaid, eid)
                {
                    Options = SubscriptionFilterOptions.SkipInitialEvents,
                };
            }



            // Event handler What to do when we receive an event
            NewMessageEventHandler eventHandler = (s, e) =>
            {
                // Is it from our subscription
                if (!e.FromSet(subscriptionId))
                {
                    return;
                }

                // Is it the correct type?
                if (e.Message is ElementStateEventMessage elementState)
                {

                    string id = $"{elementState.DataMinerID}/{elementState.ElementID}";

                    log.WriteLine(Log.Level.DEBUG, $"Notification received for {id} {elementState.State} {elementState.IsElementStartupComplete}");
                    
                    lock (_lock)
                    {
                        // Are all flags correct
                        if (ids.Contains(id) && elementState.State == Skyline.DataMiner.Net.Messages.ElementState.Active && elementState.IsElementStartupComplete)
                        {

                            ids.Remove(id);
                            log.WriteLine(Log.Level.INFO, $"Element started {id}");
                        }

                        // if there is no element left in the hashset it means we received a notification for eachc of them
                        if (ids.Count == 0)
                            _waitEvent.Set();
                    }
                }
            };

            // add handler to OnNewMessage
            connection.OnNewMessage += eventHandler;

            try
            {
                // Create the subscription request on the server
                using (var subscribeWait = new ManualResetEvent(false))
                {
                    connection.TrackAddSubscription(subscriptionId, elementStateSubscriptionFilters).OnFinished(() => subscribeWait.Set()).Execute();
                    if (!subscribeWait.WaitOne(this.AddRemoveSubscriptionTimeout))
                    {
                        throw new Exception($"Adding the subscription took a long time >{this.AddRemoveSubscriptionTimeout.TotalSeconds}.");
                    }
                }

                // Now we do the actual action where we subscribed on: Start the elements
                for (int i = 0; i < nr_elems; i++)
                {
                    stopped[i].Start();
                    log.WriteLine(Log.Level.INFO, $"Start request sent to \"{stopped[i].ElementName}\" {stopped[i].DmaId}/{stopped[i].ElementId}");
                    engine.Sleep(DELAY_START);
                }

                if (_waitEvent.WaitOne(ElementStartStopTimeout))
                {
                    log.WriteLine(Log.Level.INFO, "All elements started in this batch");
                    return true;
                }
                else
                {
                    log.WriteLine(Log.Level.ERROR, "Timeout starting elements of ths batch");
                    return false;
                }

            }
            finally
            {
                // Cleanup the subscription on the system, otherwise the system will get overloaded.
                _waitEvent.Dispose();

                // Cleanup the subscriptions
                connection.OnNewMessage -= eventHandler;
                using (var subscribeWait = new ManualResetEvent(false))
                {
                    connection.TrackClearSubscriptions(subscriptionId).OnFinished(() => subscribeWait.Set()).Execute();
                    if (!subscribeWait.WaitOne(this.AddRemoveSubscriptionTimeout))
                    {
                        throw new Exception($"Clearing the subscription took a long time more then {this.AddRemoveSubscriptionTimeout.TotalSeconds}.");
                    }
                }
            }
        }


        public bool Stop(IEngine engine, Element[] elems)
        {

            List<Element> active = new List<Element>();
            HashSet<string> ids = new HashSet<string>();

            foreach (Element el in elems)
            {
                // Find the elements that need to be stopped
                var elemInfo = engine.SendSLNetSingleResponseMessage(new GetElementByIDMessage(el.DmaId, el.ElementId)) as ElementInfoEventMessage;
                if (elemInfo != null && elemInfo.State == Skyline.DataMiner.Net.Messages.ElementState.Active)
                {
                    active.Add(el);
                    ids.Add($"{el.DmaId}/{el.ElementId}");
                }
            }

            int nr_elems = active.Count;

            if (nr_elems == 0)
            {
                engine.GenerateInformation("No elements to start!");
                log.WriteLine(Log.Level.INFO, "No elemments to stop in this batch, all are active");
                return false;
            }

            // set the subscriptions
            var connection = engine.GetUserConnection();

            // intiialize wait event
            ManualResetEvent _waitEvent = new ManualResetEvent(false);


            // subsription id to be used in the request
            string subscriptionId = $"{Guid.NewGuid()}_{DateTime.Now:yyyy_MM_dd_HH_mm_ss}";

            // create the subscription filters, one for each element
            SubscriptionFilter[] elementStateSubscriptionFilters = new SubscriptionFilterElement[nr_elems];
            for (int i = 0; i < nr_elems; i++)
            {
                int dmaid = active[i].DmaId;
                int eid = active[i].ElementId;

                elementStateSubscriptionFilters[i] = new SubscriptionFilterElement(typeof(ElementStateEventMessage), dmaid, eid)
                {
                    Options = SubscriptionFilterOptions.SkipInitialEvents,
                };
            }



            // Event handler What to do when we receive an event
            NewMessageEventHandler eventHandler = (s, e) =>
            {
                // Is it from our subscription
                if (!e.FromSet(subscriptionId))
                {
                    return;
                }

                // Is it the correct type?
                if (e.Message is ElementStateEventMessage elementState)
                {

                    string id = $"{elementState.DataMinerID}/{elementState.ElementID}";

                    log.WriteLine(Log.Level.DEBUG, $"Notification received for {id} {elementState.State} {elementState.IsElementStartupComplete}");
                    
                    lock (_lock)
                    {
                        // Are all flags correct
                        if (ids.Contains(id) && elementState.State == Skyline.DataMiner.Net.Messages.ElementState.Stopped && !elementState.IsElementStartupComplete)
                        {

                            ids.Remove(id);
                            log.WriteLine(Log.Level.INFO, $"Element stopped {id}");
                        }

                        // if there is no element left in the hashset it means we received a notification for each of them
                        if (ids.Count == 0)
                            _waitEvent.Set();
                    }
                }
            };

            // add handler to OnNewMessage
            connection.OnNewMessage += eventHandler;

            try
            {
                // Create the subscription request on the server
                using (var subscribeWait = new ManualResetEvent(false))
                {
                    connection.TrackAddSubscription(subscriptionId, elementStateSubscriptionFilters).OnFinished(() => subscribeWait.Set()).Execute();
                    if (!subscribeWait.WaitOne(this.AddRemoveSubscriptionTimeout))
                    {
                        throw new Exception($"Adding the subscription took a long time >{this.AddRemoveSubscriptionTimeout.TotalSeconds}.");
                    }
                }

                // Now we do the actual action where we subscribed on: Stop the elements
                for (int i = 0; i < nr_elems; i++)
                {
                    active[i].Stop();
                    log.WriteLine(Log.Level.INFO, $"Stop request sent to \"{active[i].ElementName}\" {active[i].DmaId}/{active[i].ElementId}");
                    engine.Sleep(DELAY_STOP);
                }
                
                if (_waitEvent.WaitOne(ElementStartStopTimeout))
                {
                    log.WriteLine(Log.Level.INFO, "All elements stopped in this batch");
                    return true;
                }
                else
                {
                    log.WriteLine(Log.Level.ERROR, "Timeout stopping elements of ths batch");
                    return false;
                }

            }
            finally
            {
                // Cleanup the subscription on the system, otherwise the system will get overloaded.
                _waitEvent.Dispose();

                // Cleanup the subscriptions
                connection.OnNewMessage -= eventHandler;
                using (var subscribeWait = new ManualResetEvent(false))
                {
                    connection.TrackClearSubscriptions(subscriptionId).OnFinished(() => subscribeWait.Set()).Execute();
                    if (!subscribeWait.WaitOne(this.AddRemoveSubscriptionTimeout))
                    {
                        throw new Exception($"Clearing the subscription took a long time more then {this.AddRemoveSubscriptionTimeout.TotalSeconds}.");
                    }
                }
            }
        }
    }
}
