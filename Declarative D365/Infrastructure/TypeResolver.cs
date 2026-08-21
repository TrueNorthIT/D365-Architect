using Spectre.Console.Cli;

namespace Declarative_D365.Infrastructure;

/// <summary>
/// Resolves command instances (and anything they depend on) from the
/// underlying <see cref="IServiceProvider"/> built by <see cref="TypeRegistrar"/>.
/// </summary>
public sealed class TypeResolver(IServiceProvider provider) : ITypeResolver, IDisposable
{
    public object? Resolve(Type? type)
    {
        return type is null ? null : provider.GetService(type);
    }

    public void Dispose()
    {
        if (provider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
