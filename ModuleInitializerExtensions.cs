using System;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace ModuleServicesRegistration
{
	/// <summary>
	/// This class is a utility class that is used to register services for all the services in its contextual.
	/// </summary>
	public static class ModuleInitializerExtensions
	{
		public static IServiceCollection RunModuleInitializers(this IServiceCollection services, IEnumerable<Assembly> assemblies )
		{
			// Get all the types under the current assembly.
			IEnumerable<Type> types = assemblies.SelectMany(asm => asm.GetTypes())
                // Find all the concrete types and are implemented the IModuleInitializer interface
                // typeof(IModuleInitializer).IsAssignableFrom(t): if t is assignable to the IModuleInitializer type, return true
				// otherwise, return false. 
                .Where(t=> !t.IsAbstract && typeof(IModuleInitializer).IsAssignableFrom(t));
            // iterate over and create instances of all the implementation types
            foreach (Type implType in types )
			{
				// Create the instance of the implementation type and convert it to the IModuleInitializer type
				IModuleInitializer? initializer = (IModuleInitializer?)Activator.CreateInstance(implType);
				if (initializer == null)
				{
					throw new ApplicationException("cannot create" + implType);
				}

				// Add all the types to the services
				initializer.Initialize(services);
			}

			return services;
		}
	}
}

