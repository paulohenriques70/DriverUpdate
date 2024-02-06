/*
****************************************************************************
*  Copyright (c) 2024,  Skyline Communications NV  All Rights Reserved.    *
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

dd/mm/2024	1.0.0.1		XXX, Skyline	Initial version
****************************************************************************
*/

using Skyline.DataMiner.Net;
using Skyline.DataMiner.Net.Messages;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ElementStartAndStop_1
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using Skyline.DataMiner.Automation;

    /// <summary>
    /// Represents a DataMiner Automation script.
    /// </summary>
    public class Script
    {
        private TimeSpan AddRemoveSubscriptionTimeout = TimeSpan.FromSeconds(30);
        private readonly int ElementStartStopTimeout = 30000;
        private readonly object _lock = new object();

        public bool StartElement(IEngine engine, int dmaid, int eid)
        {

            // The element 
            int DMAID = dmaid;
            int ElementID = eid;



            // Check if the element is active, if already active it will not send star events
            var elemInfo = engine.SendSLNetSingleResponseMessage(new GetElementByIDMessage(DMAID, ElementID)) as ElementInfoEventMessage;
            if (elemInfo != null && elemInfo.State == ElementState.Active)
            {
                engine.GenerateInformation("The element is already active");
                return true;
            }

            // **************************
            // Setting up subscription
            // ****************************
            var connection = engine.GetUserConnection();
            ManualResetEvent _waitEvent = new ManualResetEvent(false);
            string subscriptionId = $"{Guid.NewGuid()}_{DateTime.Now:yyyy_MM_dd_HH_mm_ss}";

            // subscription filter, which events do we want to receive
            SubscriptionFilter elementStateSubscriptionFilter = new SubscriptionFilterElement(typeof(ElementStateEventMessage), DMAID, ElementID)
            {
                Options = SubscriptionFilterOptions.SkipInitialEvents,
            };

            // Event handler What to do when we receive an event
            NewMessageEventHandler evHandler = (s, e) =>
            {
                // Is it from our subscription
                if (!e.FromSet(subscriptionId))
                {
                    return;
                }

                // Is it the correct type
                if (e.Message is ElementStateEventMessage elementState)
                {
                    // Are all flags correct
                    if (elementState.ElementID == ElementID && elementState.DataMinerID == DMAID
                        && elementState.State == ElementState.Active && elementState.IsElementStartupComplete)
                    {
                        _waitEvent.Set();
                    }
                }
            };

            connection.OnNewMessage += evHandler;
            try
            {
                // Create the subscription on the server
                using (var subscribeWait = new ManualResetEvent(false))
                {
                    connection.TrackAddSubscription(subscriptionId, new SubscriptionFilter[] { elementStateSubscriptionFilter }).OnFinished(() => subscribeWait.Set()).Execute();
                    if (!subscribeWait.WaitOne(this.AddRemoveSubscriptionTimeout))
                    {
                        throw new Exception($"Adding the subscription took a long time >{this.AddRemoveSubscriptionTimeout.TotalSeconds}.");
                    }
                }


                // ****************************
                // End Setting up subscription
                // ****************************


                // ****************************
                // Do the action that will generate the event
                // ***********************************


                // Now we do the actual action where we subscribed on
                // ****************************
                // Start the element
                // ****************************
                SetElementStateMessage startRequest = new SetElementStateMessage(DMAID, ElementID, ElementState.Active, true)
                {
                    HostingDataMinerID = -1
                };

                Task.Run(() => engine.SendSLNetSingleResponseMessage(startRequest));

                if (_waitEvent.WaitOne(ElementStartStopTimeout))
                {
                    return true;
                }
                else
                {
                    return false;
                }

            }
            finally
            {
                // ***********************************
                // Cleanup the subscription on the system, otherwise the system will get overloaded.
                // ***********************************
                _waitEvent.Dispose();
                // Cleanup the subscriptions
                connection.OnNewMessage -= evHandler;
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


        public bool StartElements(IEngine engine, Element[] elems)
        {

            List<Element> stopped = new List<Element>();
            HashSet<string> ids = new HashSet<string>();

            foreach (Element el in elems)
            {
                // Find the elements that need to be started
                var elemInfo = engine.SendSLNetSingleResponseMessage(new GetElementByIDMessage(el.DmaId, el.ElementId)) as ElementInfoEventMessage;
                if (elemInfo != null && elemInfo.State != ElementState.Active)
                {
                    stopped.Add(el);
                    ids.Add($"{el.DmaId}/{el.ElementId}");
                }
            }

            int nr_elems = stopped.Count;

            if (nr_elems == 0)
            {
                engine.GenerateInformation("No elements to stop!");
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
                    engine.GenerateInformation("Notification recevied for " + id + " " + elementState.State + " " + elementState.IsElementStartupComplete);

                    lock (_lock)
                    {
                        // Are all flags correct
                        if (ids.Contains(id) && elementState.State == ElementState.Active && elementState.IsElementStartupComplete)
                        {

                            ids.Remove(id);
                            engine.GenerateInformation("Matched. Removed from hashset");
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
                    engine.GenerateInformation("Start request sent to " + stopped[i].ElementName + " " + stopped[i].DmaId + "/" + stopped[i].ElementId);

                }

                if (_waitEvent.WaitOne(ElementStartStopTimeout))
                {
                    return true;
                }
                else
                {
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


        public bool StopElements(IEngine engine, Element[] elems)
        {

            List<Element> active = new List<Element>();
            HashSet<string> ids = new HashSet<string>();

            foreach (Element el in elems)
            {
                // Find the elements that need to be stopped
                var elemInfo = engine.SendSLNetSingleResponseMessage(new GetElementByIDMessage(el.DmaId, el.ElementId)) as ElementInfoEventMessage;
                if (elemInfo != null && elemInfo.State == ElementState.Active)
                {
                    active.Add(el);
                    ids.Add($"{el.DmaId}/{el.ElementId}");
                }
            }

            int nr_elems = active.Count;

            if (nr_elems == 0)
            {
                engine.GenerateInformation("No elements to start!");
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
                    engine.GenerateInformation("Notification recevied for " + id + " " + elementState.State + " " + elementState.IsElementStartupComplete);

                    lock (_lock)
                    {
                        // Are all flags correct
                        if (ids.Contains(id) && elementState.State == ElementState.Stopped && !elementState.IsElementStartupComplete)
                        {

                            ids.Remove(id);
                            engine.GenerateInformation("Matched. Removed from hashset");
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
                    engine.GenerateInformation("Stop request sent to " + active[i].ElementName + " " + active[i].DmaId + "/" + active[i].ElementId);

                }

                if (_waitEvent.WaitOne(ElementStartStopTimeout))
                {
                    return true;
                }
                else
                {
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



        public void Run(IEngine engine)
        {



            Element[] elems = {
                engine.FindElement("ElemAA"),
                engine.FindElement("Elem2"),
                engine.FindElement("ElemDD"),
                engine.FindElement("ElemXXX"),
            };
            StartElements(engine, elems);
            engine.GenerateInformation("End Start elements");
            engine.Sleep(5000);
            StopElements(engine, elems);
            engine.GenerateInformation("End Stopt elements");
        }
    }

}