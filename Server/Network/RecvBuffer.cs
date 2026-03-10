using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    public class RecvBuffer
    {
        private ArraySegment<byte> _buffer;
        private int _readPos;
        private int _writePos;

        public RecvBuffer(int size)
        {
            _buffer = new ArraySegment<byte>(new byte[size]);
        }

        public int DataSize => _writePos - _readPos;
        public int FreeSize => _buffer.Count - _writePos;

        public ArraySegment<byte> ReadSegment =>
            new ArraySegment<byte>(_buffer.Array, _buffer.Offset + _readPos, DataSize);

        public ArraySegment<byte> WriteSegment =>
            new ArraySegment<byte>(_buffer.Array, _buffer.Offset + _writePos, FreeSize);

        public void OnWrite(int numOfBytes)
        {
            _writePos += numOfBytes;
        }

        public void OnRead(int numOfBytes)
        {
            _readPos += numOfBytes;
        }

        public void Clean()
        {
            int dataSize = DataSize;

            if (dataSize == 0)
            {
                _readPos = 0;
                _writePos = 0;
            }
            else
            {
                Array.Copy(_buffer.Array, _buffer.Offset + _readPos,
                           _buffer.Array, _buffer.Offset,
                           dataSize);

                _readPos = 0;
                _writePos = dataSize;
            }
        }
    }
}
