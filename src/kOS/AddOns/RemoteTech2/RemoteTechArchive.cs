﻿using kOS.Persistence;
using kOS.Safe.Persistence;

namespace kOS.AddOns.RemoteTech2
{
    public class RemoteTechArchive : Archive
    {
        public bool CheckRange(Vessel vessel)
        {
            return vessel != null && RemoteTechHook.Instance.HasConnectionToKSC(vessel.id);
        }
    }
}
