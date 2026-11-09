using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;
using Roof.Common.Tools;

namespace Roof.Engine.Services
{
    internal class ServiceManager
    {
        private static readonly ServiceInformation[] _serviceInfos =
        [
            new ServiceInformation { Name = "ClientManagerService", Service = new ClientManagerService() },
            new ServiceInformation { Name = "PathFinderService", Service = new PathFinderService() },
            new ServiceInformation { Name = "UpdateService", Service = new UpdateService() },
            new ServiceInformation { Name = "ResourceManagerService", Service = new ResourceManagerService() }
        ];

        private static readonly ConcurrentBag<Thread> _serviceThreads = new();

        static ServiceManager() { }

        internal static async Task InitializeServices()
        {
            foreach (var serviceInfo in _serviceInfos)
            {
                Logger.LogToFile($"[ServiceManager]: Initializing {serviceInfo.Name}...");
                await serviceInfo.Service.Initialize();
                Logger.LogToFile($"[ServiceManager]: {serviceInfo.Name} initialized.");
            }
        }

        internal static ServiceBase GetService(string serviceName) => _serviceInfos.FirstOrDefault(s => s.Name == serviceName).Service;

        internal static void StartServices()
        {
            foreach (var serviceInfo in _serviceInfos)
            {
                try
                {
                    Logger.LogToFile($"[ServiceManager]: Starting {serviceInfo.Name}...");
                    var svcThread = new Thread(() =>
                    {
                        serviceInfo.Service.Start();
                        Logger.LogToFile($"[ServiceManager]: {serviceInfo.Name} started.");
                    }) { Name = serviceInfo.Name };
                    svcThread.IsBackground = true;

                    SetThreadPriority(svcThread, serviceInfo.Name);
                    svcThread.Start();

                    _serviceThreads.Add(svcThread);
                }
                catch (Exception ex)
                {
                    Logger.LogToFile($"[ServiceManager] - StartServices(): Exception occurred while trying to start {serviceInfo.Name}. Exception: {ex.Message}", LogSeverity.Danger);
                }
            }
        }

        internal static async Task TerminateServices()
        {
            foreach (var thread in _serviceThreads)
                thread.Join(TimeSpan.FromSeconds(5));

            foreach (var serviceInfo in _serviceInfos.OrderBy(s => s.Name switch
            {
                "UpdateService" => 0,
                "ResourceManagerService" => 1,
                "ClientManagerService" => 2,
                _ => 3
            }))
            {
                try
                {
                    Logger.LogToFile($"[ServiceManager]: Stopping {serviceInfo.Name}...");
                    if (await serviceInfo.Service.Terminate().WaitAsync(TimeSpan.FromSeconds(10)))
                    {
                        Logger.LogToFile($"[ServiceManager]: Successfully terminated {serviceInfo.Name}.");
                    }
                    else
                    {
                        Environment.ExitCode = 1;
                        Logger.LogToFile($"[ServiceManager] - TerminateServices(): Failed to terminate {serviceInfo.Name}.", LogSeverity.Danger);
                    }
                }
                catch (Exception ex)
                {
                    Environment.ExitCode = 1;
                    Logger.LogExceptionToFile($"[ServiceManager]: Failed to stop {serviceInfo.Name}.", ex);
                }
            }
        }

        private static void SetThreadPriority(Thread th, string svcName)
        {
            th.Priority = svcName switch
            {
                "ResourceManagerService" => ThreadPriority.Highest,
                _ => ThreadPriority.Normal,
            };
        }

        private struct ServiceInformation
        {
            internal string Name;
            internal ServiceBase Service;
        }
    }
}
