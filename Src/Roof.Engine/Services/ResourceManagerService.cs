using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Roof.Common.Tools;
using Roof.Engine.Managers;

namespace Roof.Engine.Services
{
    internal class ResourceManagerService : ServiceBase
    {
        private const int RESOURCEMANAGER_WINDOW_TICKRATE_MILLISECONDS = 60000; //Running every 60 seconds

        private readonly CancellationTokenSource _backgroundResourceMgrCts = new();
        private Task? _backgroundResourceMgrTask;

        internal override Task Initialize()
        {
            return Task.CompletedTask;
        }
        
        internal override void Start()
        {
            //Logger.LogToFile("[ResourceManagerService]: Disabling Linux services...");
            //Managers.SystemResourceManager.DisableLinuxServices();
            
            Logger.LogToFile("[ResourceManagerService]: Monitoring resource usage...");
            _backgroundResourceMgrTask = Task.Run(() => MonitorResourceUsage(_backgroundResourceMgrCts.Token));
            ShutdownManager.Observe(_backgroundResourceMgrTask);
        }

        internal override async Task<bool> Terminate()
        {
            _backgroundResourceMgrCts.Cancel();
            if (_backgroundResourceMgrTask != null)
                await _backgroundResourceMgrTask;
            return true;
        }

        private async Task MonitorResourceUsage(CancellationToken token)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(RESOURCEMANAGER_WINDOW_TICKRATE_MILLISECONDS));
            
            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    Logger.LogToFile("[ResourceManagerService]: Performing background resource monitoring...");
                    
                    // Monitor CPU Usage
                    // Monitor Memory Usage
                    // Monitor Disk Usage
                    // Monitor Network Usage
                    // Monitor GPU Usage
                    // Monitor I/O Usage
                    // Monitor Process Usage
                    // Monitor Service Usage
                    // Monitor User Usage

                    await SystemResourceManager.MonitorAndRotateLogFile();
                    SystemResourceManager.MonitorAndEvictLogFiles();
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                Logger.LogToFile("[ResourceManagerService]: Background service stopped.");
            }
        }
    }
}