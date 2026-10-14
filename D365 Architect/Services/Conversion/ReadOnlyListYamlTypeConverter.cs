using System.Collections;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Every curated model in this tool exposes its lists as <see cref="IReadOnlyList{T}"/>
/// (a deliberate "this is a read model, not something to mutate" choice —
/// see e.g. <c>EntityDefinition.Attributes</c>). YamlDotNet's deserializer
/// can't instantiate an interface type on its own the way it can a concrete
/// <see cref="List{T}"/>, so without this it fails on every list property
/// with "No node deserializer was able to deserialize the node into type
/// IReadOnlyList`1[...]". This reads a YAML sequence into a
/// <see cref="List{T}"/> and hands it back through the same interface —
/// deserialization-only, since serialization already works fine without it.
/// </summary>
internal sealed class ReadOnlyListYamlTypeConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var elementType = type.GetGenericArguments()[0];
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;

        parser.Consume<SequenceStart>();
        while (!parser.TryConsume<SequenceEnd>(out _))
        {
            list.Add(rootDeserializer(elementType));
        }

        return list;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer) =>
        throw new NotSupportedException($"{nameof(ReadOnlyListYamlTypeConverter)} is deserialization-only; serialization doesn't need it.");
}
