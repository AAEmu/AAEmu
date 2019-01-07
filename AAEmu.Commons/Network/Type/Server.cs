using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NLog;

namespace AAEmu.Commons.Network.Type
{
    public class Server : INetBase
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private const int ReceiveBufferSize = 8096;
        private BufferManager _bufferManager;
        private Socket _listenSocket;
        private Semaphore _maxNumberAcceptedClients;
        private ConcurrentDictionary<uint, Session> _sessions;
        private BaseProtocolHandler _protocolHandler;

        public bool IsStarted { get; private set; }

        /// <summary>
        /// Create an uninitialized server instance.  To start the server listening for connection requests
        /// call the Init method followed by Start method 
        /// </summary>
        /// <param name="localEndPoint"></param>
        /// <param name="numConnections">the maximum number of connections the sample is designed to handle simultaneously</param>
        public Server(EndPoint localEndPoint, int numConnections)
        {
            // create the socket which listens for incoming connections
            _listenSocket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _listenSocket.Bind(localEndPoint);
            // start the server with a listen backlog of 100 connections
            _listenSocket.Listen(100);

            // allocate buffers such that the maximum number of sockets can have one outstanding read and 
            //write posted to the socket simultaneously  
            _bufferManager = new BufferManager(ReceiveBufferSize * numConnections, ReceiveBufferSize);

            _maxNumberAcceptedClients = new Semaphore(numConnections, numConnections);

            _sessions = new ConcurrentDictionary<uint, Session>();

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
            StartAccept(null);
        }

        public void Stop()
        {
            foreach (var session in _sessions.Values)
                session.Close();
            IsStarted = false;
            _listenSocket.Close();
        }

        /// <summary>
        /// Begins an operation to accept a connection request from the client 
        /// </summary>
        /// <param name="acceptEventArg">The context object to use when issuing the accept operation on the 
        /// server's listening socket</param>
        private void StartAccept(SocketAsyncEventArgs acceptEventArg)
        {
            if (acceptEventArg == null)
            {
                acceptEventArg = new SocketAsyncEventArgs();
                acceptEventArg.Completed += AcceptEventArg_Completed;
            }
            else
            {
                // socket must be cleared since the context object is being reused
                acceptEventArg.AcceptSocket = null;
            }

            _maxNumberAcceptedClients.WaitOne();
            var willRaiseEvent = _listenSocket.AcceptAsync(acceptEventArg);
            if (!willRaiseEvent)
                ProcessAccept(acceptEventArg);
        }

        /// <summary>
        /// This method is the callback method associated with Socket.AcceptAsync operations and is invoked
        /// when an accept operation is complete
        /// </summary>
        private void AcceptEventArg_Completed(object sender, SocketAsyncEventArgs e)
        {
            ProcessAccept(e);
        }

        private void ProcessAccept(SocketAsyncEventArgs e)
        {
            // Get the socket for the accepted client connection and put it into the 
            //ReadEventArg object user token
            var readEventArg = new SocketAsyncEventArgs();
            readEventArg.Completed += ReadComplete;
            // assign a byte buffer from the buffer pool to the SocketAsyncEventArg object
            _bufferManager.SetBuffer(readEventArg);

            if (e.AcceptSocket == null || e.AcceptSocket.RemoteEndPoint == null)
            {
                if (IsStarted)
                    StartAccept(e);
                return;
            }

            var session = new Session(this, readEventArg, e.AcceptSocket);
            readEventArg.UserToken = session;

            _sessions.TryAdd(session.Id, session);

            _protocolHandler.OnConnect(session);

            // As soon as the client is connected, post a receive to the connection
            var willRaiseEvent = e.AcceptSocket.ReceiveAsync(readEventArg);
            if (!willRaiseEvent)
                ProcessReceive(readEventArg);

            // Accept the next connection request
            StartAccept(e);
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

            _sessions.TryRemove(session.Id, out var val);
            if (val != null)
                _maxNumberAcceptedClients.Release();
        }
    }
}