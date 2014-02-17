using Microsoft.WindowsAzure;

namespace WorkerRoleAccelerator.Core
{
    using System;
    using System.Diagnostics;
    using System.Net;
    using System.Security.Permissions;
    using System.Threading;
    using Microsoft.WindowsAzure.Diagnostics;
    using Microsoft.WindowsAzure.ServiceRuntime;

    public class WorkerRole : RoleEntryPoint
    {
        /// <summary>
        /// Worker Role Accelerator Run method. Called by Windows Azure after the role instance has been initialized.
        /// This method serves as the main thread of execution for the role.
        /// It has an infinite loop in order to check if new plugins are uploaded to the Azure Blob Storage.
        /// </summary>
        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        public override void Run()
        {
            Trace.TraceInformation("Worker Role Accelerator entry point was called");

            var loader = new WorkerRoleLoader();

            while (true)
            {
                Trace.TraceInformation("Worker Role Accelerator is Working");

                loader.Poll();

                Thread.Sleep(30000);
            }
        }

        /// <summary>
        /// Called by Windows Azure to initialize the role instance.
        /// </summary>
        /// <returns>
        /// True if initialization succeeds, False if it fails. The default implementation returns True.
        /// </returns>
        public override bool OnStart()
        {
            CloudStorageAccount.SetConfigurationSettingPublisher((configName, configSetter) => configSetter(RoleEnvironment.GetConfigurationSettingValue(configName)));

            // Start the Diagnostic Monitor
            var dmc = DiagnosticMonitor.GetDefaultInitialConfiguration();
#if DEBUG
            dmc.Logs.ScheduledTransferPeriod = TimeSpan.FromSeconds(1);
#elif RELEASE
			dmc.Logs.ScheduledTransferPeriod = TimeSpan.FromSeconds(30);
#endif
            dmc.Logs.ScheduledTransferLogLevelFilter = LogLevel.Information;
            DiagnosticMonitor.Start("Microsoft.WindowsAzure.Plugins.Diagnostics.ConnectionString", dmc);

            // Set the maximum number of concurrent connections
            ServicePointManager.DefaultConnectionLimit = 12;

            return base.OnStart();
        }
    }
}