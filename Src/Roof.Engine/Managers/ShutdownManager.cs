using System.Runtime.InteropServices;
using Roof.Common.Tools;
using Roof.Engine.Services;
using Roof.Engine.Tools;

namespace Roof.Engine.Managers
{
    internal static class ShutdownManager
    {
        private static readonly CancellationTokenSource _shutdownCts = new();
        private static readonly Lazy<Task> _shutdownTask = new(() => Task.Run(PerformShutdown));
        private static volatile bool _rebootRequested;

        private static readonly object _clientWorkLock = new();
        private static readonly HashSet<Task> _clientWork = new();

        internal static void RunClientWork(Func<Task> work)
        {
            lock (_clientWorkLock)
            {
                if (Token.IsCancellationRequested)
                    return;
                var task = work();
                _clientWork.Add(task);
                Observe(task);
                _ = task.ContinueWith(completed =>
                {
                    lock (_clientWorkLock)
                        _clientWork.Remove(completed);
                }, TaskScheduler.Default);
            }
        }

        internal static CancellationToken Token => _shutdownCts.Token;

        internal static IDisposable? RegisterSignals()
        {
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                RequestShutdown();
            };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => WaitForShutdown();
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                Environment.ExitCode = 1;
                if (e.ExceptionObject is Exception ex)
                    Logger.LogExceptionToFile("[Program]: Unhandled engine failure.", ex);
                WaitForShutdown();
            };

            if (OperatingSystem.IsWindows())
                return null;

            return PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                RequestShutdown();
            });
        }

        internal static void RequestReboot()
        {
            // The client handler must return so shutdown can finish active requests.
            _rebootRequested = true;
            RequestShutdown();
        }

        internal static void RequestShutdown()
        {
            _shutdownCts.Cancel();
        }

        internal static void Observe(Task task)
        {
            _ = task.ContinueWith(failedTask =>
            {
                Environment.ExitCode = 1;
                Logger.LogExceptionToFile("[Program]: Background service failed.", failedTask.Exception!);
                RequestShutdown();
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }

        internal static Task Shutdown()
        {
            RequestShutdown();
            return _shutdownTask.Value;
        }

        private static void WaitForShutdown()
        {
            // Process-exit callbacks cannot await, and must not block termination indefinitely.
            try
            {
                if (!Shutdown().Wait(TimeSpan.FromSeconds(60)))
                    Environment.ExitCode = 1;
            }
            catch { Environment.ExitCode = 1; }
        }

        private static async Task PerformShutdown()
        {
            Logger.LogToFile("[Program]: Engine shutting down...");
            try
            {
                await ServiceManager.TerminateServices();
                Task[] clientWork;
                lock (_clientWorkLock)
                    clientWork = _clientWork.ToArray();
                try
                {
                    await Task.WhenAll(clientWork).WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (Exception ex)
                {
                    Environment.ExitCode = 1;
                    Logger.LogExceptionToFile("[Program]: Failed to finish active client requests.", ex);
                }

                if (!await ConfigurationManager.SaveLoadedConfiguration())
                {
                    Environment.ExitCode = 1;
                    Logger.LogToFile("[Program]: Failed to save configuration during shutdown.", LogSeverity.Danger);
                }
            }
            catch (Exception ex)
            {
                Environment.ExitCode = 1;
                Logger.LogExceptionToFile("[Program]: Shutdown failed.", ex);
            }
            finally
            {
                // File logging disposes (and flushes) its writer after every entry.
                Logger.LogToFile("[Program]: Engine shutdown completed.");
                Console.Out.Flush();
                Console.Error.Flush();
            }

            if (_rebootRequested)
            {
                var rebootResult = await Shell.ExecuteAsync("sudo reboot");
                if (rebootResult.Contains("Error:", StringComparison.OrdinalIgnoreCase))
                {
                    Environment.ExitCode = 1;
                    Logger.LogToFile($"[Program]: Failed to reboot after shutdown. NativeResult: {rebootResult}", LogSeverity.Danger);
                }
            }
        }
    }
}
