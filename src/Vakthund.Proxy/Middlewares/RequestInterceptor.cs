using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using Vakthund.Proxy.Models;
using Vakthund.Proxy.Options;
using Vakthund.Proxy.Services;
using Vakthund.Shared.Models;
using Yarp.ReverseProxy.Model;

namespace Vakthund.Proxy.Middlewares;

public class RequestInterceptor(AuditQueue queue, ProxyActivityFeed activityFeed, IOptions<ProxyOptions> options) : IMiddleware
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
            ClientIp = ClientIpResolver.Resolve(context),
            ContentType = context.Request.ContentType,
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
            using var captureBody = new CapturingResponseBodyStream(originalBody, opts.MaxResponseBodyBytes);
            context.Response.Body = captureBody;

            try
            {
                await next.Invoke(context);

                if (!captureBody.ExceededLimit)
                {
                    entry.ResponseBody = captureBody.GetCapturedText();
                    if (string.IsNullOrEmpty(entry.ResponseBody))
                    {
                        entry.ResponseBody = null;
                    }
                }

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
            entry.ResponseContentType = context.Response.ContentType;
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
        entry.Upstreamed = true;
    }

    private sealed class CapturingResponseBodyStream(Stream inner, int maxCaptureBytes) : Stream
    {
        private readonly MemoryStream _capture = new();
        private long _totalBytes;

        public bool ExceededLimit => _totalBytes > maxCaptureBytes;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public string? GetCapturedText()
        {
            if (_capture.Length == 0)
            {
                return null;
            }

            return Encoding.UTF8.GetString(_capture.ToArray());
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);

        public override void Write(byte[] buffer, int offset, int count)
        {
            Capture(buffer.AsSpan(offset, count));
            inner.Write(buffer, offset, count);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Capture(buffer.Span);
            await inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Capture(buffer.AsSpan(offset, count));
            return inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override void WriteByte(byte value)
        {
            _totalBytes++;
            if (_capture.Length < maxCaptureBytes)
            {
                _capture.WriteByte(value);
            }

            inner.WriteByte(value);
        }

        private void Capture(ReadOnlySpan<byte> buffer)
        {
            _totalBytes += buffer.Length;

            long remaining = maxCaptureBytes - _capture.Length;
            if (remaining <= 0)
            {
                return;
            }

            int count = (int)Math.Min(remaining, buffer.Length);
            _capture.Write(buffer[..count]);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();
    }
}
