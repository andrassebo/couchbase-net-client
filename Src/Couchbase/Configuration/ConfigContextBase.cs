using System.Threading;
using Couchbase.Logging;
using Couchbase.Authentication.SASL;
using Couchbase.Configuration.Client;
using Couchbase.Configuration.Server.Serialization;
using Couchbase.Core;
using Couchbase.Core.Buckets;
using Couchbase.Core.Transcoders;
using Couchbase.IO;
using Couchbase.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Couchbase.N1QL;

namespace Couchbase.Configuration
{
    /// <summary>
    /// Base class for configuration contexts. The configuration context is a class which maintains the internal
    /// state of the cluster and communicats with configuration providers to ensure that the state is up-to-date.
    /// </summary>
    internal abstract class ConfigContextBase : IConfigInfo
    {
        public const int SearchNodeFailureThreshold = 2;

        protected static readonly ILog Log = LogManager.GetLogger<ConfigContextBase>();
        private static int _roundRobinPosition = 0;
        protected IKeyMapper KeyMapper;
        protected IDictionary<IPEndPoint, IServer> Servers = new Dictionary<IPEndPoint, IServer>();
        protected Func<IConnectionPool, IIOService> IOServiceFactory;
        protected Func<PoolConfiguration, IPEndPoint, IConnectionPool> ConnectionPoolFactory;
        protected readonly Func<string, string, IConnectionPool, ITypeTranscoder, ISaslMechanism> SaslFactory;
        private bool _disposed;
        protected ReaderWriterLockSlim Lock = new ReaderWriterLockSlim();

        //for segregating the nodes into separate lists
        protected List<IServer> QueryNodes;
        protected List<IServer> ViewNodes;
        protected List<IServer> DataNodes;
        protected List<IServer> IndexNodes;
        protected List<IServer> SearchNodes;
        protected List<IServer> AnalyticsNodes;

        public bool IsQueryCapable { get; set; }
        public bool IsViewCapable { get; set; }
        public bool IsDataCapable { get; set; }
        public bool IsIndexCapable { get; set; }
        public bool IsSearchCapable { get; set; }

        public bool IsAnalyticsCapable => AnalyticsNodes.Any();

        public ConcurrentBag<FailureCountingUri> QueryUris = new ConcurrentBag<FailureCountingUri>();
        public ConcurrentBag<FailureCountingUri> SearchUris = new ConcurrentBag<FailureCountingUri>();
        public ConcurrentBag<FailureCountingUri> AnalyticsUris = new ConcurrentBag<FailureCountingUri>();
        protected IBucketConfig _bucketConfig;

        protected string UserName { get; }
        protected string Password { get; }

        protected ConfigContextBase(IBucketConfig bucketConfig, ClientConfiguration clientConfig,
            Func<IConnectionPool, IIOService> ioServiceFactory,
            Func<PoolConfiguration, IPEndPoint, IConnectionPool> connectionPoolFactory,
            Func<string, string, IConnectionPool, ITypeTranscoder, ISaslMechanism> saslFactory,
            ITypeTranscoder transcoder,
            string userName,
            string password)
        {
            _bucketConfig = bucketConfig;
            ClientConfig = clientConfig;
            IOServiceFactory = ioServiceFactory;
            ConnectionPoolFactory = connectionPoolFactory;
            CreationTime = DateTime.Now;
            SaslFactory = saslFactory;
            Transcoder = transcoder;

            UserName = !string.IsNullOrWhiteSpace(userName) ? userName : bucketConfig.Name;
            Password = password;
        }

        public FailureCountingUri GetQueryUri(int queryFailedThreshold)
        {
            var queryUris = QueryUris.Where(x => x.IsHealthy(queryFailedThreshold)).ToList();
            if (queryUris.Count == 0)
            {
                // All query URIs are unhealthy, so reset them all back to healthy and return the entire list
                // It's better to at least try the nodes than assume they're all failing indefinitely

                foreach (var queryUri in QueryUris)
                {
                    queryUri.ClearFailed();
                    queryUris.Add(queryUri);
                }
            }

            return RoundRobin(queryUris);
        }

        private static FailureCountingUri RoundRobin(IReadOnlyList<FailureCountingUri> uris)
        {
            var count = uris.Count;
            if (count == 0)
            {
                return null;
            }

            Interlocked.Increment(ref _roundRobinPosition);;
            if (_roundRobinPosition >= count)
            {
                Interlocked.Exchange(ref _roundRobinPosition, 0);
            }

            var mod = _roundRobinPosition % count;
            return uris[mod];
        }

        public FailureCountingUri GetSearchUri()
        {
            var searchUris = SearchUris.Where(x => x.IsHealthy(SearchNodeFailureThreshold)).ToList();
            if (searchUris.Count == 0)
            {
                // All search URIs are unhealthy, so reset them all back to healthy and return the entire list
                // It's better to at least try the nodes than assume they're all failing indefinitely

                foreach (var searchUri in SearchUris)
                {
                    searchUri.ClearFailed();
                    searchUris.Add(searchUri);
                }
            }

            return searchUris.GetRandom();
        }

