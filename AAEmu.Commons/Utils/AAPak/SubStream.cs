using System;
using System.IO;

namespace AAEmu.Commons.Utils.AAPak
{
    // Sources:
    // https://stackoverflow.com/questions/6949441/how-to-expose-a-sub-section-of-my-stream-to-a-user
    // https://social.msdn.microsoft.com/Forums/vstudio/en-US/c409b63b-37df-40ca-9322-458ffe06ea48/how-to-access-part-of-a-filestream-or-memorystream?forum=netfxbcl

    public class SubStream : Stream
    {
        private Stream baseStream;
        private readonly long length;
        private long position;
        private readonly long baseOffset;

        public SubStream(Stream baseStream, long offset, long length)
        {
            if (baseStream == null) throw new ArgumentNullException("baseStream");
            if (!baseStream.CanRead) throw new ArgumentException("can't read base stream");
            if (offset < 0) throw new ArgumentOutOfRangeException("offset");

            this.baseStream = baseStream;
            this.baseOffset = offset;
            this.length = length;

            if (baseStream.CanSeek)
            {
                baseStream.Seek(offset, SeekOrigin.Begin);
            }
            else
            { 
                // read it manually...
                const int BUFFER_SIZE = 512;
                byte[] buffer = new byte[BUFFER_SIZE];
                while (offset > 0)
                {
                    int read = baseStream.Read(buffer, 0, offset < BUFFER_SIZE ? (int)offset : BUFFER_SIZE);
                    offset -= read;
                }
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            CheckDisposed();
            long remaining = length - position;
            if (remaining <= 0) return 0;
            if (remaining < count) count = (int)remaining;
            int read = baseStream.Read(buffer, offset, count);
            position += read;
            return read;
        }

        private void CheckDisposed()
        {
            if (baseStream == null) throw new ObjectDisposedException(GetType().Name);
        }

        public override long Length
        {
            get { CheckDisposed(); return length; }
        }

        public override bool CanRead
        {
            get { CheckDisposed(); return true; }
        }

        public override bool CanWrite
        {
            get { CheckDisposed(); return false; }
        }

        public override bool CanSeek
        {
            get { CheckDisposed(); return false; }
        }

        public override long Position
        {
            get
            {
                CheckDisposed();
                return position;
            }
            set
            {
                if (position > length)
                    position = length;
                else
                    position = value;
                baseStream.Position = baseOffset + position; 
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Flush()
        {
            CheckDisposed(); baseStream.Flush();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            /*
            if (disposing)
            {
                if (baseStream != null)
                {
                    try { baseStream.Dispose(); }
                    catch { }
                    baseStream = null;
                }
            }
            */
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }

}
