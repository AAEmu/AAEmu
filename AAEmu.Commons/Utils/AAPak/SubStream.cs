namespace AAEmu.Commons.Utils.AAPak;

/// <summary>Read-only view over a bounded section of another stream.</summary>
public sealed class SubStream : Stream
{
    private Stream _baseStream;
    private readonly long _length;
    private readonly long _baseOffset;
    private long _position;

    public SubStream(Stream baseStream, long offset, long length)
    {
        ArgumentNullException.ThrowIfNull(baseStream);
        if (!baseStream.CanRead)
            throw new ArgumentException("Base stream must be readable.", nameof(baseStream));
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        _baseStream = baseStream;
        _baseOffset = offset;
        _length = length;

        if (baseStream.CanSeek)
        {
            if (offset > baseStream.Length || length > baseStream.Length - offset)
                throw new ArgumentException("The requested range exceeds the base stream.", nameof(length));
            baseStream.Seek(offset, SeekOrigin.Begin);
            return;
        }

        Span<byte> buffer = stackalloc byte[512];
        while (offset > 0)
        {
            var read = baseStream.Read(buffer[..(int)Math.Min(offset, buffer.Length)]);
            if (read == 0)
                throw new EndOfStreamException("The base stream ended before the requested offset.");
            offset -= read;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        CheckDisposed();
        var remaining = _length - _position;
        if (remaining <= 0)
            return 0;
        count = (int)Math.Min(count, remaining);
        var read = _baseStream.Read(buffer, offset, count);
        _position += read;
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        CheckDisposed();
        var remaining = _length - _position;
        if (remaining <= 0)
            return 0;
        var read = _baseStream.Read(buffer[..(int)Math.Min(buffer.Length, remaining)]);
        _position += read;
        return read;
    }

    public override long Length
    {
        get
        {
            CheckDisposed();
            return _length;
        }
    }

    public override bool CanRead => _baseStream != null;
    public override bool CanWrite => false;
    public override bool CanSeek => _baseStream?.CanSeek == true;

    public override long Position
    {
        get
        {
            CheckDisposed();
            return _position;
        }
        set
        {
            CheckDisposed();
            if (!CanSeek)
                throw new NotSupportedException("The base stream does not support seeking.");
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = Math.Min(value, _length);
            _baseStream.Position = _baseOffset + _position;
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        CheckDisposed();
        if (!CanSeek)
            throw new NotSupportedException("The base stream does not support seeking.");

        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(_position + offset),
            SeekOrigin.End => checked(_length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        Position = target;
        return _position;
    }

    public override void Flush()
    {
        CheckDisposed();
        _baseStream.Flush();
    }

    public override void SetLength(long value) =>
        throw new NotSupportedException("SubStream is read-only.");

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("SubStream is read-only.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _baseStream = null;
        base.Dispose(disposing);
    }

    private void CheckDisposed()
    {
        ObjectDisposedException.ThrowIf(_baseStream == null, this);
    }
}
