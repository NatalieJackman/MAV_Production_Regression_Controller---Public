using Crestron.SimplSharp;
using System;
using System.Collections.Generic;

namespace MAV.ProductionRegressionController
{
    internal static class SelfTargetGuard
    {
        public static uint ControllerSlot { get { try { return InitialParametersClass.ApplicationNumber; } catch { return 0; } } }

        public static bool IsSelf(string address)
        {
            string value = (address ?? string.Empty).Trim();
            if (value.Equals("localhost", StringComparison.OrdinalIgnoreCase) || value == "127.0.0.1" || value == "::1") return true;
            List<string> local = GetLocalAddresses();
            foreach (string ip in local) if (value.Equals(ip, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static List<string> GetLocalAddresses()
        {
            List<string> values = new List<string>();
            for (short adapter = 0; adapter < 4; adapter++)
            {
                try
                {
                    string ip = CrestronEthernetHelper.GetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_GET.GET_CURRENT_IP_ADDRESS, adapter);
                    if (!string.IsNullOrWhiteSpace(ip) && !values.Contains(ip)) values.Add(ip);
                }
                catch { }
            }
            return values;
        }
    }
}
