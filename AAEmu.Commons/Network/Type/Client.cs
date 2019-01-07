using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NLog;

namespace AAEmu.Commons.Network.Type
{
    public class Client : INetBase
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private const int ReceiveBufferSize = 8096;
        private IPEndPoint _remoteEndPoint;
        private BufferManager _bufferManager;
        private Socket _connectSocket;
        private BaseProtocolHandler _protocolHandler;

        public bool IsStarted { get; private set; }

        public Client(IPEndPoint remoteEndPoint)
        {
            _remoteEndPoint = remoteEndPoint;
            _connectSocket = new Socket(remoteEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            //_connectSocket.Connect(remoteEndPoint);

            // allocate buffers such that the maximum number of sockets can have one outstanding read and 
            // write posted to the socket simultaneously  
            _bufferManager = new BufferManager(ReceiveBufferSize, ReceiveBufferSize);

            // Allocates one large byte buffer which all I/O operations use a piece of.  This gaurds 
            // against memory fragmentation
            _bufferManager.InitBuffer();
        }

        public void SetHandler(BaseProtocolHandler handler)
        {
            _protocolHandler = handler;
        }

        /// <summary>
        /// Starts the server such that it is listening for incoming connection requests.    
        /// </summary>
        public void Start()
        {
            IsStarted = true;
            // post accepts on the listening socket
            StartConnect();
        }

        public void Stop()
        {
            IsStarted = false;
            if (_connectSocket.Connected)
                _connectSocket.Disconnect(true);
            _connectSocket.Close();
            _log.Info("Network stoped");
        }

        /// <summary>
        /// Begins an operation to connect
        /// </summary>
        private void StartConnect()
        {
            _log.Info("Connecting to {0}", _remoteEndPoint.ToString());
            var connectEventArg = new SocketAsyncEventArgs {RemoteEndPoint = _remoteEndPoint};
            connectEventArg.Completed += AcceptEventArg_Completed;
            var willRaiseEvent = _connectSocket.ConnectAsync(connectEventArg);
            if (!willRaiseEvent)
                ProcessConnect(connectEventArg);
        }

        /// <summary>
        /// This method is the callback method associated with Socket.AcceptAsync operations and is invoked
        /// when an accept operation is complete
        /// </summary>
        private void AcceptEventArg_Completed(object sender, SocketAsyncEventArgs e)
        {
            ProcessConnect(e);
        }

        private void ProcessConnect(SocketAsyncEventArgs e)
        {
            var errorCode = e.SocketError;
            switch (errorCode)
            {
                case SocketError.TimedOut:
                case SocketError.ConnectionRefused:
                    _log.Info("Connection to {0} failed, retry...", _remoteEndPoint);
                    Thread.Sleep(10000);
                    StartConnect();
                    return;
                case SocketError.Interrupted:
                case SocketError.OperationAborted:
                    return;
            }

            if (errorCode != SocketError.Success)
                throw new SocketException((int) errorCode);

            // Get the socket for the accepted client connection and put it into the 
            // ReadEventArg object user token
            var readEventArg = new SocketAsyncEventArgs();
            readEventArg.Completed += ReadComplete;
            // assign a byte buffer from the buffer pool to the SocketAsyncEventArg object
            _bufferManager.SetBuffer(readEventArg);

            if (_connectSocket?.RemoteEndPoint == null)
            {
                _log.Info("Connection to {0} failed, retry...", _remoteEndPoint);
                Thread.Sleep(10000);
                StartConnect();
                return;
            }

            var session = new Session(this, readEventArg, _connectSocket);
            readEventArg.UserToken = session;

            _protocolHandler.OnConnect(session);

            // As soon as the client is connected, post a receive to the connection
            var willRaiseEvent = _connectSocket.ReceiveAsync(readEventArg);
            if (!willRaiseEvent)
                ProcessReceive(readEventArg);
        }

        /// <summary>
        /// This method is called whenever a receive or send opreation is completed on a socket 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e">SocketAsyncEventArg associated with the completed receive operation</param>
        private void ReadComplete(object sender, SocketAsyncEventArgs e)
        {
            // determine which type of operation just completed and call the associated handler
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    ProcessReceive(e);
                    break;
                default:
                    throw new ArgumentException("The last operation completed on the socket was not a receive");
            }
        }

        /// <summary>
        /// This method is invoked when an asycnhronous receive operation completes. If the 
        /// remote host closed the connection, then the socket is closed.  If data was received then
        /// the data is echoed back to the client.
        /// </summary>
        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            // check if the remote host closed the connection
            var session = (Session) e.UserToken;
            if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
            {
                var buf = new byte[e.BytesTransferred];
                Buffer.BlockCopy(e.Buffer, e.Offset, buf, 0, e.BytesTransferred);
                _protocolHandler.OnReceive(session, buf, e.BytesTransferred);

                // read the next block of data send from the client
                try
                {
                    var willRaiseEvent = session.Socket.ReceiveAsync(e);
                    if (!willRaiseEvent)
                        ProcessReceive(e);
                }
                catch (ObjectDisposedException)
                {
                    session.Close();
                }
            }
            else
            {
                if (e.SocketError != SocketError.Success && e.SocketError != SocketError.OperationAborted &&
                    e.SocketError != SocketError.ConnectionReset)
                    _log.Error("Error on ProcessReceive: {0}", e.SocketError.ToString());
                session.Close();
            }
        }

        public void OnConnect(Session session)
        {
            _protocolHandler.OnConnect(session);
        }

        public void OnDisconnect(Session session)
        {
            _protocolHandler.OnDisconnect(session);
        }

        public void OnReceive(Session session, byte[] buf, int bytes)
        {
            _protocolHandler.OnReceive(session, buf, bytes);
        }

        public void OnSend(Session session, byte[] buf, int offset, int bytes)
        {
            _protocolHandler.OnSend(session, buf, offset, bytes);
        }

        public void RemoveSession(Session session)
        {
            _bufferManager.FreeBuffer(session.ReadEventArg);
        }
    }
}