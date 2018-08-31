﻿using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Couchbase.Analytics;
using Couchbase.Authentication.SASL;
using Couchbase.Core;
using Couchbase.Core.Transcoders;
using Couchbase.IO;
using Couchbase.IO.Operations;
using Couchbase.N1QL;
using Couchbase.Search;
using Couchbase.Views;

namespace Couchbase.Tests.Fakes
{
    internal class FakeServer : IServer
    {
        public FakeServer(IConnectionPool connectionPool, IViewClient viewClient, IQueryClient queryClient, IPEndPoint endPoint, IIOService service)
        {
            ConnectionPool = connectionPool;
            ViewClient = viewClient;
            QueryClient = queryClient;
            EndPoint = endPoint;
            Service = service;
        }

        public IConnectionPool ConnectionPool { get; private set; }

        public IViewClient ViewClient { get; private set; }

        public IQueryClient QueryClient { get; private set; }

        public ISearchClient SearchClient { get; internal set; }

        public IAnalyticsClient AnalyticsClient { get; internal set; }

        public IPEndPoint EndPoint { get; private set; }

        public IIOService Service { get; set; }

        public bool IsSearchNode { get; private set; }

        public bool IsSecure { get; private set; }

        public bool IsDead { get; private set; }

        public Task SendAsync<T>(IOperation<T> operation)
        {
            return Service.ExecuteAsync(operation);
        }

        public ISearchQueryResult Send(SearchQuery searchQuery)
        {
            throw new NotImplementedException();
        }

        public Task SendAsync(IOperation operation)
        {
            return Service.ExecuteAsync(operation);
        }

        public Func<string, string, IIOService, ITypeTranscoder, ISaslMechanism> SaslFactory { get; set; }

        public bool IsMgmtNode
        {
            get { throw new NotImplementedException(); }
        }

        public bool IsQueryNode
        {
            get { throw new NotImplementedException(); }
        }

        public bool IsDataNode
        {
            get { throw new NotImplementedException(); }
        }

        public bool IsIndexNode
        {
            get { throw new NotImplementedException(); }
        }

        public bool IsViewNode
        {
            get { throw new NotImplementedException(); }
        }

        public void MarkDead()
        {
            throw new NotImplementedException();
        }

        public bool IsDown
        {
            get { throw new NotImplementedException(); }
        }

        public void CheckOnline(bool isDead)
        {
            throw new NotImplementedException();
        }

        public Task<ISearchQueryResult> SendAsync(SearchQuery searchQuery)
        {
            throw new NotImplementedException();
        }

        public Task<ISearchQueryResult> SendAsync(SearchQuery searchQuery, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public IOperationResult<T> Send<T>(IOperation<T> operation)
        {
            throw new NotImplementedException();
        }

        public Task<IViewResult<T>> SendAsync<T>(IViewQueryable query)
        {
            throw new NotImplementedException();
        }

        public IQueryResult<T> Send<T>(IQueryRequest queryRequest)
        {
            throw new NotImplementedException();
        }

        public Task<IQueryResult<T>> SendAsync<T>(IQueryRequest queryRequest)
        {
            throw new NotImplementedException();
        }

        public Task<IQueryResult<T>> SendAsync<T>(IQueryRequest queryRequest, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public IQueryResult<T> Send<T>(string query)
        {
            throw new NotImplementedException();
        }

        public Task<IQueryResult<T>> SendAsync<T>(string query)
        {
            throw new NotImplementedException();
        }

        public string GetBaseViewUri(string name)
        {
            throw new NotImplementedException();
        }

        public string GetBaseQueryUri()
        {
            throw new NotImplementedException();
        }

        public void CreateSaslMechanismIfNotExists()
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public int InvalidateQueryCache()
        {
            throw new NotImplementedException();
        }


        public IOperationResult Send(IOperation operation)
        {
            throw new NotImplementedException();
        }

        public IViewResult<T> Send<T>(IViewQueryable query)
        {
            throw new NotImplementedException();
        }

        public IAnalyticsResult<T> Send<T>(IAnalyticsRequest analyticsRequest)
        {
            throw new NotImplementedException();
        }

        public Task<IAnalyticsResult<T>> SendAsync<T>(IAnalyticsRequest analyticsRequest, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Uri CachedViewBaseUri
        {
            get { throw new NotImplementedException(); }
        }

        public Uri CachedQueryBaseUri
        {
            get { throw new NotImplementedException(); }
        }


        public uint Revision
        {
            get { throw new NotImplementedException(); }
        }

        public bool IsAnalyticsNode
        {
            get
            {
                throw new NotImplementedException();
            }
        }
    }
}