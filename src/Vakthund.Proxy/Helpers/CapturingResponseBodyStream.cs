using System.Text;

namespace Vakthund.Proxy.Helpers;

internal sealed class CapturingResponseBodyStream(Stream inner, int maxCaptureBytes) : Stream
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
