using Microsoft.Extensions.DependencyInjection;
namespace Asionyx.Service.Deployment.Shared;

/// <summary>
/// Delegate used to resolve the API key at runtime. The deployment service will register
/// a resolver that returns the configured API key. Integration tests can register a
/// resolver that returns a test-generated key.
/// </summary>
public delegate string ApiKeyResolver();

/// <summary>
/// Small helper extensions for DI registration (Microsoft DI).
/// </summary>
public static class ServiceCollectionExtensions
{
    public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddApiKeyResolver(this Microsoft.Extensions.DependencyInjection.IServiceCollection services, ApiKeyResolver resolver)
    {
        services.AddSingleton(resolver);
        // Also register as Func<string> for callers that expect the Func type
        services.AddSingleton<System.Func<string>>(() => resolver());
        return services;
    }
}
