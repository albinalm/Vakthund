using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Vakthund.Proxy.Options;
using Vakthund.Shared.Models;

namespace Vakthund.Proxy.Services;

public class AuditQueue(IOptions<VakthundOptions> options)
{
    private readonly Channel<AuditEntry> _channel = Channel.CreateBounded<AuditEntry>(
        new BoundedChannelOptions(options.Value.MaxQueuedEntries)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

    public void Enqueue(AuditEntry entry) =>
        _channel.Writer.TryWrite(entry);

    public ChannelReader<AuditEntry> Reader => _channel.Reader;
}
