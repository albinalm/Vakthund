using System.Diagnostics;
using Microsoft.Extensions.Options;
using Vakthund.Proxy.Models;
using Vakthund.Proxy.Options;
using Vakthund.Proxy.Services;
using Vakthund.Shared.Models;
using Yarp.ReverseProxy.Model;

namespace Vakthund.Proxy.Middlewares;

public class RequestInterceptor(AuditQueue queue, ProxyActivityFeed activityFeed, IOptions<VakthundOptions> options) : IMiddleware
{
    public static readonly object AuditEntryItemKey = new();
    public static readonly object ProxyStartTimestampItemKey = new();
    private static readonly object ProxyResponseCapturedItemKey = new();

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var opts = options.Value;
        string? body = null;

        if (opts.MaxBodyBytes > 0 && (context.Request.ContentLength is null or <= 0 || context.Request.ContentLength <= opts.MaxBodyBytes))
        {
            context.Request.EnableBuffering();
            string raw = await new StreamReader(context.Request.Body).ReadToEndAsync();
            context.Request.Body.Position = 0;
            body = raw.Length <= opts.MaxBodyBytes ? raw : null;
        }

        var entry = new AuditEntry
        {
            Scheme = context.Request.Scheme,
            Host = context.Request.Host.Value,
            Path = context.Request.Path.Value ?? "/",
            Query = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null,
            Method = context.Request.Method,
            Body = string.IsNullOrEmpty(body) ? null : body,
            Headers = context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString()),
            Cookies = context.Request.Cookies.ToDictionary(c => c.Key, c => c.Value),
            Queries = context.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString())
        };

        activityFeed.Publish(new ProxyActivity
        {
            Phase = "proxying",
            Method = entry.Method,
            Uri = entry.Uri
        });

        entry.Timestamp = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();
        context.Items[AuditEntryItemKey] = entry;
        context.Items[ProxyStartTimestampItemKey] = Stopwatch.GetTimestamp();

        if (opts.MaxResponseBodyBytes > 0 && (context.Response.ContentLength is null || context.Response.ContentLength <= opts.MaxResponseBodyBytes))
        {
            var originalBody = context.Response.Body;
            using var buffer = new MemoryStream();
            context.Response.Body = buffer;

            try
            {
                await next.Invoke(context);

                buffer.Position = 0;
                if (buffer.Length <= opts.MaxResponseBodyBytes)
                {
                    entry.ResponseBody = await new StreamReader(buffer).ReadToEndAsync();
                    if (string.IsNullOrEmpty(entry.ResponseBody))
                        entry.ResponseBody = null;
                }

                buffer.Position = 0;
                await buffer.CopyToAsync(originalBody);
                CaptureTotalResponse();
            }
            finally
            {
                context.Response.Body = originalBody;
            }
        }
        else
        {
            await next.Invoke(context);
            CaptureTotalResponse();
        }

        string? destination = context.Features.Get<IReverseProxyFeature>()?.ProxiedDestination?.Model.Config.Address;
        if (destination is not null)
        {
            var uri = new Uri(destination);
            entry.Scheme = uri.Scheme;
            entry.Host = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        }

        queue.Enqueue(entry);
        activityFeed.Publish(new ProxyActivity
        {
            Phase = "proxied",
            Method = entry.Method,
            Uri = entry.Uri,
            StatusCode = entry.StatusCode
        });

        void CaptureTotalResponse()
        {
            sw.Stop();
            entry.StatusCode = context.Response.StatusCode;
            entry.DurationMs = sw.ElapsedMilliseconds;
        }
    }

    public static void CaptureTargetResponse(HttpContext context, int? statusCode = null)
    {
        if (!context.Items.TryGetValue(AuditEntryItemKey, out object? entryObj) ||
            entryObj is not AuditEntry entry ||
            !context.Items.TryGetValue(ProxyStartTimestampItemKey, out object? startObj) ||
            startObj is not long startTimestamp ||
            context.Items.ContainsKey(ProxyResponseCapturedItemKey))
        {
            return;
        }

        context.Items[ProxyResponseCapturedItemKey] = true;
        entry.StatusCode = statusCode ?? context.Response.StatusCode;
        entry.TargetDurationMs = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
    }
}
