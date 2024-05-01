using System;
using Microsoft.Extensions.DependencyInjection;

namespace ModuleServicesRegistration
{
	public interface IModuleInitializer
	{
		void Initialize(IServiceCollection services);
	}
}

