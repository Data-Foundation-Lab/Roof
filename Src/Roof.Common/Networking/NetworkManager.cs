using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Roof.Common.Networking
{
    /// <summary>
    /// Shared manager class for networking-related operations.
    /// </summary>
    public class NetworkManager
    {
        private const string ARP_COMMAND_NAME = "arp";
        private const int QUICK_SCAN_TIMEOUT = 100; // milliseconds
        private const int FULL_SCAN_TIMEOUT = 1000; // milliseconds
        private const int MAX_CONCURRENT_PINGS = 256;

        private static readonly string[] _virtualAdapterKeywords = { "vmware", "virtual", "vethernet", "vbox", "hyper-v" };

        /// <summary>
        /// Combines ping scanning and ARP table lookup to discover local devices.
        /// </summary>
        public static async Task<List<IPAddress>> GetAllLocalNetworkDeviceIPAddresses()
        {
            var activeIPs = new ConcurrentBag<IPAddress>();
            
            // On macOS, local discovery is handled heavily through mDNSResponder, Bonjour, ARP cache, local network privacy, and interface state.
            // Avoid active ICMP subnet scanning on macOS because aggressive ping sweeps can destabilise local DNS/discovery behaviour.
            // Instead, piggyback off the ARP table and let higher-level verification confirm candidates.
            if (OperatingSystem.IsMacOS())
            {
                foreach (var ip in GetArpTableAddressesForOSX())
                {
                    if (IsUsableIPv4Address(ip))
                        activeIPs.Add(ip);
                }
                
                return activeIPs.Distinct().ToList();
            }
            
            var localIpAddresses = GetLocalIPAddresses();

            // If we have local IPs, scan each corresponding subnet.
            if (localIpAddresses.Count > 0)
            {
                var tasks = new List<Task>();

                foreach (var ip in localIpAddresses)
                {
                    var subnetMask = GetSubnetMask(ip);
                    if (subnetMask == null)
                        continue;  // skip if subnet mask isn�t available

                    var (startIp, endIp) = GetIpRange(ip, subnetMask);

#if DEBUG
                    // Uncomment for debugging
                    // Console.WriteLine($"Local IP Address: {ip}\nSubnet Mask: {subnetMask}\nScanning IP range: {startIp} - {endIp}");
#endif
                    tasks.Add(QuickScanNetwork(startIp, endIp, activeIPs));
                }

                await Task.WhenAll(tasks);
            }

            if (OperatingSystem.IsWindows())
            {
                // Additionally, get IPs from the ARP table.
                foreach (var ip in GetArpTableAddresses())
                {
                    activeIPs.Add(ip);
                }
            }

            // Return unique IPs.
            return activeIPs.Distinct().ToList();
        }
        
        private static bool IsUsableIPv4Address(IPAddress ip)
        {
            if (ip.AddressFamily != AddressFamily.InterNetwork)
                return false;

            var bytes = ip.GetAddressBytes();

            // 0.0.0.0
            if (bytes.All(b => b == 0))
                return false;

            // 127.0.0.0/8 loopback
            if (bytes[0] == 127)
                return false;

            // 169.254.0.0/16 link-local
            if (bytes[0] == 169 && bytes[1] == 254)
                return false;

            // 224.0.0.0+ multicast/reserved
            if (bytes[0] >= 224)
                return false;

            return true;
        }

        public static IPAddress GetLocalIPAddress()
        {
            try
            {
                var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                 ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                 !IsVirtualAdapter(ni))
                    .ToList();

                foreach (var ni in networkInterfaces)
                {
                    var props = ni.GetIPProperties();

                    // First, try to find an IPv4 address with a default gateway
                    var ipv4Address = props.UnicastAddresses
                        .FirstOrDefault(addr =>
                            addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                            props.GatewayAddresses.Any());

                    if (ipv4Address != null)
                    {
                        return ipv4Address.Address;
                    }
                }

                // If no IPv4 address with a default gateway is found, fall back to any IPv4 address
                foreach (var ni in networkInterfaces)
                {
                    var ipv4Address = ni.GetIPProperties().UnicastAddresses
                        .FirstOrDefault(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork);

                    if (ipv4Address != null)
                    {
                        return ipv4Address.Address;
                    }
                }

                // If still no IPv4 address is found, try an alternative method
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    if (socket.LocalEndPoint is IPEndPoint endPoint)
                    {
                        return endPoint.Address;
                    }
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static List<IPAddress> GetLocalIPAddresses()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                             !IsVirtualAdapter(ni))
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses
                    .Where(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(ip => ip.Address))
                .Distinct()
                .ToList();
        }

        private static bool IsVirtualAdapter(NetworkInterface ni)
        {
            // Check for common virtual adapter names
            string name = ni.Name.ToLower();
            string description = ni.Description.ToLower();

            return _virtualAdapterKeywords.Any(keyword => name.Contains(keyword) || description.Contains(keyword)) ||
                   ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel;
        }

        private static IPAddress GetSubnetMask(IPAddress address)
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => !IsVirtualAdapter(ni))
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .FirstOrDefault(ip => ip.Address.Equals(address))?.IPv4Mask;
        }

        private static (IPAddress, IPAddress) GetIpRange(IPAddress address, IPAddress subnetMask)
        {
            byte[] ipBytes = address.GetAddressBytes();
            byte[] maskBytes = subnetMask.GetAddressBytes();

            byte[] startIpBytes = new byte[ipBytes.Length];
            byte[] endIpBytes = new byte[ipBytes.Length];

            for (int i = 0; i < ipBytes.Length; i++)
            {
                startIpBytes[i] = (byte)(ipBytes[i] & maskBytes[i]);
                endIpBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
            }

            return (new IPAddress(startIpBytes), new IPAddress(endIpBytes));
        }

        private static async Task QuickScanNetwork(IPAddress startIp, IPAddress endIp, ConcurrentBag<IPAddress> activeIPs)
        {
            var ips = GenerateIPRange(startIp.GetAddressBytes(), endIp.GetAddressBytes()).ToList();
            var quickScanResults = new ConcurrentDictionary<IPAddress, bool>();

            // Quick scan
            await PingIPAddressesInBatches(ips, QUICK_SCAN_TIMEOUT, quickScanResults);

            // Add quick scan results to activeIPs
            foreach (var kvp in quickScanResults.Where(r => r.Value))
            {
                activeIPs.Add(kvp.Key);
            }

            // Full scan on unresponsive IPs
            var unresponsiveIPs = quickScanResults.Where(r => !r.Value).Select(r => r.Key).ToList();
            await PingIPAddressesInBatches(unresponsiveIPs, FULL_SCAN_TIMEOUT, quickScanResults);

            // Add full scan results to activeIPs
            foreach (var kvp in quickScanResults.Where(r => r.Value))
            {
                activeIPs.Add(kvp.Key);
            }
        }

        private static async Task PingIPAddressesInBatches(List<IPAddress> ips, int timeout, ConcurrentDictionary<IPAddress, bool> results)
        {
            for (int i = 0; i < ips.Count; i += MAX_CONCURRENT_PINGS)
            {
                var batch = ips.Skip(i).Take(MAX_CONCURRENT_PINGS);
                var pingTasks = batch.Select(ip => PingIpAddressAsync(ip, timeout, results));
                await Task.WhenAll(pingTasks);
            }
        }

        private static IEnumerable<IPAddress> GenerateIPRange(byte[] startIp, byte[] endIp)
        {
            while (true)
            {
                yield return new IPAddress((byte[])startIp.Clone());

                if (CompareIpAddresses(startIp, endIp) == 0)
                    yield break;

                IncrementIpAddress(startIp);
            }
        }

        private static int CompareIpAddresses(byte[] ip1, byte[] ip2)
        {
            for (int i = 0; i < ip1.Length; i++)
            {
                int compare = ip1[i].CompareTo(ip2[i]);
                if (compare != 0)
                {
                    return compare;
                }
            }
            return 0;
        }

        private static void IncrementIpAddress(byte[] ip)
        {
            for (int i = ip.Length - 1; i >= 0; i--)
            {
                if (ip[i] < 255)
                {
                    ip[i]++;
                    break;
                }
                ip[i] = 0;
            }
        }

        private static async Task PingIpAddressAsync(IPAddress ip, int timeout, ConcurrentDictionary<IPAddress, bool> results)
        {
            using var ping = new Ping();
            try
            {
                var reply = await ping.SendPingAsync(ip, timeout);
                results[ip] = reply.Status == IPStatus.Success;
            }
            catch (PingException)
            {
                results[ip] = false;
            }
        }

        /// <summary>
        /// Retrieves IP addresses from the system ARP table.
        /// </summary>
        private static List<IPAddress> GetArpTableAddresses()
        {
            List<IPAddress> result = new List<IPAddress>();

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = ARP_COMMAND_NAME,
                    Arguments = "-a",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    // Each line that starts with an IP address is likely an entry
                    foreach (string line in output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string trimmedLine = line.Trim();
                        if (Regex.IsMatch(trimmedLine, @"^(\d{1,3}\.){3}\d{1,3}"))
                        {
                            // The first token is the IP address
                            string[] tokens = trimmedLine.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (tokens.Length > 0 && IPAddress.TryParse(tokens[0], out IPAddress ip))
                            {
                                result.Add(ip);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Optionally log the exception.
            }

            return result;
        }
        
        private static List<IPAddress> GetArpTableAddressesForOSX()
        {
            List<IPAddress> result = new List<IPAddress>();

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = ARP_COMMAND_NAME,
                    Arguments = "-an",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    // Each line typically has the format: ? (IP) at <MAC> on <interface> ...
                    foreach (string line in output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string trimmedLine = line.Trim();
                        // Use regex to extract the IP address inside parentheses
                        Match match = Regex.Match(trimmedLine, @"\((\d{1,3}(?:\.\d{1,3}){3})\)");
                        if (match.Success)
                        {
                            if (IPAddress.TryParse(match.Groups[1].Value, out IPAddress ip))
                            {
                                result.Add(ip);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Optionally log the exception.
            }

            return result;
        }
    }
}