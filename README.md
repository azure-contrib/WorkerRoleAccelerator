WorkerRoleAccelerator
=====================

First I looked at the Windows Azure Accelerator for Web Roles and thought that was really good stuff and continued working and trying to find an Accelerator for Worker roles and I found it, http://blogs.southworks.net/mwoloski/2011/07/22/windows-azure-accelerator-for-worker-role-update-your-workers-faster/, done by Mattias Woloski from Southworks and started looking at it and trying to find how I would use it, and suddenly I found myself seeing some things that could be improved like: Support for multiple Workers in the same Accelerator Running each Worker in their own AppDomain so they could be isolated from others Have a VSIX Package where I could really start this and have all of that working 

Having seen this I thought that I would do it and talked to Mattias about it and he liked the idea and so here it is. The codebase will always be available in one only place, that will be the GitHub but the VSIX packages will appear here to make things easier.

Currently Known issues:
•In your __entrypoint.txt file that you'll place inside your Windows Azure Storage Container you should place your different dll's on different lines

In order to know how to work with it check the Readme.txt file that is inside the project or follow my blog that will have entries about this.

The license is AS IS. So there's no support or any responsibility about this and you should run it at your own risk. This was done only to help you achieve better results but not a commercial product. If you have any interesting ideas about this, please feel free to contact me or drop here a comment.

How it works? 1. Deploy the Worker Role Accelerator into Windows Azure. 2. Create your Worker Roles which you want to join together in independent Worker Role projects 3. Upload the worker role assembly that implements the RoleEntryPoint class 4. Upload any dependent assemblies required by the main assembly (that are not in the GAC) 5. Optionally, a configuration file (WorkerRoleName.dll.config) to define settings for your worker role. 6. Open the __entrypoint.txt present on the Storage Account being used and write the name of the assembly that implements the RoleEntryPoint class (this is the worker role main assembly mentioned as the first item in the current list)

The worker role accelerator loads the DLL specified in the __entrypoint.txt file and then executes the code in this 
