using Roof.Common.Tools;
using System;

namespace Roof.PathFinder
{
    internal class Program
    {
        internal const string PATHFINDER_IDENTIFIER =
            Common.Networking.NetworkCommonIdentifiers.PATHFINDER_GLOBAL_IDENTIFIER;

        private static bool _exitRequested = false;
        private static Thread? _webSocketRelayThread;

        static async Task Main(string[] args)
        {
            Logger.Log($"VERSION - {Version.Get()} ({Version.GetCompileFlag()})", LogSeverity.Warning);
            
            Console.CancelKeyPress += new ConsoleCancelEventHandler(OnCancelKeyPress);
            AppDomain.CurrentDomain.ProcessExit += new EventHandler(OnProcessExit);

            _webSocketRelayThread = new Thread(async () =>
            {
                await WebSocketRelay.Start();
            })
            { IsBackground = true };
            _webSocketRelayThread.Start();

            while (!_exitRequested) 
            {
                await Task.Delay(1000);
            }
        }

        static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs args)
        {
            Logger.Log("Shutting down PathFinder...");

            // Prevent the process from terminating immediately
            _exitRequested = true;
            args.Cancel = true;

            WebSocketRelay.StopAsync().GetAwaiter().GetResult();
        }

        static void OnProcessExit(object? sender, EventArgs args)
        {
            Logger.Log("Performing final cleanup...");

            WebSocketRelay.Stop();

            _webSocketRelayThread = null;
        }
    }
}
