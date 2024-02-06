public static bool IsElementLoadedInSLElement(this SLProtocol protocol, uint dmaId, uint elementId)
{
    bool isFullyLoaded = false;

    object elementStartupComplete = protocol.NotifyDataMiner((int)SLNetMessages.NotifyType.NT_ELEMENT_STARTUP_COMPLETE, new uint[] { dmaId, elementId }, null);
    if (elementStartupComplete != null)
    {
        isFullyLoaded = Convert.ToBoolean(elementStartupComplete);
    }

    return isFullyLoaded;
}



public static bool IsElementLoadedInSLNet(this SLProtocol protocol, uint dmaId, uint elementId)
{
    bool isFullyLoaded = false;

    SLNetMessages.GetElementProtocolMessage getElementProtocolMessage = new SLNetMessages.GetElementProtocolMessage((int)dmaId, (int)elementId);
    SLNetMessages.DMSMessage[] getElementProtocolResults = protocol.SLNet.SendMessage(getElementProtocolMessage);
    if (getElementProtocolResults != null)
    {
        foreach (SLNetMessages.GetElementProtocolResponseMessage getElementProtocolResult in getElementProtocolResults)
        {
            if (getElementProtocolResult != null && !getElementProtocolResult.WasBuiltWithUnsafeData)
            {
                isFullyLoaded = true;
            }
        }
    }

    return isFullyLoaded;
}


public static bool IsElementActiveInSLDms(this SLProtocol protocol, uint dmaId, uint elementId)
{
    bool isFullyLoaded = false;

    DMS dms = new DMS();
    object oElementState = null;
    dms.Notify((int)SLNetMessages.NotificationType.DMS_GET_ELEMENT_STATE, 0, dmaId, elementId, out oElementState);

    string sElementState = oElementState as string;
    if (sElementState != null && sElementState.Equals("Active", StringComparison.InvariantCultureIgnoreCase))
    {
        isFullyLoaded = true;
    }

    return isFullyLoaded;
}