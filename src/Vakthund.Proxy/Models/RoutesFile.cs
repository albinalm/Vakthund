using JetBrains.Annotations;

namespace Vakthund.Proxy.Models;

[UsedImplicitly]
public class RoutesFile
{
    public List<VakthundRoute> Routes { get; set; } = [];
}
