using System;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Security.Authentication;
using System.Threading.Tasks;
using Couchbase.Logging;
using Couchbase.Authentication.SASL;
using Couchbase.IO.Operations;
using Couchbase.IO.Operations.Errors;
using Couchbase.Tracing;
using Couchbase.Utils;

namespace Couchbase.IO.Services
{
    // ReSharper disable once InconsistentNaming
    /// <summary>
    /// The default service for performing IO. Each thread uses a connection before returning back to the pool.
    /// </summary>
    /// <seealso cref="Couchbase.IO.Services.IOServiceBase" />
    public class PooledIOService : IOServiceBase
    {
        private static readonly ILog Log = LogManager.GetLogger<PooledIOService>();
        private volatile bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="PooledIOService"/> class.
        /// </summary>
        /// <param name="connectionPool">The connection pool.</param>
        public PooledIOService(IConnectionPool connectionPool)
        {
            Log.Debug("Creating PooledIOService {0}", Identity);
            ConnectionPool = connectionPool;

            var connection = connectionPool.Connections.FirstOrDefault() ?? connectionPool.Acquire();
            CheckEnabledServerFeatures(connection);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PooledIOService"/> class.
        /// </summary>
        /// <param name="connectionPool">The connection pool.</param>
        /// <param name="saslMechanism">The sasl mechanism.</param>
        public PooledIOService(IConnectionPool connectionPool, ISaslMechanism saslMechanism)
        {
            Log.Debug("Creating PooledIOService {0}", Identity);
            ConnectionPool = connectionPool;
            SaslMechanism = saslMechanism;

            var conn = connectionPool.Acquire();
            CheckEnabledServerFeatures(conn);
        }

        /// <summary>
        /// Executes an operation for a given key.
        /// </summary>
        /// <param name="operation">The <see cref="IOperation" /> being executed.</param>
        /// <returns>
        /// An <see cref="IOperationResult" /> representing the result of operation.
        /// </returns>
        public override IOperationResult Execute(IOperation operation)
        {
            var connection = ConnectionPool.Acquire();

            Log.Trace("Using conn {0} on {1}", connection.Identity, connection.EndPoint);
            try
            {
                //A new connection will have to check for server features
                CheckEnabledServerFeatures(connection);

                var request = operation.Write(Tracer, ConnectionPool.Configuration.BucketName);
                byte[] response;
                OperationHeader header;
                ErrorCode errorCode;

                using (var scope = Tracer.BuildSpan(operation, connection, ConnectionPool.Configuration.BucketName).StartActive())
                {
                    response = connection.Send(request);
                    header = response.CreateHeader(ErrorMap, out errorCode);
                    scope.Span.SetPeerLatencyTag(header.GetServerDuration(response));
                }

                operation.Read(response, header, errorCode);
            }
            catch (SocketException e)
            {
                Log.Debug(e);
                operation.Exception = e;
                operation.HandleClientError(e.Message, ResponseStatus.TransportFailure);
            }
            catch (AuthenticationException e)
            {
                Log.Debug(e);
                operation.Exception = e;
                operation.HandleClientError(e.Message, ResponseStatus.AuthenticationError);
            }
            catch (RemoteHostTimeoutException e)
            {
                Log.Debug(e);
                operation.Exception = e;
                operation.HandleClientError(e.Message, ResponseStatus.TransportFailure);

                //this almost always will be a server offline or service down
                ConnectionPool.Owner.MarkDead();
            }
            catch (SendTimeoutExpiredException e)
            {
                Log.Debug(e);
                operation.Exception = e;
                operation.HandleClientError(e.Message, ResponseStatus.OperationTimeout);
            }
            catch (Exception e)
            {
                Log.Debug(e);
                operation.Exception = e;
                operation.HandleClientError(e.Message, ResponseStatus.ClientFailure);
            }
            finally
            {
                ConnectionPool.Release(connection);
            }

            return operation.GetResult(Tracer, ConnectionPool.Configuration.BucketName);
        }

        /// <summary>
        /// Executes an operation for a given key.
        /// </summary>
        /// <typeparam name="T">The Type T of the value being stored or retrieved.</typeparam>
        /// <param name="operation">The <see cref="IOperation{T}" /> being executed.</param>
        /// <returns>
        /// An <see cref="IOperationResult{T}" /> representing the result of operation.
        /// </returns>
        public override IOperationResult<T> Execute<T>(IOperation<T> operation)
        {
            var connection = ConnectionPool.Acquire();

            Log.Trace("Using conn {0} on {1}", connection.Identity, connection.EndPoint);

            try
            {
                //A new connection will have to check for server features
                CheckEnabledServerFeatures(connection);

                var request = operation.Write(Tracer, ConnectionPool.Configuration.BucketName);
                byte[] response;
                OperationHeader header;
                ErrorCode errorCode;

                using (var scope = Tracer.BuildSpan(operation, connection, ConnectionPool.Configuration.BucketName).StartActive())
                {
                    response = connection.Send(request);
                    header = response.CreateHeader(ErrorMap, out errorCode);
                    scope.Span.SetPeerLatencyTag(header.GetServerDuration(response));
                }

                operation.Read(response, header, errorCode);
            }
            catch (SocketException e)
            {
                Log.Debug(e);
                operation.Exception = e;
                operation.HandleClientError(e.Message, ResponseStatus.TransportFailure);
            }
            catch (AuthenticationException e)
            {
                Log.Debug(e);
                operation.Exception = e;
                operation.HandleClientError(e.Message, ResponseStatus.AuthenticationError);
            }
            catch (RemoteHostTimeoutException e)
            {
                Log.Debug(e);
                operation.Exception = e;
                operation.HandleClientError(e.Message, ResponseStatus.TransportFailure);

                //this almost always will be a server offline or service down
                ConnectionPool.Owner.MarkDead();
            }
            catch (SendTimeoutExpiredException e)
            {
                Log.Debug(e);
                operation.Exception = e;
                operation.HandleClientError(e.Message, ResponseStatus.OperationTimeout);
            }
            catch (Exception e)
            {
                Log.Debug(e);
                operation.Exception = e;
                operation.HandleClientError(e.Message, ResponseStatus.ClientFailure);
            }
            finally
            {
                ConnectionPool.Release(connection);
            }

            return operation.GetResultWithValue(Tracer, ConnectionPool.Configuration.BucketName);
        }

        /// <summary>
        /// Asynchrounously executes an operation for a given key.
        /// </summary>
        /// <typeparam name="T">The Type T of the value being stored or retrieved.</typeparam>
        /// <param name="operation">The <see cref="IOperation{T}" /> being executed.</param>
        /// <param name="connection">The <see cref="IConnection" /> the operation is using.</param>
        /// <returns>
        /// An <see cref="IOperationResult{T}" /> representing the result of operation.
        /// </returns>
        /// <remarks>
        /// This overload is used to perform authentication on the connection if it has not already been authenticated.
        /// </remarks>
        public override async Task ExecuteAsync<T>(IOperation<T> operation, IConnection connection)
        {
            ExceptionDispatchInfo capturedException = null;
            try
            {
                Log.Trace("Using conn {0} on {1}", connection.Identity, connection.EndPoint);

                var request = await operation.WriteAsync(Tracer, ConnectionPool.Configuration.BucketName).ContinueOnAnyContext();
                var span = Tracer.BuildSpan(operation, connection, ConnectionPool.Configuration.BucketName).Start();

                await connection.SendAsync(request, operation.Completed, span, ErrorMap).ContinueOnAnyContext();
            }
            catch (Exception e)
            {
                Log.Debug(e);
                capturedException = ExceptionDispatchInfo.Capture(e);
            }

            if (capturedException != null)
            {
                await HandleException(capturedException, operation).ContinueOnAnyContext();
            }
        }

        /// <summary>
        /// Asynchrounously executes an operation for a given key.
        /// </summary>
        /// <param name="operation">The <see cref="IOperation{T}" /> being executed.</param>
        /// <param name="connection">The <see cref="IConnection" /> the operation is using.</param>
        /// <returns>
        /// An <see cref="IOperationResult" /> representing the result of operation.
        /// </returns>
        /// <remarks>
        /// This overload is used to perform authentication on the connection if it has not already been authenticated.
        /// </remarks>
        public override async Task ExecuteAsync(IOperation operation, IConnection connection)
        {
            ExceptionDispatchInfo capturedException = null;
            try
            {
                Log.Trace("Using conn {0} on {1}", connection.Identity, connection.EndPoint);

                var request = await operation.WriteAsync(Tracer, ConnectionPool.Configuration.BucketName).ContinueOnAnyContext();
                var span = Tracer.BuildSpan(operation, connection, ConnectionPool.Configuration.BucketName).Start();

                await connection.SendAsync(request, operation.Completed, span, ErrorMap).ContinueOnAnyContext();
            }
            catch (Exception e)
            {
                Log.Debug(e);
                capturedException = ExceptionDispatchInfo.Capture(e);
            }

            if (capturedException != null)
            {
                await HandleException(capturedException, operation).ContinueOnAnyContext();
            }
        }

        /// <summary>
        /// Asynchrounously executes an operation for a given key.
        /// </summary>
        /// <typeparam name="T">The Type T of the value being stored or retrieved.</typeparam>
        /// <param name="operation">The <see cref="IOperation{T}" /> being executed.</param>
        /// <returns>
        /// An <see cref="IOperationResult{T}" /> representing the result of operation.
        /// </returns>
        /// <remarks>
        /// This overload is used to perform authentication on the connection if it has not already been authenticated.
        /// </remarks>
        public override async Task ExecuteAsync<T>(IOperation<T> operation)
        {
            ExceptionDispatchInfo capturedException = null;
            IConnection connection = null;
            try
            {
                connection = ConnectionPool.Acquire();
                Log.Trace("Using conn {0} on {1}", connection.Identity, connection.EndPoint);

                //A new connection will have to check for server features
                CheckEnabledServerFeatures(connection);

                await ExecuteAsync(operation, connection).ContinueOnAnyContext();
            }
            catch (Exception e)
            {
                Log.Debug(e);
                capturedException = ExceptionDispatchInfo.Capture(e);
            }
            finally
            {
                ConnectionPool.Release(connection);
            }

            if (capturedException != null)
            {
                await HandleException(capturedException, operation).ContinueOnAnyContext();
            }
        }

        /// <summary>
        /// Asynchrounously executes an operation for a given key.
        /// </summary>
        /// <param name="operation">The <see cref="IOperation{T}" /> being executed.</param>
        /// <returns>
        /// An <see cref="IOperationResult" /> representing the result of operation.
        /// </returns>
        /// <remarks>
        /// This overload is used to perform authentication on the connection if it has not already been authenticated.
        /// </remarks>
        public override async Task ExecuteAsync(IOperation operation)
        {
            ExceptionDispatchInfo capturedException = null;
            IConnection connection = null;
            try
            {
                connection = ConnectionPool.Acquire();

                Log.Trace("Using conn {0} on {1}", connection.Identity, connection.EndPoint);

                //A new connection will have to check for server features
                CheckEnabledServerFeatures(connection);

                await ExecuteAsync(operation, connection).ContinueOnAnyContext();
            }
            catch (Exception e)
            {
                Log.Debug(e);
                capturedException = ExceptionDispatchInfo.Capture(e);
            }
            finally
            {
                ConnectionPool.Release(connection);
            }

            if (capturedException != null)
            {
                await HandleException(capturedException, operation).ContinueOnAnyContext();
            }
        }

        /// <summary>
        /// Returns true if internal TCP connections are using SSL.
        /// </summary>
        public override bool IsSecure
        {
            get
            {
                var connection = ConnectionPool.Acquire();
                var isSecure = connection.IsSecure;
                ConnectionPool.Release(connection);
                return isSecure;
            }
            protected set => throw new NotSupportedException();
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public override void Dispose()
        {
            Log.Debug("Disposing PooledIOService for {0} - {1}", EndPoint, Identity);
            Dispose(true);
        }

        void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    GC.SuppressFinalize(this);
                }
                ConnectionPool?.Dispose();
            }
            _disposed = true;
        }

#if DEBUG
        /// <summary>Allows an object to try to free resources and perform other cleanup operations before it is reclaimed by garbage collection.</summary>
        ~PooledIOService()
        {
            Log.Debug("Finalizing PooledIOService for {0} - {1}", EndPoint, Identity);
            Dispose(false);
        }
#endif
    }
}

#region [ License information          ]

/* ************************************************************
 *
 *    @author Couchbase <info@couchbase.com>
 *    @copyright 2017 Couchbase, Inc.
 *
 *    Licensed under the Apache License, Version 2.0 (the "License");
 *    you may not use this file except in compliance with the License.
 *    You may obtain a copy of the License at
 *
 *        http://www.apache.org/licenses/LICENSE-2.0
 *
 *    Unless required by applicable law or agreed to in writing, software
 *    distributed under the License is distributed on an "AS IS" BASIS,
 *    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *    See the License for the specific language governing permissions and
 *    limitations under the License.
 *
 * ************************************************************/

#endregion
