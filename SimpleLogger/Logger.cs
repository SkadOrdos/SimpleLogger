using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace SimpleLogger
{
    public class LogArgs : EventArgs
    {
        /// <summary>
        /// Allows cancel writing log to file
        /// </summary>
        public bool Cancel;

        /// <summary>
        /// Message to write in log
        /// </summary>
        public string Message;

        /// <summary>
        /// Log mark
        /// </summary>
        public string Label;

        /// <summary>
        /// Log time
        /// </summary>
        public readonly DateTime Time;

        /// <summary>
        /// New log object
        /// </summary>
        /// <param name="message">Message to log</param>
        /// <param name="label">Mark</param>
        public LogArgs(string message, string label)
        {
            Label = label;
            Message = message;
            Time = DateTime.Now;
        }
    }


    [Serializable]
    public class LogSettings
    {
        public LogSettings()
        {
            LogFileName = "log_";
            LogFolderPath = "Logs";
            MaxFileSizeInMB = 100;
        }

        /// <summary>
        /// File name to logs
        /// </summary>
        public string LogFileName { get; set; }

        private string logFolderPath;
        /// <summary>
        /// Path to logs
        /// </summary>
        public string LogFolderPath
        {
            get { return logFolderPath; }
            set
            {
                if (String.IsNullOrEmpty(value)) value = "Logs";
                logFolderPath = value;
            }
        }

        /// <summary>
        /// Max file size in MBytes
        /// </summary>
        public int MaxFileSizeInMB { get; set; }

        /// <summary>
        /// Max file size in bytes
        /// </summary>
        public long MaxFileSizeInBytes
        { get { return MaxFileSizeInMB * 1048576; } }


        public object Clone()
        {
            return MemberwiseClone();
        }
    }


    public class Logger : IDisposable
    {
        public LogSettings Settings { get; protected set; }

        protected Thread LogProcessor;
        protected AutoResetEvent WaitData;
        protected ConcurrentQueue<LogArgs> Queue;

        public static readonly object SyncRoot = new Object();

        public const string LoggerSettingsName = "logger";
        public const string LoggerSettingsExt = ".xml";
        public const string LoggerFileExt = ".log";

        #region// Log labels

        public const string DebugString = "DEBUG";
        public const string InfoString = "INFO";
        public const string WarnString = "WARN";
        public const string ErrorString = "ERROR";
        public const string FatalString = "FATAL";

        #endregion

        public static string DefaultSettingsFileName
        {
            get { return LoggerSettingsName + LoggerSettingsExt; }
        }

        /// <summary>
        /// Encoding for logs
        /// </summary>
        public Encoding Encoding = System.Text.Encoding.UTF8;


        volatile static Logger log2file;
        /// <summary>
        /// Logger instance by loaded settings
        /// </summary>
        public static Logger Instance
        {
            get
            {
                lock (SyncRoot)
                {
                    if (log2file == null)
                    {
                        log2file = new Logger();
                        log2file.LoadSettings(DefaultSettingsFileName);
                    }

                }
                return log2file;
            }
        }

        /// <summary>
        /// Create logger by settings
        /// </summary>
        /// <param name="settings">Logger settings</param>
        /// <returns>Logger instance</returns>
        public static Logger CreateSpecificInstance(LogSettings settings)
        {
            lock (SyncRoot)
            {
                if (log2file != null) log2file.Dispose();

                log2file = new Logger();
                log2file.LoadSettings(settings);
            }

            return log2file;
        }


        /// <summary>
        /// Get new logger by settings
        /// </summary>
        /// <param name="settingsPath">Path to settings file</param>
        /// <returns></returns>
        public static Logger GetLogger(string settingsPath)
        {
            var newLogger = new Logger();
            newLogger.LoadSettings(settingsPath);
            return newLogger;
        }

        /// <summary>
        /// Get new logger by settings
        /// </summary>
        /// <param name="logSettings">Settings</param>
        /// <returns></returns>
        public static Logger GetLogger(LogSettings logSettings)
        {
            var newLogger = new Logger();
            newLogger.LoadSettings(logSettings);
            return newLogger;
        }


        protected Logger()
        {
            WaitData = new AutoResetEvent(false);
            Queue = new ConcurrentQueue<LogArgs>();

            LogProcessor = new Thread(new ThreadStart(LogLoop));
            LogProcessor.Priority = ThreadPriority.BelowNormal;
        }

        /// <summary>
        /// Load settings and start
        /// </summary>
        /// <param name="settFile">Path to settings file</param>
        public void LoadSettings(string settFile)
        {
            try
            {
                if (!File.Exists(settFile))
                {
                    Settings = new LogSettings();
                    SaveToXml(settFile, Settings);
                    Console.WriteLine("Logger settings not found! Create a default logger settings file.");
                }
                else Settings = LoadFromXml(settFile);

                LoadSettings(Settings);
            }
            catch (Exception e)
            {
                Console.WriteLine("Can't load logger settings file: " + e.Message);
            }
        }

        /// <summary>
        /// Load settings and start
        /// </summary>
        /// <param name="settings">Settings</param>
        public void LoadSettings(LogSettings settings)
        {
            if (settings != null)
            {
                Settings = (LogSettings)settings.Clone();
                Directory.CreateDirectory(Settings.LogFolderPath);
            }

            Start();
        }


        #region// Save/Load settings

        public static void SaveToXml(String fileName, LogSettings SerializableObject)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(LogSettings));
            using (TextWriter textWriter = new StreamWriter(fileName))
            {
                serializer.Serialize(textWriter, SerializableObject);
            }
        }

        public static LogSettings LoadFromXml(String fileName)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(LogSettings));
            using (TextReader textReader = new StreamReader(fileName))
            {
                return (LogSettings)serializer.Deserialize(textReader);
            }
        }

        #endregion

        private bool isStarted;
        /// <summary>
        /// Start logger
        /// </summary>
        public void Start()
        {
            if (!isDisposing && !isStarted)
            {
                isStarted = true;

                LogProcessor.Start();
                Info("Logger started");
            }
        }

        /// <summary>
        /// Stop logger
        /// </summary>
        public void Stop()
        {
            if (!isDisposing && isStarted)
            {
                Info("Logger stopped");
                isStarted = false;
            }
        }


        private bool isDisposing = false;
        /// <summary>
        /// Dispose and release all resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            Stop();
            isDisposing = true;
            WaitData.Set();
        }


        /// <summary>
        /// Push log to process
        /// </summary>
        /// <param name="o">Logger args</param>
        private void Push(LogArgs o)
        {
            Queue.Enqueue(o);
            WaitData.Set();
        }

        /// <summary>
        /// Process log
        /// </summary>
        private void LogLoop()
        {
            while (!isDisposing)
            {
                WaitData.WaitOne();

                LogArgs logObject;
                while (Queue.TryDequeue(out logObject))
                {
                    __WriteToLog(logObject);
                }
            }
        }


        private string lastFile;
        /// <summary>
        /// Write to log from thread
        /// </summary>
        private void __WriteToLog(object o)
        {
            LogArgs args = (LogArgs)o;
            string label = args.Label;

            if (!String.IsNullOrEmpty(label))
                label = String.Concat(label, " ");
            else label = String.Empty;

            if (args.Message != null && args.Message.Length > 0)
            {
                string logLine = String.Concat(label, "[", args.Time.ToString("dd MMM yyyy HH:mm:ss\\.fff"), "] ", args.Message, Environment.NewLine);

                try
                {
                    if (!File.Exists(lastFile)) Directory.CreateDirectory(Settings.LogFolderPath);

                    if (!String.IsNullOrEmpty(lastFile))
                    {
                        FileInfo lf = new FileInfo(lastFile);
                        if (lf.Exists && lf.Length > Settings.MaxFileSizeInBytes)
                        {
                            lastFile = GetNewFileName();
                        }
                    }
                    else lastFile = GetNewFileName();

                    using (var fileStream = new StreamWriter(lastFile, true, Encoding))
                        fileStream.Write(logLine);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("FATAL: Write log to file!\n" + ex);
                }
            }
        }


        private string GetNewFileName()
        {
            string fileTime = DateTime.UtcNow.ToString("yyyyMMdd.HHmmss");
            return Path.Combine(Settings.LogFolderPath, String.Concat(Settings.LogFileName, fileTime, LoggerFileExt));
        }


        #region// WriteByLevel

        public void Debug(object obj)
        {
            if (obj != null) Debug(obj.ToString());
        }

        public void Debug(string msg)
        {
            Write(msg, DebugString);
        }

        public void Info(object obj)
        {
            if (obj != null) Info(obj.ToString());
        }

        public void Info(string msg)
        {
            Write(msg, InfoString);
        }

        public void Warn(object obj)
        {
            if (obj != null) Warn(obj.ToString());
        }

        public void Warn(string msg)
        {
            Write(msg, WarnString);
        }

        public void Error(object obj)
        {
            if (obj != null) Error(obj.ToString());
        }

        public void Error(string msg)
        {
            Write(msg, ErrorString);
        }

        public void Fatal(object obj)
        {
            if (obj != null) Fatal(obj.ToString());
        }

        public void Fatal(string msg)
        {
            Write(msg, FatalString);
        }

        #endregion


        /// <summary>
        /// Write to log
        /// </summary>
        /// <param name="msg">Message</param>
        public void Write(string msg)
        {
            Write(msg, String.Empty);
        }


        /// <summary>
        /// Raises before write log
        /// </summary>
        public event EventHandler<LogArgs> OnWrite = delegate { };

        /// <summary>
        /// Write to log
        /// </summary>
        /// <param name="msg">Message</param>
        /// <param name="label">Label (if label == ERROR or label == FATAL, log will be dump to file imediatly)</param>
        public void Write(string msg, string label)
        {
            var evArgs = new LogArgs(msg, label);
            OnWrite(this, evArgs);
            if (!evArgs.Cancel)
                Push(evArgs);
        }
    }
}
