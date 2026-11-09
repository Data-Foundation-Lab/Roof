using Roof.Common.Tools;
using Roof.Engine.Managers;

namespace Roof.Engine.Services
{
    internal class UpdateService : ServiceBase
    {
        private const int UPDATE_WINDOW_TICKRATE_MILLISECONDS = 60000; //Running every 60 seconds

        private readonly CancellationTokenSource _backgroundUpdateCts = new();
        private Task? _backgroundUpdateTask;
        
        internal override Task Initialize()
        {
            return Task.CompletedTask;
        }

        internal override void Start()
        {
            _backgroundUpdateTask = Task.Run(() => PerformBackgroundUpdateCheck(_backgroundUpdateCts.Token));
            ShutdownManager.Observe(_backgroundUpdateTask);
        }

        internal override async Task<bool> Terminate()
        {
            _backgroundUpdateCts.Cancel();
            if (_backgroundUpdateTask != null)
                await _backgroundUpdateTask;
            return true;
        }

        private static async Task PerformBackgroundUpdateCheck(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(UPDATE_WINDOW_TICKRATE_MILLISECONDS));
            
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken))
                {
#if RELEASE || BETA || IA
//#if DEBUG
                    //Only perform update checks when it's actually properly setup. We already do initial update check at startup.
                    //This is so that it doesn't interfere during the setup process.
                    var conf = ConfigurationManager.GetConfiguration();
                    if (conf.HasCompletedInitialSetup)
                    {
                        Logger.LogToFile("[UpdateService]: Performing background update check...");
                    
                        //Perform update checks...
                        bool updateCheckStatus = await UpdateManager.CheckForUpdate();
                        if (!updateCheckStatus)
                        {
                            Logger.LogToFile("[UpdateService]: Failed to perform update check.");
                        }
                    }
#endif
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                Logger.LogToFile("[UpdateService]: Background service stopped.");
            }
        }
    }
}
