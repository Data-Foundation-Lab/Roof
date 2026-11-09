using System.Diagnostics;

namespace Roof.Common.Tools
{
    public enum LogSeverity
    {
        Neutral,
        Warning,
        Danger
    }

    public class Logger
    {
        private const string LOG_FILE_TIMESTAMP_FORMAT = "yyyy-MM-ddTHHmmss";
        private const string LOG_TIMESTAMP_FORMAT = "h:mm:ss tt";
        private static string? _preferredLogFileDirectoryPath = null;
        private static string _logFileDirectoryPath => _preferredLogFileDirectoryPath ?? Path.Combine(Directory.GetCurrentDirectory(), "Logs");
        public static string LogFileDirectoryPath => _logFileDirectoryPath;

        private static string _logFileName = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToString(LOG_FILE_TIMESTAMP_FORMAT);
        private static string _logFilePath => Path.Combine(_logFileDirectoryPath, $"{_logFileName}.log");
        public static string LogFilePath => _logFilePath;
        private static bool _hasCompletedLogDirectoryCheck = false;
        
        private static readonly object _lock = new object();
        private static readonly object _fileLock = new object();

        public static void SetPreferredLogFileDirectoryPath(string logFileDirectoryPath)
        {
            _preferredLogFileDirectoryPath = logFileDirectoryPath;
        }

        public static void RotateLogFile()
        {
            lock (_fileLock)
            {
                _logFileName = DateTime.Now.ToString(LOG_FILE_TIMESTAMP_FORMAT);
            }
        }
        
        static Logger()
        {
            
        }
        
        private static void CheckForLogDirectory()
        {
            if (!_hasCompletedLogDirectoryCheck)
            {
                if (!Directory.Exists(_logFileDirectoryPath))
                {
                    try
                    {
                        Directory.CreateDirectory(_logFileDirectoryPath);
                    }
                    catch (Exception ex)
                    {
                        Log($"Exception occured while creating a folder for logging at: {_logFileDirectoryPath}." +
                            $"\n[{DateTime.Now.ToString(LOG_TIMESTAMP_FORMAT)}]: Exception - {ex.Message} ", LogSeverity.Danger);
                    }
                }

                _hasCompletedLogDirectoryCheck = true;
            }
        }

        public static void Log(string dataToLog, LogSeverity severity = LogSeverity.Neutral)
        {
            lock (_lock)
            {
                if (string.IsNullOrEmpty(dataToLog))
                {
                    if (Debugger.IsAttached)
                        Debug.WriteLine($"[{DateTime.Now.ToString(LOG_TIMESTAMP_FORMAT)}]: StringNullOrEmpty occurred while logging output.");
                    else
                        Console.WriteLine($"[{DateTime.Now.ToString(LOG_TIMESTAMP_FORMAT)}]: StringNullOrEmpty occurred while logging output.");
                    
                    return;
                }

                var consoleColor = severity switch
                {
                    LogSeverity.Neutral => ConsoleColor.Cyan,
                    LogSeverity.Warning => ConsoleColor.Yellow,
                    LogSeverity.Danger => ConsoleColor.Red,
                    _ => ConsoleColor.White,
                };
                var temp = Console.ForegroundColor;
                Console.ForegroundColor = consoleColor;

                if (Debugger.IsAttached)
                    Debug.WriteLine($"({DateTime.Now.ToString(LOG_TIMESTAMP_FORMAT)}) {dataToLog}");
                else
                    Console.WriteLine($"({DateTime.Now.ToString(LOG_TIMESTAMP_FORMAT)}) {dataToLog}");

                Console.ForegroundColor = temp;
            }
        }
        
        public static void LogToFile(string dataToLog, LogSeverity severity = LogSeverity.Neutral)
        {
            CheckForLogDirectory();
            
            //Logging to the console should only occur when we’re in DEBUG mode.
#if DEBUG
            Log(dataToLog, severity);
#endif
            
            lock (_fileLock)
            {
                try
                {
                    using (var sw = File.AppendText(_logFilePath))
                    {
                        if (string.IsNullOrEmpty(dataToLog))
                        {
                            sw.WriteLine($"[{DateTime.Now.ToString(LOG_TIMESTAMP_FORMAT)}]({severity}) - StringNullOrEmpty occured while logging the output.");
                        }
                        else
                        {
                            sw.WriteLine($"[{DateTime.Now.ToString(LOG_TIMESTAMP_FORMAT)}]({severity}) - {dataToLog}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Exception occured while writing to the log file at: {_logFilePath}." +
                        $"\n[{DateTime.Now.ToString(LOG_TIMESTAMP_FORMAT)}] - Exception: {ex.Message}", LogSeverity.Danger);
                }
            }
        }

        public static void LogException(string preExceptionInfo, Exception exceptionObject)
        {
            Log($"[Exception Info]" +
                preExceptionInfo +
                $"\n[{DateTime.Now.ToString(LOG_TIMESTAMP_FORMAT)}] - Message: {exceptionObject.Message}" +
                $"\n[{DateTime.Now.ToString(LOG_TIMESTAMP_FORMAT)}] - Source: {exceptionObject.Source}" +
                $"\n[{DateTime.Now.ToString(LOG_TIMESTAMP_FORMAT)}] - InnerException: {(exceptionObject.InnerException != null ? exceptionObject.InnerException.Message : "N/A")}" +
                $"\n[{DateTime.Now.ToString(LOG_TIMESTAMP_FORMAT)}] - Stack Trace: " +
                $"\n{exceptionObject.StackTrace}", LogSeverity.Danger);
        }

        public static void LogExceptionToFile(string preExceptionInfo, Exception exceptionObject)
        {
            CheckForLogDirectory();
            
            //Logging to the console should only occur when we’re in DEBUG mode.
#if DEBUG
            LogException(preExceptionInfo, exceptionObject);
#endif

            lock (_fileLock)
            {
                try
                {
                    using (var sw = File.AppendText(_logFilePath))
                    {
                        if (string.IsNullOrEmpty(preExceptionInfo))
                        {
                            sw.WriteLine($"[{DateTime.Now.ToString(LOG_TIMESTAMP_FORMAT)}]({LogSeverity.Danger}) - StringNullOrEmpty occured while logging the output.");
                        }
                        else
                        {
                            sw.WriteLine($"[{DateTime.Now.ToString(LOG_TIMESTAMP_FORMAT)}]({LogSeverity.Danger}) - {preExceptionInfo}" +
                                         $"\n[{DateTime.Now.ToString(LOG_TIMESTAMP_FORMAT)}]({LogSeverity.Danger}) - Message: {exceptionObject.Message}" +
                                         $"\n[{DateTime.Now.ToString(LOG_TIMESTAMP_FORMAT)}]({LogSeverity.Danger}) - Source: {exceptionObject.Source}" +
                                         $"\n[{DateTime.Now.ToString(LOG_TIMESTAMP_FORMAT)}]({LogSeverity.Danger}) - InnerException: {(exceptionObject.InnerException != null ? exceptionObject.InnerException.Message : "N/A")}" +
                                         $"\n[{DateTime.Now.ToString(LOG_TIMESTAMP_FORMAT)}]({LogSeverity.Danger}) - Stack Trace: " +
                                         $"\n{exceptionObject.StackTrace}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Exception occured while writing to the log file at: {_logFilePath}." +
                        $"\n[{DateTime.Now.ToString(LOG_TIMESTAMP_FORMAT)}]: Exception: {ex.Message} ", LogSeverity.Danger);
                }
            }
        }
    }
}