using D365Architect.Services.Conversion.Models;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Reads and writes one <see cref="ViewColumn"/> in either of its YAML shapes:
/// a plain string (<c>- createdon</c>, no explicit width) or a mapping
/// (<c>- name: bogdan_name</c> / <c>width: 300</c>). YamlDotNet can't map a
/// scalar onto a class by itself, hence this converter. Writing picks the
/// smaller shape: a bare string whenever there is no width (the
/// minimal-YAML rule — an omitted width means the default).
/// </summary>
internal sealed class ViewColumnYamlTypeConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(ViewColumn);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        if (parser.TryConsume<Scalar>(out var scalar))
        {
            return new ViewColumn { Name = scalar.Value };
        }

        var start = parser.Consume<MappingStart>();
        string? name = null;
        int? width = null;
        while (!parser.TryConsume<MappingEnd>(out _))
        {
            var key = parser.Consume<Scalar>();
            var value = parser.Consume<Scalar>();
            switch (key.Value)
            {
                case "name":
                    name = value.Value;
                    break;
                case "width" when int.TryParse(value.Value, out var parsed):
                    width = parsed;
                    break;
                case "width":
                    throw new YamlException(value.Start, value.End, $"A column's 'width' must be a whole number of pixels, not '{value.Value}'.");
                default:
                    throw new YamlException(key.Start, key.End, $"Unknown column key '{key.Value}': a column is a plain name, or 'name' with an optional 'width'.");
            }
        }

        return name is null
            ? throw new YamlException(start.Start, start.End, "A column written as a mapping needs a 'name'.")
            : new ViewColumn { Name = name, Width = width };
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        var column = (ViewColumn)value!;
        if (column.Width is null)
        {
            emitter.Emit(new Scalar(column.Name));
            return;
        }

        emitter.Emit(new MappingStart());
        emitter.Emit(new Scalar("name"));
        emitter.Emit(new Scalar(column.Name));
        emitter.Emit(new Scalar("width"));
        emitter.Emit(new Scalar(column.Width.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        emitter.Emit(new MappingEnd());
    }
}
