using System;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using System.Net.NetworkInformation;

namespace TranslationEplAddIn.Tool.License
{
    public static class HardwareFingerprint
    {
        public static string GetMachineCode()
        {
            try
            {
                string cpuId = GetWmiProperty("Win32_Processor", "ProcessorId");
                string boardId = GetWmiProperty("Win32_BaseBoard", "SerialNumber");
                
                // If both are unknown or empty, fallback to MAC address and main disk ID
                if ((string.IsNullOrEmpty(cpuId) || cpuId == "UNKNOWN" || cpuId.IndexOf("UNKNOWN", StringComparison.OrdinalIgnoreCase) >= 0) && 
                    (string.IsNullOrEmpty(boardId) || boardId == "UNKNOWN" || boardId.IndexOf("UNKNOWN", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    cpuId = GetMacAddress();
                    boardId = GetDiskId();
                }

                string raw = $"EPL-TRANS|{cpuId}|{boardId}";
                
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < 8; i++) // Take first 8 bytes = 16 hex chars
                    {
                        sb.Append(hashBytes[i].ToString("X2"));
                    }
                    return sb.ToString();
                }
            }
            catch
            {
                return "ERR-GET-HARDWARE-ID";
            }
        }

        private static string GetWmiProperty(string wmiClass, string property)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}"))
                using (var collection = searcher.Get())
                {
                    foreach (var obj in collection)
                    {
                        string val = obj[property]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(val))
                        {
                            return val;
                        }
                    }
                }
            }
            catch { }
            return "UNKNOWN";
        }

        private static string GetMacAddress()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in interfaces)
                {
                    if (ni.OperationalStatus == OperationalStatus.Up && 
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback && 
                        ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    {
                        string mac = ni.GetPhysicalAddress().ToString();
                        if (!string.IsNullOrEmpty(mac))
                            return mac;
                    }
                }
            }
            catch { }
            return "NO-MAC";
        }

        private static string GetDiskId()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_PhysicalMedia"))
                using (var collection = searcher.Get())
                {
                    foreach (var obj in collection)
                    {
                        string serial = obj["SerialNumber"]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(serial))
                            return serial;
                    }
                }
            }
            catch { }
            return "NO-DISK";
        }
    }
}
