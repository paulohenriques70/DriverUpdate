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
****************************************************************************
*/

namespace DriverUpdate_1
{
    using System;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using Skyline.DataMiner.Automation;
    using Skyline.DataMiner.Core.DataMinerSystem.Automation;
    using Skyline.DataMiner.Core.DataMinerSystem.Common;
    using Skyline.DataMiner.Net.Messages;
    using Skyline.DataMiner.Net.Messages.Advanced;

    /// <summary>
    /// Represents a DataMiner Automation script.
    /// </summary>
    public class Script
    {
        // delay between stopping 2 elements
        private const int DELAY_STOP = 500;

        // delay between starting 2 elements
        private const int DELAY_START = 500;

        // delay betwwen 2 stopping batches
        private const int DELAY_STOP_BATCH = 5000;

        // delay between 2 starting batches
        private const int DELAY_START_BATCH = 10000;

        // timeout for a stoping batch
        private const int TIMEOUT_STOP = 60000;

        // timeout for a starting batch
        private const int TIMEOUT_START = 60000;

        // delay between checking retries
        private const int WAIT_BEFORE_GET_STATE = 5000;

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

            engine.Timeout = TimeSpan.FromHours(2);

            // script input arguments
            string driverName = engine.GetScriptParam("Driver name").Value.Trim();
            string driverVersion = engine.GetScriptParam("Driver Version").Value.Trim();
            int batchSize = int.Parse(engine.GetScriptParam("Batch Size").Value.Trim());

            engine.GenerateInformation("Driver name: " + driverName);
            engine.GenerateInformation("Driver Version: " + driverVersion);
            engine.GenerateInformation("Batch Size: " + batchSize);

            if (CheckInputParameters(driverName, driverVersion))
            {
                Element[] elements = engine.FindElementsByProtocol(driverName, "Production").Where(x => x.IsActive).ToArray();
                engine.GenerateInformation("Number of active elements running Production version: " + elements.Count());

                StopElements(elements, batchSize);

                Thread.Sleep(1000);

                SetProductionVersion(driverName, driverVersion);

                Thread.Sleep(1000);

                StartElements(elements, batchSize);
            }

        }

        private bool CheckInputParameters(string driverName, string driverVersion)
        {
            try
            {
                IDmsProtocol newProtocol = dms.GetProtocol(driverName, driverVersion);
                IDmsProtocol prodProtocol = dms.GetProtocol(driverName, "Production");
                engine.GenerateInformation("Current Production version: " + prodProtocol.ReferencedVersion);
                if (driverVersion.ToLower() == prodProtocol.ReferencedVersion.ToLower())
                {
                    engine.GenerateInformation("ERR: Version " + driverVersion + " is already in Production. Aborting");
                    return false;
                }
            }
            catch (Exception ex)
            {
                engine.GenerateInformation("ERR: " + ex.Message + ". Aborting.");
                return false;
            }

            return true;
        }

        private void StartElements(Element[] elements, int batchSize)
        {
            int size = elements.Count();
            int number = 1;
            for (int i = 0; i < size; i += batchSize, number++)
            {
                engine.GenerateInformation("Proceesing start batch #" + number + " out of " + ((int)(size / batchSize) + 1));
                StartElements(elements.Skip(i).Take(batchSize).ToArray());
                Thread.Sleep(DELAY_START_BATCH);
            }
        }

        private void StartElements(Element[] elements)
        {
            foreach (Element el in elements)
            {
                el.Start();
                engine.GenerateInformation("Element started: " + el.ElementName + " - " + el.ProtocolName + " - " + el.ProtocolVersion);

                Thread.Sleep(DELAY_START);
            }

            if (Retry(elements, IsAllStarted, TIMEOUT_START))
            {
                engine.GenerateInformation("All elements in batch were started");
            }
            else
            {
                engine.GenerateInformation("ERR: Failed to start some elements");
            }
        }

        private void StopElements(Element[] elements, int batchSize)
        {
            int size = elements.Count();
            int number = 1;
            for (int i = 0; i < size; i += batchSize, number++)
            {
                engine.GenerateInformation("Proceesing stop batch #" + number + " out of " + ((int)(size / batchSize) + 1));
                StopElements(elements.Skip(i).Take(batchSize).ToArray());
                Thread.Sleep(DELAY_STOP_BATCH);
            }
        }

        private void StopElements(Element[] elements)
        {
            foreach (Element el in elements)
            {
                el.Stop();
                engine.GenerateInformation("Element stopped: " + el.ElementName + " - " + el.ProtocolName + " - " + el.ProtocolVersion);

                Thread.Sleep(DELAY_STOP);
            }

            if (Retry(elements, IsAllStopped, TIMEOUT_STOP))
            {
                engine.GenerateInformation("All elements in batch were stopped");
            }
            else
            {
                engine.GenerateInformation("ERR: Failed to stop some elements");
            }
        }

        private bool IsAllStopped(Element[] elems)
        {
            foreach (Element el in elems)
            {
                if (dms.GetElement(el.ElementName).State != Skyline.DataMiner.Core.DataMinerSystem.Common.ElementState.Stopped)
                    return false;
            }

            return true;
        }

        private bool IsAllStarted(Element[] elems)
        {
            foreach (Element el in elems)
            {
                if (!dms.GetElement(el.ElementName).IsStartupComplete())
                    return false;
            }

            return true;
        }

        // Retry until success or until timeout
        private bool Retry(Element[] elemns, Func<Element[], bool> allReady, int timeout)
        {
            bool success;

            Stopwatch sw = new Stopwatch();
            sw.Start();

            do
            {
                success = allReady(elemns);
                if (!success)
                {
                    engine.GenerateInformation("Waiting for all elements to stop/start ...");
                    Thread.Sleep(WAIT_BEFORE_GET_STATE);
                }
            }
            while (!success && sw.Elapsed.TotalMilliseconds <= timeout);

            return success;
        }

        private void SetProductionVersion(string driverName, string driverVersion)
        {
            engine.GenerateInformation("Setting " + driverName + "_" + driverVersion + " as Production.");

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
}