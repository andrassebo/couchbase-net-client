﻿using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using Couchbase.IO.Converters;
using Couchbase.IO.Operations;

namespace Couchbase.IO.Strategies
{
    internal class SslConnection : ConnectionBase
    {
        private readonly SslStream _sslStream;
        private readonly ConnectionPool<SslConnection> _connectionPool;
        private readonly AutoResetEvent _sendEvent = new AutoResetEvent(false);
        private volatile bool _disposed;

        internal SslConnection(ConnectionPool<SslConnection> connectionPool, Socket socket, IByteConverter converter) 
            : this(connectionPool, socket, new SslStream(new NetworkStream(socket)), converter)
        {
        }

        internal SslConnection(ConnectionPool<SslConnection> connectionPool, Socket socket, SslStream sslStream, IByteConverter converter) 
            : base(socket, converter)
        {
            _connectionPool = connectionPool;
            _sslStream = sslStream;
        }

        public void Authenticate()
        {
            try
            {
                var targetHost = _connectionPool.EndPoint.Address.ToString();
                Log.Warn(m => m("Starting SSL encryption on {0}", targetHost));
                _sslStream.AuthenticateAsClient(targetHost);
                IsSecure = true;
            }
            catch (AuthenticationException e)
            {
                Log.Error(e);
            }
        }

        public override IOperationResult<T> Send<T>(IOperation<T> operation)
        {
            try
            {
                operation.Reset();
                var buffer = operation.Write();

                _sslStream.BeginWrite(buffer, 0, buffer.Length, SendCallback, operation);
                _sendEvent.WaitOne();
            }
            catch (IOException e)
            {
                Log.Warn(e);
                WriteError("Failed. Check Exception property.", operation, 0);
                operation.Exception = e;
                _sendEvent.Set();
            }
            return operation.GetResult();
        }

        private void SendCallback(IAsyncResult asyncResult)
        {
            var operation = (IOperation)asyncResult.AsyncState;
            try
            {
                _sslStream.EndWrite(asyncResult);
                operation.Buffer = BufferManager.TakeBuffer(512);
                _sslStream.BeginRead(operation.Buffer, 0, operation.Buffer.Length, ReceiveCallback, operation);
            }
            catch (IOException e)
            {
                Log.Warn(e);
                WriteError("Failed. Check Exception property.", operation, 0);
                operation.Exception = e;
                _sendEvent.Set();
            }
        }

        private void ReceiveCallback(IAsyncResult asyncResult)
        {
            var operation = (IOperation)asyncResult.AsyncState;

            try
            {
                var bytesRead = _sslStream.EndRead(asyncResult);
                operation.Read(operation.Buffer, 0, bytesRead);
                BufferManager.ReturnBuffer(operation.Buffer);

                if (operation.LengthReceived < operation.TotalLength)
                {
                    operation.Buffer = BufferManager.TakeBuffer(512);
                    _sslStream.BeginRead(operation.Buffer, 0, operation.Buffer.Length, ReceiveCallback, operation);
                }
                else
                {
                    _sendEvent.Set();
                }
            }
            catch (IOException e)
            {
                Log.Warn(e);
                WriteError("Failed. Check Exception property.", operation, 0);
                operation.Exception = e;
                _sendEvent.Set();
            }
        }

        static void WriteError(string errorMsg, IOperation operation, int offset)
        {
            var bytes = Encoding.UTF8.GetBytes(errorMsg);
            operation.Read(bytes, offset, errorMsg.Length);
        }

        /// <summary>
        /// Shuts down, closes and disposes of the internal <see cref="Socket"/> instance.
        /// </summary>
        public override void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!_disposed)
                {
                    if (Socket != null)
                    {
                        if (Socket.Connected)
                        {
                            Socket.Shutdown(SocketShutdown.Both);
                            Socket.Close(_connectionPool.Configuration.ShutdownTimeout);
                        }
                        else
                        {
                            Socket.Close();
                            Socket.Dispose();
                        }
                    }
                    if (_sslStream != null)
                    {
                        _sslStream.Dispose();
                    }
                }
            }
            else
            {
                if (Socket != null)
                {
                    Socket.Close();
                    Socket.Dispose();
                }
                if (_sslStream != null)
                {
                    _sslStream.Dispose();
                }
            }
            _disposed = true;
        }

        ~SslConnection()
        {
            Dispose(false);
        }
    }
}
