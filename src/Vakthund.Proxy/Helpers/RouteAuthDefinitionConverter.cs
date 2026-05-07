using Vakthund.Proxy.Models;
using Vakthund.Shared.Models;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Vakthund.Proxy.Helpers;

internal sealed class RouteAuthDefinitionConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(RouteAuthDefinition);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        if (parser.Current is Scalar)
        {
            Scalar scalar = parser.Consume<Scalar>();
            return new RouteAuthDefinition { Reference = scalar.Value };
        }

        if (parser.Current is MappingStart)
        {
            var auth = (AuthExpectation?)rootDeserializer(typeof(AuthExpectation));
            return new RouteAuthDefinition { Inline = auth };
        }

        throw new InvalidOperationException("Route auth must be either a named auth reference or an auth object.");
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        throw new NotSupportedException("Routes files are only deserialized.");
    }
}
