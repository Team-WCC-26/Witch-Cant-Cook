using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    public abstract class Session
    {
        Socket _socket;
        RecvBuffer _recvBuffer = new RecvBuffer(4096);

        public void Start(Socket socket)
        {
            _socket = socket;
            RegisterRecv();
        }

        void RegisterRecv()
        {
            _recvBuffer.Clean();

            SocketAsyncEventArgs args = new SocketAsyncEventArgs();
            args.SetBuffer(_recvBuffer.WriteSegment);
            args.Completed += OnRecvCompleted;

            _socket.ReceiveAsync(args);
        }

        void OnRecvCompleted(object sender, SocketAsyncEventArgs args)
        {
            if (args.BytesTransferred > 0 && args.SocketError == SocketError.Success)
            {
                _recvBuffer.OnWrite(args.BytesTransferred);

                int processLen = OnRecv(_recvBuffer.ReadSegment);

                if (processLen < 0)
                {
                    Disconnect();
                    return;
                }

                _recvBuffer.OnRead(processLen);

                RegisterRecv();
            }
            else
            {
                Disconnect();
            }
        }

        protected abstract int OnRecv(ArraySegment<byte> buffer);

        void Disconnect()
        {
            _socket.Close();
        }
    }
}
