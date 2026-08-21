using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace D365Architect.Infrastructure;

/// <summary>
/// Bridges Spectre.Console.Cli's command resolution onto a standard
/// <see cref="IServiceCollection"/>, so every command and its dependencies
/// are registered and resolved through the same DI container as the rest
/// of the app.
/// </summary>
public sealed class TypeRegistrar(IServiceCollection services) : ITypeRegistrar
{
    public ITypeResolver Build()
    {
        return new TypeResolver(services.BuildServiceProvider());
    }

    public void Register(Type service, Type implementation)
    {
        services.AddSingleton(service, implementation);
    }

    public void RegisterInstance(Type service, object implementation)
    {
        services.AddSingleton(service, implementation);
    }

    public void RegisterLazy(Type service, Func<object> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        services.AddSingleton(service, _ => factory());
    }
}
