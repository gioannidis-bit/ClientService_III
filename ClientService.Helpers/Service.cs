// File: Service.cs
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Management;        // for ManagementEventWatcher
using System.Reflection;
using System.ServiceProcess;
using System.Threading;        // for ThreadPool
using ClientService.Classes.Devices.AccessIS;
using ClientService.Classes.Factories;
using ClientService.Classes.Interfaces;
using ClientService.Models.Base;
using log4net;

namespace ClientService.Helpers
{
    internal class Service : ServiceBase, IDisposable
    {
        private static readonly ILog logger = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private IContainer components;
        private UsbAutoReconnect usbWatcher;

        public static ConfigurationModel config;
        public static IScanner scanner;

        public Service(string execName, string serviceName)
        {
            base.ServiceName = serviceName;
            var procName = Process.GetCurrentProcess().ProcessName;
            logger.Info($"Process: {procName}");
        }

        protected override void OnStart(string[] args) => Start(args);
        protected override void OnStop() => Stop();

        public void Start(string[] args)
        {
            logger.Info("Initializing Protel Document Scanner");
            config = new ConfigurationHelper().GetConfiguration();
            scanner = new ConcreteScannerFactory().GetScanner(config);
            scanner.Connect();

            InitializeUsbAutoReconnect();
        }

        public new void Stop()
        {
            try
            {
                usbWatcher?.Dispose();
                usbWatcher = null;
                scanner?.Disconnect();
            }
            catch (Exception ex)
            {
                logger.Warn("Error during Stop() cleanup", ex);
            }

            logger.Info(Environment.UserInteractive
                ? "Application closed as Console Application"
                : "Application stopped as Windows Service");
        }

        private void InitializeUsbAutoReconnect()
        {
            if (scanner is AccessISScanner accessScanner)
            {
                usbWatcher = new UsbAutoReconnect(accessScanner);
                usbWatcher.Start();
                logger.Info("USB auto-reconnect watcher started");
            }
            else
            {
                logger.Warn("USB auto-reconnect not initialized: scanner is not AccessISScanner");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
                usbWatcher?.Dispose();
                (scanner as IDisposable)?.Dispose();
            }
            base.Dispose(disposing);
        }

        void IDisposable.Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    // ───── UsbAutoReconnect ───────────────────────────────
    public class UsbAutoReconnect : IDisposable
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(UsbAutoReconnect));
        private readonly AccessISScanner scanner;
        private readonly ManagementEventWatcher insertWatcher;
        private volatile bool isReconnecting;

        // Debounce: ignore any new event within 2s of the last one
        private DateTime lastEvent = DateTime.MinValue;
        private static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(2);

        private const string VID = "vid_0db5";
        private const string PID = "pid_013e";

        public UsbAutoReconnect(AccessISScanner scannerInstance)
        {
            scanner = scannerInstance ?? throw new ArgumentNullException(nameof(scannerInstance));

            var insertQuery = new WqlEventQuery(
                "SELECT * FROM __InstanceCreationEvent WITHIN 2 " +
                "WHERE TargetInstance ISA 'Win32_USBControllerDevice'");
            insertWatcher = new ManagementEventWatcher(insertQuery);
            insertWatcher.EventArrived += OnUsbInserted;
        }

        public void Start() => insertWatcher.Start();
        public void Stop() => insertWatcher.Stop();

        private void OnUsbInserted(object sender, EventArrivedEventArgs e)
        {
            // first, debounce
            var now = DateTime.UtcNow;
            if (now - lastEvent < DebounceWindow)
                return;
            lastEvent = now;

            // then filter by VID/PID
            var mbo = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var dependent = (mbo["Dependent"] as string)?.ToLower() ?? "";
            if (!dependent.Contains(VID) || !dependent.Contains(PID))
                return;

            // finally, queue the reconnect
            if (isReconnecting) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                isReconnecting = true;
                try
                {
                    logger.Info("[UsbAutoReconnect] Scanner replugged → calling Reconnect()");
                    scanner.Reconnect();
                }
                catch (Exception ex)
                {
                    logger.Warn("[UsbAutoReconnect] Reconnect failed", ex);
                }
                finally
                {
                    isReconnecting = false;
                }
            });
        }

        public void Dispose()
        {
            Stop();
            insertWatcher.EventArrived -= OnUsbInserted;
            insertWatcher.Dispose();
        }
    }

}
