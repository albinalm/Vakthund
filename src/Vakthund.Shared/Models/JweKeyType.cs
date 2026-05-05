using System.Text.Json.Serialization;

namespace Vakthund.Shared.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum JweKeyType
{
    Rsa,
    Ec,
    Symmetric,
    Password
}
