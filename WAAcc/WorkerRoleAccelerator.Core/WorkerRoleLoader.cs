namespace WorkerRoleAccelerator.Core
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Reflection;
    using System.Security.Permissions;
    using System.Threading.Tasks;
    using Microsoft.WindowsAzure;
    using Microsoft.WindowsAzure.ServiceRuntime;
    using Microsoft.WindowsAzure.StorageClient;

    public class WorkerRoleLoader
    {
        /// <summary>
        /// Describes the Name of the Worker Role Accelerator AppDomain
        /// </summary>
        private const string AppDomainName = "WorkerRoleAccelerator";
        /// <summary>
        /// Describes the name of the Folder that will be used to configure the Windows Azure Accelerator for Worker Roles
        /// </summary>
        private const string LocalStorageConfigFileFolder = "ConfigFiles";
        /// <summary>
        /// Describes the Name of the Data Configuration Key that should be used for configuring all the Loggers for Windows Azure
        /// </summary>
        private const string DataConfigurationKey = "DataConnectionString";
        /// <summary>
        /// Describes the Name of the Readme file that will be used
        /// </summary>
        private const string ReadmeFileName = "__readme.txt";
        /// <summary>
        /// Describes the Name of the EntryPoint file that will define which Workers to Load
        /// </summary>
        private const string EntryPointFileName = "__entrypoint.txt";
        /// <summary>
        /// Describes the Name of the Worker Role Accelerator base that should be used
        /// </summary>
        private const string MainExceptionIdentifier = "WorkerRoleAccelerator";
        /// <summary>
        /// Describes the Pattern used to generate the Exception File Name that should be used
        /// </summary>
        private const string ExceptionFileNamePattern = "{0}__an_error_occured.txt";

        /// <summary>
        /// Dictionary that will have the information about all the loaded plugins and their associated AppDomains
        /// </summary>
        private Dictionary<String, AppDomain> _pluginDomains;

        /// <summary>
        /// Blob Client that will provide access to the blob storage account that should be used
        /// </summary>
        private readonly CloudBlobClient _blobStorage;

        /// <summary>
        /// Dictionary that has information regarding the plugin last modified date so it can refresh it if a newer version is available
        /// </summary>
        public Dictionary<String, DateTime> LastModified;

        /// <summary>
        /// Constructor that Initializes the Windows Azure Accelerator for Worker Role 
        /// </summary>
        public WorkerRoleLoader()
        {
            LastModified = new Dictionary<String, DateTime>();

            CloudStorageAccount storageAccount = CloudStorageAccount.FromConfigurationSetting(DataConfigurationKey);
            _blobStorage = storageAccount.CreateCloudBlobClient();
        }


        /// <summary>
        /// If there is a new plugin assembly in the storage account specified in "DataConnectionString".
        /// The worker role entry point and its dependencies must be loaded to the container specified int he ServiceConfiguration.cscfg.
        /// </summary>
        /// <returns>An instance of WorkerRoleLoader or null</returns>
        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        public void Poll()
        {
            var containerName = RoleEnvironment.GetConfigurationSettingValue("WorkerRoleEntryPointContainerName");

            if (containerName == null)
            {
                EnsureContainer("WorkerRoleAcceleratorError");
                Log("WorkerRoleAcceleratorError",
                         "No configuration setting was found. Make sure to provide a value for the PluginContainer key in the ServiceConfiguration.cscfg file");

                return;
            }

            EnsureContainer(containerName);
            EnsureReadmeFile(containerName);
            EnsureBlob(containerName, EntryPointFileName);

            string assemblyNamesList = ReadAssemblyNameFromEntryPointFile(containerName, EntryPointFileName);

            if (string.IsNullOrEmpty(assemblyNamesList))
                return;

            // Gets all the plug-in names that are specified inside the Entry Point File and that should be loaded into the Accelerator
            var assemblyNames = assemblyNamesList.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

            // Launches in Parallel the process of running the plug-ins and also update them if they already exist
            foreach (var assemblyName in assemblyNames)
            {
                if (!BlobExists(containerName, assemblyName))
                {
                    Log(containerName,
                             string.Format("Assembly not found:'{0}' in container '{1}'", assemblyName, containerName),
                             assemblyName);
                }
                else
                {
                    var configPath = DownloadConfigurationFileToLocalStorage(containerName, assemblyName + ".config");

                    var modifiedDateFromBlob = GetFileLastModifiedDateFromBlob(containerName, assemblyName);

                    if (!LastModified.ContainsKey(assemblyName))
                        LastModified.Add(assemblyName, DateTime.MinValue);

                    if (modifiedDateFromBlob <= LastModified[assemblyName]) continue;
                    UnloadAppDomain(assemblyName);
                    Execute(containerName, assemblyName, configPath);

                    DeleteBlob(containerName, GetPluginExceptionFileName(assemblyName));
                    LastModified[assemblyName] = modifiedDateFromBlob;
                }

            }
        }

        private void DeleteBlob(string containerName, string blobName)
        {
            _blobStorage.GetContainerReference(containerName)
                .GetBlobReference(blobName)
                .DeleteIfExists();
        }

        private DateTime GetFileLastModifiedDateFromBlob(string containerName, string blobName)
        {
            var blob = _blobStorage.GetContainerReference(containerName)
                                       .GetBlobReference(blobName);
            blob.FetchAttributes();
            return blob.Attributes.Properties.LastModifiedUtc;
        }

        private string DownloadConfigurationFileToLocalStorage(string containerName, string configFileBlobName)
        {
            var blob = _blobStorage.GetContainerReference(containerName)
                                       .GetBlobReference(configFileBlobName);

            string path = null;
            if (blob.Exists())
            {
                LocalResource localStorage = RoleEnvironment.GetLocalResource(LocalStorageConfigFileFolder);
                path = Path.Combine(localStorage.RootPath, configFileBlobName);
                File.WriteAllBytes(path, blob.DownloadByteArray());
            }

            return path;
        }

        private string ReadAssemblyNameFromEntryPointFile(string containerName, string blobName)
        {
            string content = _blobStorage.GetContainerReference(containerName)
                                            .GetBlobReference(blobName)
                                            .DownloadText();

            return content;
        }

        private bool BlobExists(string containerName, string blobName)
        {
            var blob = _blobStorage.GetContainerReference(containerName)
                                       .GetBlobReference(blobName);

            return blob.Exists();
        }

        private void Log(string container, string message, string exceptionModuleName = MainExceptionIdentifier)
        {
            try
            {
                _blobStorage.GetContainerReference(container)
                           .GetBlobReference(GetPluginExceptionFileName(exceptionModuleName))
                           .UploadText(String.Format("{0} - {1}", DateTime.UtcNow, message));
            }
            catch (StorageClientException ex)
            {
                Trace.TraceError(ex.Message);
            }
        }

        private static string GetPluginExceptionFileName(string exceptionModuleName)
        {
            return String.Format(ExceptionFileNamePattern, exceptionModuleName);
        }

        private void EnsureReadmeFile(string containerName)
        {
            if (BlobExists(containerName, ReadmeFileName)) return;
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WorkerRoleAccelerator.Core.Readme.txt");
            if (stream == null) return;
            var readme = new StreamReader(stream).ReadToEnd();

            _blobStorage.GetContainerReference(containerName)
                .GetBlobReference(ReadmeFileName)
                .UploadText(readme);
        }

        private void EnsureBlob(string containerName, string blobName)
        {
            if (!BlobExists(containerName, blobName))
            {
                _blobStorage.GetContainerReference(containerName)
                            .GetBlobReference(blobName)
                            .UploadByteArray(new byte[] { });
            }
        }

        private void EnsureContainer(string containerName)
        {
            CloudBlobContainer container = _blobStorage.GetContainerReference(containerName);
            container.CreateIfNotExist();
        }

        /// <summary>
        /// If a new plugin assembly is detected, this method sets a new application domain and loads the plugin on it.
        /// Finally, creates an instance of the proxy and call its methods (OnStart and Run) in a new thread.
        /// </summary>
        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        private void Execute(string containerName, string entryPointAssemblyName, string configFilePath)
        {
            try
            {
                if (_pluginDomains == null || !_pluginDomains.ContainsKey(entryPointAssemblyName))
                {
                    // setup a new application domain and load the plug-in
                    var setupInfo = new AppDomainSetup
                    {
                        ApplicationBase = AppDomain.CurrentDomain.BaseDirectory,
                        ConfigurationFile = !string.IsNullOrEmpty(configFilePath) ? configFilePath : null
                    };

                    if (_pluginDomains == null)
                        _pluginDomains = new Dictionary<String, AppDomain>();

                    _pluginDomains.Add(entryPointAssemblyName, AppDomain.CreateDomain(entryPointAssemblyName, null, setupInfo));
                }

                object[] args = { containerName, entryPointAssemblyName };
                var proxy = _pluginDomains[entryPointAssemblyName].CreateInstanceAndUnwrap(typeof(ProxyRoleEntryPoint).Assembly.FullName,
                                                                      typeof(ProxyRoleEntryPoint).FullName,
                                                                      false,
                                                                      BindingFlags.Default,
                                                                      null,
                                                                      args,
                                                                      null,
                                                                      null) as ProxyRoleEntryPoint;

                new Task(() =>
                {
                    try
                    {
                        if (proxy != null && proxy.OnStart())
                        {
                            proxy.Run();
                        }
                    }
                    catch (AppDomainUnloadedException ex)
                    {
                        // A new Plugin was found and the AppDomain was unloaded
                        Log(containerName,
                                 string.Format("'Plugin.Run()' method throws and AppDomainUnloaded Exception. Exception message: '{0}' on plug-in {1}.", ex, entryPointAssemblyName),
                                 entryPointAssemblyName);
                    }
                    catch (Exception ex)
                    {
                        Log(containerName,
                                 string.Format("'Plugin.Run()' method throws and unhandled exception. Exception message: '{0}' on plug-in {1}.", ex, entryPointAssemblyName),
                                 entryPointAssemblyName);

                        UnloadAppDomain(entryPointAssemblyName);
                    }
                }).Start();
            }
            catch (Exception ex)
            {
                Log(containerName,
                         string.Format("Unrecoverable error in plug-in '{0}'.\n{1}", entryPointAssemblyName, ex),
                         entryPointAssemblyName);
                UnloadAppDomain(entryPointAssemblyName);
            }
        }

        /// <summary>
        /// Unload the Application Domain.
        /// This will abort any threads currently executing in the domain.
        /// </summary>
        public void UnloadAppDomain()
        {
            UnloadAppDomain(AppDomainName);
        }

        public void UnloadAppDomain(String appDomain)
        {
            if (_pluginDomains != null && _pluginDomains.ContainsKey(appDomain))
            {
                Trace.TraceInformation("Unloading AppDomain for plugin '{0}'.", appDomain);
                AppDomain.Unload(_pluginDomains[appDomain]);
                _pluginDomains.Remove(appDomain);
                LastModified.Remove(appDomain);
            }
        }
    }
}