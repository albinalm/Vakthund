using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Vakthund.Proxy.Middlewares;
using Vakthund.Proxy.Options;
using Vakthund.Proxy.Services;
using Vakthund.Shared.Models;

namespace Vakthund.Tests.Services;

public class RequestInterceptorTests
{
    [Fact]
    public async Task InvokeAsync_ForwardsCapturedResponseWritesBeforePipelineCompletes()
    {
        ProxyOptions options = new()
        {
            MaxBodyBytes = 0,
            MaxResponseBodyBytes = 1024
        };
        AuditQueue queue = new(Options.Create(options));
        RequestInterceptor interceptor = new(queue, new ProxyActivityFeed(), Options.Create(options));
        DefaultHttpContext context = new();
        using MemoryStream clientBody = new();
        context.Response.Body = clientBody;

        TaskCompletionSource firstWriteCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource allowPipelineToComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        byte[] chunk = Encoding.UTF8.GetBytes("hello");

        Task invokeTask = interceptor.InvokeAsync(context, async ctx =>
        {
            await ctx.Response.Body.WriteAsync(chunk);
            await ctx.Response.Body.FlushAsync();
            firstWriteCompleted.SetResult();
            await allowPipelineToComplete.Task;
        });

        await firstWriteCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("hello", Encoding.UTF8.GetString(clientBody.ToArray()));
        Assert.False(invokeTask.IsCompleted);

        allowPipelineToComplete.SetResult();
        await invokeTask.WaitAsync(TimeSpan.FromSeconds(5));

        AuditEntry entry = await ReadSingleEntryAsync(queue);
        Assert.Equal("hello", entry.ResponseBody);
    }

    [Fact]
    public async Task InvokeAsync_DoesNotStoreTruncatedResponseBody_WhenCaptureLimitIsExceeded()
    {
        ProxyOptions options = new()
        {
            MaxBodyBytes = 0,
            MaxResponseBodyBytes = 4
        };
        AuditQueue queue = new(Options.Create(options));
        RequestInterceptor interceptor = new(queue, new ProxyActivityFeed(), Options.Create(options));
        DefaultHttpContext context = new();
        using MemoryStream clientBody = new();
        context.Response.Body = clientBody;

        await interceptor.InvokeAsync(context, async ctx =>
        {
            await ctx.Response.Body.WriteAsync(Encoding.UTF8.GetBytes("hello"));
        });

        Assert.Equal("hello", Encoding.UTF8.GetString(clientBody.ToArray()));

        AuditEntry entry = await ReadSingleEntryAsync(queue);
        Assert.Null(entry.ResponseBody);
    }

    private static async Task<AuditEntry> ReadSingleEntryAsync(AuditQueue queue)
    {
        AuditBatch batch = await queue.ReadBatchAsync(1, TimeSpan.Zero, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        return Assert.Single(batch.Entries);
    }
}
