To deploy a role to the worker role accelerator, copy the following files to the current blob storage container:
 
- Upload the worker role assembly that implements the RoleEntryPoint class
- Upload any dependent assemblies required by the main assembly (that are not in the GAC)
- Optionally, a configuration file (app.config) to define settings for your worker role.
- Open the __entrypoint.txt file and write the name of the assembly that implements the RoleEntryPoint class (this is the worker role main assembly mentioned as the first item in the current list)
 
The worker role accelerator loads the DLL specified in the __entrypoint.txt file and then executes the code in this class.