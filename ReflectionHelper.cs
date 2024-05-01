using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ModuleServicesRegistration
{

    public static class ReflectionHelper
	{

		/// <summary>
		///
		/// Get an assembly by name
		/// </summary>
		/// <returns></returns>
		public static IEnumerable<Assembly> GetAssembliesByProductName(string productName)
		{
			// Obtain all assemblies under current Appdomain (Current project)
			IEnumerable<Assembly> asms = AppDomain.CurrentDomain.GetAssemblies();

			// Iterate over all the assemblies
			foreach(Assembly asm in asms)
			{
				// Get custom attribute of the assembly
				AssemblyProductAttribute? assemblyProductAttribute = asm.GetCustomAttribute<AssemblyProductAttribute>();
				// When the attribute is not null and its name is the assembly we want, add it to the return result
				if(assemblyProductAttribute != null && assemblyProductAttribute.Product == productName)
				{
					yield return asm;
				}
			}
		}


        /// <summary>
        ///
        /// Determine the assembly whether a system assembly based on the Assembly type
        /// </summary>
        /// <param name="asm"> Assembly that need to be determined </param>
        /// <returns></returns>
        private static bool IsSystemAssembly(Assembly asm)
		{
			// Get the company attribute of the passed assembly
			AssemblyCompanyAttribute? asmCompanyAttr =  asm.GetCustomAttribute<AssemblyCompanyAttribute>();
			if(asmCompanyAttr == null) // The assembly does not contain any company information
			{
				return false;
			}
			// The assembly contains company information
			string companyName = asmCompanyAttr.Company;
			// Return true if the assembly contains Microsoft, otherwise, return false
			return companyName.Contains("Microsoft");
		}


		/// <summary>
		/// Determine the assembly whether a system assembly based on the assembly path
		/// </summary>
		/// <param name="asmPath"></param>
		/// <returns></returns>
		private static bool IsSystemAssembly(string asmPath)
		{
            // Get the module definition by calling the method from the AsmResolver.DotNet framework
            AsmResolver.DotNet.ModuleDefinition moduleDef = AsmResolver.DotNet.ModuleDefinition.FromFile(asmPath);

            // Get the assembly definition object through the module's Assembly property
            AsmResolver.DotNet.AssemblyDefinition? asmDef = moduleDef.Assembly;
			if(asmDef == null)
			{ // there is no any assembly definition inside the module
				return false;
			}
            // Get the attributes of the assembly
            AsmResolver.DotNet.CustomAttribute? asmAttr = asmDef.CustomAttributes
				.FirstOrDefault(c => c.Constructor?.DeclaringType?.FullName == typeof(AssemblyCompanyAttribute).FullName);
			if(asmAttr == null)
			{ // the assembly does not contain a custom attribute 
				return false;
			}
			// Get the attribute signature
			string? signature = ((AsmResolver.Utf8String?)asmAttr.Signature?.FixedArguments[0]?.Element)?.Value;
			if(signature == null)
			{
				return false;
			}

			return signature.Contains("Microsoft");
        }

		/// <summary>
		/// Determine whether the file is an assembly
		/// </summary>
		/// <param name="file"> The file need to check</param>
		/// <returns></returns>
		private static bool IsManagedAssembly(string file)
		{
			using FileStream fs = File.OpenRead(file);
			using PEReader peReader = new PEReader(fs);
			return peReader.HasMetadata && peReader.GetMetadataReader().IsAssembly;
		}



		/// <summary>
		/// Load assembly by its path
		/// </summary>
		/// <param name="asmPath"></param>
		/// <returns></returns>
        private static Assembly? TryLoadAssembly(string asmPath)
        {
            AssemblyName asmName = AssemblyName.GetAssemblyName(asmPath);
            Assembly? asm = null;
            try
            {
                asm = Assembly.Load(asmName);
            }
            catch (BadImageFormatException ex)
            {
                Debug.WriteLine(ex);
            }
            catch (FileLoadException ex)
            {
                Debug.WriteLine(ex);
            }

            if (asm == null)
            {
                try
                {
                    asm = Assembly.LoadFile(asmPath);
                }
                catch (BadImageFormatException ex)
                {
                    Debug.WriteLine(ex);
                }
                catch (FileLoadException ex)
                {
                    Debug.WriteLine(ex);
                }
            }
            return asm;
        }


        public static IEnumerable<Assembly> GetAllReferenceAssemblies(bool skipSystemAssemblies = true)
		{
			// Since Assembly.GetEntryAssembly() is TestHost when started with MSTest. While GetReferenceAssemblies of TestHost
			// does not contain the project's DLL.
			// It is possible that TestHost is called through HTTP. So we must pass a root rootAssembly

			// get root/entry Assembly
			Assembly? rootAssembly = Assembly.GetEntryAssembly();
			if(rootAssembly == null)
			{
				rootAssembly = Assembly.GetCallingAssembly();
			}
			HashSet<Assembly> returnAssemblies = new HashSet<Assembly>(new AssemblyEquality());
			HashSet<string> loadedAssemblies = new HashSet<string>();
			Queue<Assembly> assembliesToCheck = new Queue<Assembly>();
			assembliesToCheck.Enqueue(rootAssembly);

			if(skipSystemAssemblies && IsSystemAssembly(rootAssembly)){
				returnAssemblies.Add(rootAssembly);
			}

			while (assembliesToCheck.Any())
			{
				Assembly assemblyToCheck = assembliesToCheck.Dequeue();
				foreach(AssemblyName reference in assemblyToCheck.GetReferencedAssemblies())
				{
					if (!loadedAssemblies.Contains(reference.FullName))
					{
						Assembly assembly = Assembly.Load(reference);
						if(skipSystemAssemblies && IsSystemAssembly(rootAssembly))
						{
							continue;
						}
						assembliesToCheck.Enqueue(assembly);
						loadedAssemblies.Add(reference.FullName);
						returnAssemblies.Add(assembly);
					}
				}
			}

            // The following code solves two problems:
            // 1. We cannot get the Assemblies through GetReferenceAssemblies when one assembly whose types are used by reflection
            // not directly initiate.
            // 2. GetEntryAssembly() will return TestHost when we use MSTest to start the project.
            // However, we cannot get its assembly.

            // Therefore, we should compensate by scanning all the .dll files under the directory.

            IEnumerable<string> asmsInBaseDir = Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dlll", new EnumerationOptions { RecurseSubdirectories = true });
			foreach(string asmsPath in asmsInBaseDir)
			{
				if (!IsManagedAssembly(asmsPath))
				{
					continue;
				}
				AssemblyName asmName = AssemblyName.GetAssemblyName(asmsPath);

				// Do not load the assembly if it is already loaded
				if (returnAssemblies.Any(a => AssemblyName.ReferenceMatchesDefinition(a.GetName(), asmName)))
				{
					continue;
				}
				Assembly asm = Assembly.Load(asmName);
				if(skipSystemAssemblies && IsSystemAssembly(asm))
				{
					continue;
				}
				returnAssemblies.Add(asm);
			}

			return returnAssemblies.ToArray();
        }

        class AssemblyEquality : EqualityComparer<Assembly>
        {
            public override bool Equals(Assembly? x, Assembly? y)
            {
                if (x == null && y == null) return true;
                if (x == null || y == null) return false;
                return AssemblyName.ReferenceMatchesDefinition(x.GetName(), y.GetName());
            }

            public override int GetHashCode([DisallowNull] Assembly obj)
            {
                return obj.GetName().FullName.GetHashCode();
            }


        }

    }
}

