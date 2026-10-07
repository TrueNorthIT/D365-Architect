using System.Collections;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace D365Architect.Services.Conversion;

/// <summary>
/// The <see cref="ReadOnlyDictionaryYamlTypeConverter"/> counterpart for
/// <see cref="IReadOnlyDictionary{TKey,TValue}"/> — every curated model that
/// carries a translations map (<c>FormTab</c>/<c>FormSection</c>/
/// <c>FormControl</c>.<c>Translations</c>) exposes it through this interface,
/// which YamlDotNet's deserializer can't instantiate on its own any more
/// than it can <see cref="IReadOnlyList{T}"/> — confirmed live: deserializing
/// a form YAML with a non-empty <c>translations</c> map failed outright
/// before this existed, since <see cref="ReadOnlyListYamlTypeConverter"/>
/// only <see cref="ReadOnlyListYamlTypeConverter.Accepts"/>s
/// <c>IReadOnlyList&lt;&gt;</c>. Reads a YAML mapping into a
/// <see cref="Dictionary{TKey,TValue}"/> and hands it back through the same
/// interface — deserialization-only, since serialization already works fine
/// without it.
/// </summary>
internal sealed class ReadOnlyDictionaryYamlTypeConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var keyType = type.GetGenericArguments()[0];
        var valueType = type.GetGenericArguments()[1];
        var dictionary = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(keyType, valueType))!;

        parser.Consume<MappingStart>();
        while (!parser.TryConsume<MappingEnd>(out _))
        {
            var key = rootDeserializer(keyType);
            var value = rootDeserializer(valueType);
            dictionary.Add(key!, value);
        }

        return dictionary;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer) =>
        throw new NotSupportedException($"{nameof(ReadOnlyDictionaryYamlTypeConverter)} is deserialization-only; serialization doesn't need it.");
}
