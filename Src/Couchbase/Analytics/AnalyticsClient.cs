using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Couchbase.Authentication;
using Couchbase.Configuration;
using Couchbase.Configuration.Client;
using Couchbase.Logging;
using Couchbase.N1QL;
using Couchbase.Tracing;
using Couchbase.Utils;
using Couchbase.Views;

namespace Couchbase.Analytics
{
    internal class AnalyticsClient : HttpServiceBase, IAnalyticsClient
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(AnalyticsClient));
        internal const string AnalyticsPriorityHeaderName = "Analytics-Priority";

        public AnalyticsClient(HttpClient client, IDataMapper dataMapper, ConfigContextBase context)
            : base(client, dataMapper, context)
        {
        }

        /// <summary>
        /// Queries the specified request.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="request">The request.</param>
        /// <returns></returns>
        public IAnalyticsResult<T> Query<T>(IAnalyticsRequest request)
        {
            using (new SynchronizationContextExclusion())
            {
                return QueryAsync<T>(request, CancellationToken.None).Result;
            }
        }

        /// <summary>
        /// Queries the asynchronous.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="queryRequest">The query request.</param>
        /// <param name="token">The token.</param>
        /// <returns></returns>
        public async Task<IAnalyticsResult<T>> QueryAsync<T>(IAnalyticsRequest queryRequest, CancellationToken token)
        {
            var result = new AnalyticsResult<T>();

            FailureCountingUri baseUri;
            if (!TryGetUri(result, out baseUri))
            {
                return result;
            }

            ApplyCredentials(queryRequest, ClientConfiguration);

            string body;
            using (ClientConfiguration.Tracer.BuildSpan(queryRequest, CouchbaseOperationNames.RequestEncoding).StartActive())
            {
                body = queryRequest.GetFormValuesAsJson();
            }

            using (var content = new StringContent(body, System.Text.Encoding.UTF8, MediaType.Json))
            {
                try
                {
                    Log.Trace("Sending analytics query cid{0}: {1}", queryRequest.CurrentContextId, baseUri);

                    HttpResponseMessage response;
                    using (ClientConfiguration.Tracer.BuildSpan(queryRequest, CouchbaseOperationNames.DispatchToServer).StartActive())
                    {
                        var request = new HttpRequestMessage(HttpMethod.Post, baseUri)
                        {
                            Content = content
                        };

                        if (queryRequest is AnalyticsRequest req && req.PriorityValue != 0)
                        {
                            request.Headers.Add(AnalyticsPriorityHeaderName, new[] {req.PriorityValue.ToString()});
                        }

                        response = await HttpClient.SendAsync(request, token).ContinueOnAnyContext();
                    }

                    using (var scope = ClientConfiguration.Tracer.BuildSpan(queryRequest, CouchbaseOperationNames.ResponseDecoding).StartActive())
                    using (var stream = await response.Content.ReadAsStreamAsync().ContinueOnAnyContext())
                    {
                        result = DataMapper.Map<AnalyticsResultData<T>>(stream).ToQueryResult();
                        result.Success = result.Status == QueryStatus.Success;
                        result.HttpStatusCode = response.StatusCode;
                        Log.Trace("Received analytics query cid{0}: {1}", result.ClientContextId, result.ToString());

                        scope.Span.SetPeerLatencyTag(result.Metrics.ElaspedTime);
                    }
                    baseUri.ClearFailed();
                }
                catch (OperationCanceledException e)
                {
                    var operationContext = OperationContext.CreateAnalyticsContext(queryRequest.CurrentContextId, Context.BucketName, baseUri?.Authority);
                    if (queryRequest is AnalyticsRequest request)
                    {
                        operationContext.TimeoutMicroseconds = request.TimeoutValue;
                    }

                    Log.Info(operationContext.ToString());
                    ProcessError(e, result);
                }
                catch (HttpRequestException e)
                {
                    Log.Info("Failed analytics query cid{0}: {1}", queryRequest.CurrentContextId, baseUri);
                    baseUri.IncrementFailed();
                    ProcessError(e, result);
                    Log.Error(e);
                }
                catch (AggregateException ae)
                {
                    ae.Flatten().Handle(e =>
                    {
                        Log.Info("Failed analytics query cid{0}: {1}", queryRequest.CurrentContextId, baseUri);
                        ProcessError(e, result);
                        return true;
                    });
                }
                catch (Exception e)
                {
                    Log.Info("Failed analytics query cid{0}: {1}", queryRequest.CurrentContextId, baseUri);
                    Log.Info(e);
                    ProcessError(e, result);
                }
            }

            UpdateLastActivity();

            return result;
        }

        private static void ProcessError<T>(Exception exception, AnalyticsResult<T> queryResult)
        {
            const string message = "Check Exception and Error fields for details.";
            queryResult.Status = QueryStatus.Fatal;
            queryResult.HttpStatusCode = HttpStatusCode.BadRequest;
            queryResult.Success = false;
            queryResult.Message = message;
            queryResult.Exception = exception;
        }

        private bool TryGetUri<T>(AnalyticsResult<T> result, out FailureCountingUri uri)
        {
            uri = Context.GetAnalyticsUri();
            if (uri != null && !string.IsNullOrEmpty(uri.AbsoluteUri))
            {
                return true;
            }

            Log.Error(ExceptionUtil.EmptyUriTryingSubmitN1qlQuery);
            ProcessError(new InvalidOperationException(ExceptionUtil.EmptyUriTryingSubmitN1QlQuery), result);
            return false;
        }

        private static void ApplyCredentials(IAnalyticsRequest request, ClientConfiguration config)
        {
            if (config.HasCredentials)
            {
                var creds = config.GetCredentials(AuthContext.ClusterAnalytics);
                foreach (var cred in creds)
                {
                    request.Credentials(cred.Key, cred.Value, false);
                }
            }
        }
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
