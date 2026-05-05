using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Vakthund.Proxy.Models;

namespace Vakthund.Proxy.Services;

public class ProxyActivityFeed
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<Guid, Channel<ProxyActivity>> _subscribers = [];
    private readonly Queue<ProxyActivity> _recent = new();
    private readonly Lock _recentLock = new();
    private const int MaxRecent = 50;

    public async Task WriteEventStreamAsync(HttpResponse response, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.ContentType = "text/event-stream";

        ProxyActivity[] snapshot;
        lock (_recentLock)
            snapshot = [.. _recent];

        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<ProxyActivity>();
        _subscribers[id] = channel;

        try
        {
            foreach (ProxyActivity activity in snapshot)
            {
                string json = JsonSerializer.Serialize(activity, JsonOptions);
                await response.WriteAsync($"data: {json}\n\n", cancellationToken);
            }
            await response.Body.FlushAsync(cancellationToken);

            await foreach (ProxyActivity activity in channel.Reader.ReadAllAsync(cancellationToken))
            {
                string json = JsonSerializer.Serialize(activity, JsonOptions);
                await response.WriteAsync($"data: {json}\n\n", cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
            }
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
        }
    }

    public void Publish(ProxyActivity activity)
    {
        lock (_recentLock)
        {
            _recent.Enqueue(activity);
            if (_recent.Count > MaxRecent)
            {
                _recent.Dequeue();
            }
        }

        foreach (Channel<ProxyActivity> subscriber in _subscribers.Values)
        {
            subscriber.Writer.TryWrite(activity);
        }
    }
}