        public FailureCountingUri GetAnalyticsUri()
        {
            return AnalyticsUris.Where(x => x.IsHealthy(2)).GetRandom();
        }

        protected ITypeTranscoder Transcoder { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the server supports enhanced durability.
        /// </summary>
        /// <value>
        /// <c>true</c> if the server supports enhanced durability; otherwise, <c>false</c>.
        /// </value>
        public bool SupportsEnhancedDurability { get; protected set; }

        /// <summary>
        /// Gets a value indicating whether Subdocument XAttributes are supported.
        /// </summary>
        /// <value>
        /// <c>true</c> if the server supports Subdocument XAttributes; otherwise, <c>false</c>.
        /// </value>
        public bool SupportsSubdocXAttributes { get; protected set; }

        /// <summary>
        /// Gets a value indicating whether the cluster supports Enhanced Authentication.
        /// </summary>
        /// <value>
        /// <c>true</c> if the cluster supports enhanced authentication; otherwise, <c>false</c>.
        /// </value>
        public bool SupportsEnhancedAuthentication { get; protected set; }

        /// <summary>
        /// Gets a value indicating whether the cluster supports an error map that can
        /// be used to return custom error information.
        /// </summary>
        /// <value>
        /// <c>true</c> if the cluster supports KV error map; otherwise, <c>false</c>.
        /// </value>
        public bool SupportsKvErrorMap { get; protected set; }

        /// <summary>
        /// The time at which this configuration context has been created.
        /// </summary>
        public DateTime CreationTime { get; }

        /// <summary>
        /// The client configuration for a bucket.
        /// <remarks> See <see cref="IBucketConfig"/> for details.</remarks>
        /// </summary>
        public IBucketConfig BucketConfig => _bucketConfig;

        /// <summary>
        /// The name of the Bucket that this configuration represents.
        /// </summary>
        public string BucketName => BucketConfig.Name;

        /// <summary>
        /// The client configuration.
        /// </summary>
        public ClientConfiguration ClientConfig { get; }

        /// <summary>
        /// The <see cref="BucketTypeEnum"/> that this configuration context is for.
        /// </summary>
        public BucketTypeEnum BucketType
        {
            get
            {
                if (!Enum.TryParse(BucketConfig.BucketType, true, out BucketTypeEnum bucketType))
                {
                    throw new NullConfigException("BucketType is not defined");
                }
                return bucketType;
            }
        }

        /// <summary>
        /// The <see cref="NodeLocatorEnum"/> that this configuration is using.
        /// </summary>
        public NodeLocatorEnum NodeLocator
        {
            get
            {
                NodeLocatorEnum nodeLocator;
                if (!Enum.TryParse(BucketConfig.NodeLocator, true, out nodeLocator))
                {
                    throw new NullConfigException("NodeLocator is not defined");
                }
                return nodeLocator;
            }
        }

        /// <summary>
        /// Loads the most updated configuration creating any resources as needed.
        /// </summary>
        /// <param name="bucketConfig">The latest <see cref="IBucketConfig"/>
        /// that will drive the recreation if the configuration context.</param>
        /// <param name="force">True to force the reconfiguration.</param>
        public abstract void LoadConfig(IBucketConfig bucketConfig, bool force = false);

        /// <summary>
        /// Loads the most updated configuration creating any resources as needed. The <see cref="IBucketConfig"/>
        /// used by this method is passed into the CTOR.
        /// </summary>
        /// <remarks>This method should be called immediately after creation.</remarks>
        public abstract void LoadConfig();

        /// <summary>
        /// Gets the <see cref="IKeyMapper"/> instance associated with this <see cref="IConfigInfo"/>.
        /// </summary>
        /// <returns></returns>
        public IKeyMapper GetKeyMapper()
        {
            Log.Trace("Getting KeyMapper for rev#{0} on thread {1}", BucketConfig.Rev, Thread.CurrentThread.ManagedThreadId);
            return KeyMapper;
        }

        /// <summary>
        /// Gets a random server instance from the underlying <see cref="IServer"/> collection.
        /// </summary>
        /// <returns></returns>
        public IServer GetServer()
        {
            const int maxAttempts = 7;
            var attempts = 0;

            try
            {
                Lock.EnterReadLock();
                if (!Servers.Any())
                {
                    throw new ServerUnavailableException();
                }

                IServer server;
                do
                {
                    server = Servers.Values.Where(x => !x.IsDown).GetRandom();

                    //cannot find a server - usually a temp state
                    if (server == null)
                    {
                        try
                        {
                            Lock.ExitReadLock();
                            var sleepTime = (int)Math.Pow(2, attempts);
                            Thread.Sleep(sleepTime);
                        }
                        finally
                        {
                            Lock.EnterReadLock();
                        }
                    }
                    else
                    {
                        break;
                    }
                } while (attempts++ < maxAttempts);
                if (server == null)
                {
                    throw new ServerUnavailableException();
                }
                return server;
            }
            finally
            {
                Lock.ExitReadLock();
            }
        }

        List<IServer> IConfigInfo.Servers
        {
            get
            {
                try
                {
                    Lock.EnterReadLock();
                    return Servers.Values.ToList();
                }
                finally
                {
                    Lock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Reclaims all resources and suppresses finalization.
        /// </summary>
        public void Dispose()
        {
            Log.Debug("Disposing ConfigContext");
            Dispose(true);
        }

        /// <summary>
        /// Reclams all resources and optionally suppresses finalization.
        /// </summary>
        /// <param name="disposing">True to suppress finalization.</param>
        private void Dispose(bool disposing)
        {
            try
            {
                Lock.EnterWriteLock();
                if (_disposed) return;
                if (disposing)
                {
                    GC.SuppressFinalize(this);
                }
                if (Servers != null)
                {
                    foreach (var server in Servers)
                    {
                        server.Value.Dispose();
                    }
                    Servers.Clear();
                }
                _disposed = true;
            }
            finally
            {
                Lock.ExitWriteLock();
            }
        }

#if DEBUG
        /// <summary>
        /// Reclaims all un-reclaimed resources.
        /// </summary>
        ~ConfigContextBase()
        {
            Log.Debug("Finalizing ConfigContext for Rev#{0}", BucketConfig.Rev);
            Dispose(false);
        }
#endif

        public bool SslConfigured => BucketConfig.UseSsl || ClientConfig.UseSsl;

        /// <summary>
        /// Gets a data node from the Servers collection.
        /// </summary>
        /// <returns></returns>
        public IServer GetDataNode()
        {
            return DataNodes.GetRandom();
        }

        /// <summary>
        /// Gets a query node from the Servers collection.
        /// </summary>
        /// <returns></returns>
        public IServer GetQueryNode()
        {
            return QueryNodes.Where(x=>!x.IsDown).GetRandom();
        }

        /// <summary>
        /// Gets a index node from the Servers collection.
        /// </summary>
        /// <returns></returns>
        public IServer GetIndexNode()
        {
            return IndexNodes.GetRandom();
        }

        /// <summary>
        /// Gets a view node from the Servers collection.
        /// </summary>
        /// <returns></returns>
        public IServer GetViewNode()
        {
            return ViewNodes.Where(x => !x.IsDown).GetRandom();
        }

        /// <summary>
        /// Gets a search node from the servers collection.
        /// </summary>
        /// <returns></returns>
        public IServer GetSearchNode()
        {
            return SearchNodes.Where(x => !x.IsDown).GetRandom();
        }

        /// <summary>
        /// Gets an analytics node from the server collection.
        /// </summary>
        /// <returns></returns>
        public IServer GetAnalyticsNode()
        {
            return AnalyticsNodes.Where(x => !x.IsDown).GetRandom();
        }

        /// <summary>
        /// Invalidates and clears the query cache. This method can be used to explicitly clear the internal N1QL query cache. This cache will
        /// be filled with non-adhoc query statements (query plans) to speed up those subsequent executions. Triggering this method will wipe
        /// out the complete cache, which will not cause an interruption but rather all queries need to be re-prepared internally. This method
        /// is likely to be deprecated in the future once the server side query engine distributes its state throughout the cluster.
        /// </summary>
        /// <returns>
        /// An <see cref="int" /> representing the size of the cache before it was cleared.
        /// </returns>
        public int InvalidateQueryCache()
        {
            return QueryNodes.Sum(x => x.InvalidateQueryCache());
        }

        protected IIOService CreateIOService(PoolConfiguration poolConfiguration, IPEndPoint endpoint)
        {
            var connectionPool = ConnectionPoolFactory(poolConfiguration, endpoint);
            connectionPool.SaslMechanism = SaslFactory(UserName, Password, connectionPool, Transcoder);

            var ioService = IOServiceFactory(connectionPool);
            connectionPool.Initialize();

            return ioService;
        }

        protected void SwapServers(Dictionary<IPEndPoint, IServer> servers)
        {
            var old = Interlocked.Exchange(ref Servers, servers);
            if (old != null)
            {
                foreach (var server in old)
                {
                    //only dispose of a node that has been removed from the cluster map
                    if (!Servers.ContainsKey(server.Key))
                    {
                        Log.Info("Disposing node {0} from rev#{1}", server.Value.EndPoint, server.Value.Revision);
                        server.Value.Dispose();
                    }
                }
                old.Clear();
            }
        }
    }
}

#region [ License information          ]

/* ************************************************************
 *
 *    @author Couchbase <info@couchbase.com>
 *    @copyright 2014 Couchbase, Inc.
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
