namespace WorkerRoleAccelerator.Core
{
    using System;
    using System.Diagnostics;
    using System.Linq;
    using System.Reflection;
    using Microsoft.WindowsAzure;
    using Microsoft.WindowsAzure.Diagnostics;
    using Microsoft.WindowsAzure.ServiceRuntime;
    using Microsoft.WindowsAzure.StorageClient;

    public class ProxyRoleEntryPoint : MarshalByRefObject
    {
        private readonly RoleEntryPoint _workerRole;

        public ProxyRoleEntryPoint(string containerName, string entryPointAssemblyName)
        {
            Trace.Listeners.Add(new DiagnosticMonitorTraceListener());

            CloudStorageAccount.SetConfigurationSettingPublisher((configName, configSetter) => configSetter(RoleEnvironment.GetConfigurationSettingValue(configName)));

            var storageAccount = CloudStorageAccount.FromConfigurationSetting("DataConnectionString");
            var blobStorage = storageAccount.CreateCloudBlobClient();

            var workerAssemblyBytes = blobStorage.GetContainerReference(containerName)
                                                    .GetBlobReference(entryPointAssemblyName)
                                                    .DownloadByteArray();

            var entryPoint = Assembly.Load(workerAssemblyBytes);

            if (entryPoint != null)
            {
                var roleEntryPointType = entryPoint.GetTypes().FirstOrDefault(t => typeof(RoleEntryPoint).IsAssignableFrom(t));
                if (roleEntryPointType == null)
                {
                    throw new ArgumentException("The assembly does not contain a RoleEntryPoint derived class");
                }

                _workerRole = entryPoint.CreateInstance(roleEntryPointType.FullName) as RoleEntryPoint;
            }

            AppDomain.CurrentDomain.AssemblyResolve += (sender, eventArgs) =>
            {
                var dependencyName = eventArgs.Name.Split(',')[0] + ".dll";
                var dependency = blobStorage.GetContainerReference(containerName)
                                                  .GetBlobReference(dependencyName);

                if (!dependency.Exists())
                {
                    throw new ArgumentException(string.Format("Assembly '{0}' does not exists in container '{1}'", dependencyName, containerName));
                }

                var dependencyAssemblyBytes = dependency.DownloadByteArray();
                var dependencyAssembly = Assembly.Load(dependencyAssemblyBytes);

                return dependencyAssembly;
            };
        }

        public bool OnStart()
        {
            if (_workerRole == null)
            {
                return false;
            }

            return _workerRole.OnStart();
        }

        public void OnStop()
        {
            if (_workerRole != null)
            {
                _workerRole.OnStop();
            }
        }

        public void Run()
        {
            if (_workerRole != null)
            {
                _workerRole.Run();
            }
        }
    }
}