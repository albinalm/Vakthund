using Vakthund.Proxy.Models;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Vakthund.Proxy.Helpers;

internal sealed class RouteIpDefinitionConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(RouteIpDefinition);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        if (parser.Current is Scalar)
        {
            Scalar scalar = parser.Consume<Scalar>();
            return new RouteIpDefinition { Reference = scalar.Value };
        }

        if (parser.Current is SequenceStart)
        {
            var entries = (List<string>?)rootDeserializer(typeof(List<string>));
            return new RouteIpDefinition { Inline = entries ?? [] };
        }

        throw new InvalidOperationException("Route ips must be either a named ip reference or a list of ip entries.");
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        throw new NotSupportedException("Routes files are only deserialized.");
    }
}
