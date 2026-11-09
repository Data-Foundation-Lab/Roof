using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Roof.Common;
using Roof.Common.Networking;
using System.Net.NetworkInformation;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Roof.Common.Tools;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Roof.Common.Networking.RoofData;
using Roof.Common.Networking.RNMS.WebSocket;
using Roof.Common.Networking.RNMS;
using Roof.Client;
using System.ComponentModel;
using System.Reflection;

namespace Roof.ClientTestConsole
{
    /// <summary>
    /// CLI app for producing terminal-level results before going into the actual production GUI app (RoofDeck).
    /// </summary>
    class Program
    {
        internal static string Version
        {
            get
            {
                var assembly = Assembly.GetExecutingAssembly();
                var infoVersionAttribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                string version = infoVersionAttribute?.InformationalVersion ?? "Version not found";
                return version;
            }
        }
        
        static async Task Main(string[] args)
        {
            Logger.Log("Starting main system loop...");

            var nw = await Common.Networking.NetworkManager.GetAllLocalNetworkDeviceIPAddresses();

            foreach ( var ip in nw )
                Console.WriteLine(ip);

            /*bool listner = false;

            var nmp = new NetworkMessageProvider();
            
            if(listner)
                await nmp.Initialize(RNMSNetworkProtocols.MANAGED_TCP, true, "", 6969);
            else
                await nmp.Initialize(RNMSNetworkProtocols.MANAGED_TCP, false, "127.0.0.1", 6969);

            nmp.OnClientConnected += (sender, point) =>
            {
                Console.WriteLine("Client connected");
            };

            nmp.OnClientDisconnected += (sender, point) =>
            {
                Console.WriteLine("Client disconnected");
            };

            nmp.OnMessageReceived += (sender, point) =>
            {
                Console.WriteLine($"Message received: {point}");
            };

            if (!listner)
            {
                while (nmp.GetProvider() == null)
                {
                    Console.WriteLine("nmp null");
                }
                
                nmp.SendMessage("Hello World!");
            }*/
            
            while (true)
            {
                await Task.Delay(10);
            }
        }
    }
    
}