using Vakthund.Proxy.Models;
using Vakthund.Shared.Models;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Vakthund.Proxy.Services;

public static class RoutesLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new RouteAuthDefinitionConverter())
        .IgnoreUnmatchedProperties()
        .Build();

    public static List<VakthundRoute>? TryLoad(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        string yaml = File.ReadAllText(filePath);
        var file = Deserializer.Deserialize<RoutesFileDefinition>(yaml);
        Dictionary<string, AuthExpectation> namedAuths = BuildNamedAuths(file.Auths);
        List<VakthundRoute> routes = file.Routes
            .Select(route => new VakthundRoute
            {
                Path = route.Path,
                Target = route.Target,
                To = route.To,
                Auth = ResolveAuth(route.Auth, namedAuths, route.Path)
            })
            .ToList();

        return routes.Count > 0 ? routes : null;
    }

    private static Dictionary<string, AuthExpectation> BuildNamedAuths(IEnumerable<NamedAuthExpectation> auths)
    {
        var namedAuths = new Dictionary<string, AuthExpectation>(StringComparer.Ordinal);

        foreach (NamedAuthExpectation auth in auths)
        {
            string? name = auth.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("Routes file auths entries must define a non-empty name.");
            }

            if (!namedAuths.TryAdd(name, CloneAuth(auth)))
            {
                throw new InvalidOperationException($"Routes file defines duplicate auth name '{name}'.");
            }
        }

        return namedAuths;
    }

    private static AuthExpectation? ResolveAuth(
        RouteAuthDefinition? routeAuth,
        IReadOnlyDictionary<string, AuthExpectation> namedAuths,
        string routePath)
    {
        if (routeAuth is null)
        {
            return null;
        }

        if (routeAuth.Inline is { } inlineAuth)
        {
            return CloneAuth(inlineAuth);
        }

        string? name = routeAuth.Reference?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException($"Route '{routePath}' has an empty auth reference.");
        }

        if (!namedAuths.TryGetValue(name, out AuthExpectation? namedAuth))
        {
            throw new InvalidOperationException($"Route '{routePath}' references unknown auth '{name}'. Define it under auths.");
        }

        return CloneAuth(namedAuth);
    }

    private static AuthExpectation CloneAuth(AuthExpectation auth)
    {
        JweDecryptionConfig jwe = auth.Jwe ?? new JweDecryptionConfig();

        return new AuthExpectation
        {
            Enforced = auth.Enforced,
            Issuer = auth.Issuer,
            Audience = auth.Audience,
            Audiences = auth.Audiences is null ? [] : [.. auth.Audiences],
            Scopes = auth.Scopes is null ? [] : [.. auth.Scopes],
            Roles = auth.Roles is null ? [] : [.. auth.Roles],
            OpenIdConfigurationUrl = auth.OpenIdConfigurationUrl,
            JwksUrl = auth.JwksUrl,
            Jwe = new JweDecryptionConfig
            {
                KeyType = jwe.KeyType,
                Key = jwe.Key
            }
        };
    }

    private sealed class RoutesFileDefinition
    {
        public List<NamedAuthExpectation> Auths { get; set; } = [];
        public List<RouteDefinition> Routes { get; set; } = [];
    }

    private sealed class NamedAuthExpectation : AuthExpectation
    {
        public string Name { get; set; } = "";
    }

    private sealed class RouteDefinition
    {
        public string Path { get; init; } = "";
        public string Target { get; init; } = "";
        public string To { get; init; } = "";
        public RouteAuthDefinition? Auth { get; init; }
    }

    private sealed class RouteAuthDefinition
    {
        public string? Reference { get; init; }
        public AuthExpectation? Inline { get; init; }
    }

    private sealed class RouteAuthDefinitionConverter : IYamlTypeConverter
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
}
