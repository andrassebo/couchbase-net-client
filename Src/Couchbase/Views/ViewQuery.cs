using System;
using System.Collections;
using System.Text;
using Couchbase.Core;
using Couchbase.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenTracing;

namespace Couchbase.Views
{
    /// <summary>
    /// Implemented as an object that can query a Couchbase View.
    /// </summary>
    public class ViewQuery : IViewQuery
    {
        private const string UriFormat = "{0}://{1}:{2}/{3}/";
        public const string Design = "_design";
        public const string DevelopmentViewPrefix = "dev_";
        public const string ViewMethod = "_view";
        public const string ForwardSlash = "/";
        public const string QueryOperator = "?";
        public const string QueryArgPattern = "{0}={1}&";
        private const string DefaultHost = "http://localhost:8092/";
        private const uint DefaultPort = 8092;
        private const uint DefaultSslPort = 18092;
        private const string Http = "http";
        private const string Https = "https";

        private Uri _baseUri;
        private string _designDoc;
        private string _viewName;
        private bool? _development;
        private int? _skipCount;
        private StaleState _staleState;
        private bool? _descending;
        private object _endKey;
        private object _endDocId;
        private bool? _fullSet;
        private bool? _group;
        private int? _groupLevel;
        private bool? _inclusiveEnd;
        private object _key;
        private IEnumerable _keys;
        private int? _limit;
        private bool? _continueOnError;
        private bool? _reduce;
        private object _startKey;
        private object _startKeyDocId;
        private int? _connectionTimeout;

        private struct QueryArguments
        {
            public const string Descending = "descending";
            public const string EndKey = "endkey";
            public const string EndKeyDocId = "endkey_docid";
            public const string FullSet = "full_set";
            public const string Group = "group";
            public const string GroupLevel = "group_level";
            public const string InclusiveEnd = "inclusive_end";
            public const string Key = "key";
            public const string Keys = "keys";
            public const string Limit = "limit";
            public const string OnError = "on_error";
            public const string Reduce = "reduce";
            public const string Skip = "skip";
            public const string Stale = "stale";
            public const string StartKey = "startkey";
            public const string StartKeyDocId = "startkey_docid";
            public const string ConnectionTimeout = "connection_timeout";
        }

        public ViewQuery()
            : this(null, DefaultHost)
        {
        }

        public ViewQuery(string baseUri)
            : this(null, baseUri)
        {
        }

        public ViewQuery(string bucketName, string baseUri)
            : this(bucketName, baseUri, null)
        {
                _baseUri = new Uri(baseUri);
        }

        public ViewQuery(string bucketName, string designDoc, string viewName)
        {
            _baseUri = new Uri(DefaultHost);
            BucketName = bucketName;
            _designDoc = designDoc;
            _viewName = viewName;
        }

        /// <summary>
        /// When true, the generated url will contain 'https' and use port 18092
        /// </summary>
        public bool UseSsl { get; set; }

        /// <summary>
        /// Specifies the bucket and design document to target for a query.
        /// </summary>
        /// <param name="designDoc">The bucket to target</param>
        /// <param name="view">The design document to use</param>
        /// <returns></returns>
        public IViewQuery From(string designDoc, string view)
        {
            return DesignDoc(designDoc).View(view);
        }

        /// <summary>
        /// Sets the name of the Couchbase Bucket.
        /// </summary>
        /// <param name="name">The name of the bucket.</param>
        /// <returns>An IViewQuery object for chaining</returns>
        public IViewQuery Bucket(string name)
        {
            BucketName = name;
            return this;
        }

        /// <summary>
        /// Sets the name of the design document.
        /// </summary>
        /// <param name="name">The name of the design document to use.</param>
        /// <returns>An IViewQuery object for chaining</returns>
        public IViewQuery DesignDoc(string name)
        {
            _designDoc = name;
            return this;
        }

        /// <summary>
        /// Sets the name of the view to query.
        /// </summary>
        /// <param name="name">The name of the view to query.</param>
        /// <returns>An IViewQuery object for chaining</returns>
        public IViewQuery View(string name)
        {
            _viewName = name;
            return this;
        }

        /// <summary>
        /// Skip this number of records before starting to return the results
        /// </summary>
        /// <param name="count">The number of records to skip</param>
        /// <returns></returns>
        public IViewQuery Skip(int count)
        {
            _skipCount = count;
            return this;
        }

        /// <summary>
        /// Allow the results from a stale view to be used. The default is StaleState.Ok; for development work set to StaleState.False
        /// </summary>
        /// <param name="staleState">The staleState value to use.</param>
        /// <returns>An IViewQuery object for chaining</returns>
        public IViewQuery Stale(StaleState staleState)
        {
            _staleState = staleState;
            return this;
        }

        /// <summary>
        /// Return the documents in ascending by key order
        /// </summary>
        /// <returns>An IViewQuery object for chaining</returns>
        public IViewQuery Asc()
        {
            _descending = false;
            return this;
        }

        /// <summary>
        /// Return the documents in descending by key order
        /// </summary>
        /// <returns>An IViewQuery object for chaining</returns>
        public IViewQuery Desc()
        {
            _descending = true;
            return this;
        }

        /// <summary>
        /// Stop returning records when the specified key is reached. Key must be specified as a JSON value.
        /// </summary>
        /// <param name="endKey">The key to stop at</param>
        /// <returns>An IViewQuery object for chaining</returns>
        public IViewQuery EndKey(object endKey)
        {
            _endKey = EncodeParameter(endKey);
            return this;
        }

        /// <summary>
        /// Stop returning records when the specified key is reached. Key must be specified as a JSON value.
        /// </summary>
        /// <param name="endKey">The key to stop at</param>
        /// <param name="encode">True to JSON encode and URI escape the value.</param>
        /// <returns>An IViewQuery object for chaining</returns>
        public IViewQuery EndKey(object endKey, bool encode)
        {
            _endKey = encode ? EncodeParameter(endKey) : endKey;
            return this;
        }

        /// <summary>
        /// Stop returning records when the specified document ID is reached
        /// </summary>
        /// <param name="endDocId">The document Id to stop at.</param>
        /// <returns>An IViewQuery object for chaining</returns>
        public IViewQuery EndKeyDocId(object endDocId)
        {
            _endDocId = endDocId;
            return this;
        }

        /// <summary>
        /// Use the full cluster data set (development views only).
        /// </summary>
        /// <returns>An IViewQuery object for chaining</returns>
        public IViewQuery FullSet()
        {
            _fullSet = true;
            return this;
        }

        /// <summary>
        /// Group the results using the reduce function to a group or single row
        /// </summary>
        /// <param name="group">True to group using the reduce function into a single row</param>
        /// <returns>An IViewQuery object for chaining</returns>
        public IViewQuery Group(bool group)
        {
            _group = group;
            return this;
        }

        /// <summary>
        /// Specify the group level to be used
        /// </summary>
        /// <param name="level">The level of grouping to use</param>
        /// <returns>An IViewQuery object for chaining</returns>
        public IViewQuery GroupLevel(int level)
        {
            _groupLevel = level;
            return this;
        }

        /// <summary>
        /// Specifies whether the specified end key should be included in the result
        /// </summary>
        /// <param name="inclusiveEnd">True to include the last key in the result</param>
        /// <returns>An IViewQuery object for chaining</returns>
        public IViewQuery InclusiveEnd(bool inclusiveEnd)
        {
            _inclusiveEnd = inclusiveEnd;
            return this;
        }

        /// <summary>
        /// Return only documents that match the specified key. Key must be specified as a JSON value.
        /// </summary>
        /// <param name="key">The key to retrieve</param>
        /// <returns>An IViewQuery object for chaining</returns>
        public IViewQuery Key(object key)
        {
            _key = EncodeParameter(key);
            return this;
        }

        /// <summary>
        /// Return only documents that match the specified key. Key must be specified as a JSON value.
        /// </summary>
        /// <param name="key">The key to retrieve</param>
        /// <param name="encode">True to JSON encode and URI escape the value.</param>
        /// <returns>An IViewQuery object for chaining</returns>
        public IViewQuery Key(object key, bool encode)
        {
            _key = encode ? EncodeParameter(key) : key;
            return this;
        }

        /// <summary>
        /// Return only documents that match one of keys specified within the given array. Key must be specified as a JSON value. Sorting is not applied when using this option.
        /// </summary>
        /// <param name="keys">The keys to retrieve</param>
        /// <returns>An IViewQuery object for chaining</returns>
        public IViewQuery Keys(IEnumerable keys)
        {
            _keys = keys;
            return this;
        }

        /// <summary>
        /// Return only documents that match one of keys specified within the given array. Key must be specified as a JSON value. Sorting is not applied when using this option.
        /// </summary>
        /// <param name="keys">The keys to retrieve</param>
        /// <param name="encode">True to JSON encode and URI escape the value.</param>
        /// <returns>An IViewQuery object for chaining</returns>
        [Obsolete("Keys attribute is no longer submitted via query string. Use Keys(IEnumerable) instead.")]
        public IViewQuery Keys(IEnumerable keys, bool encode)
        {
            _keys = keys;
            return this;
        }

        /// <summary>
        /// Limit the number of the returned documents to the specified number
        /// </summary>
        /// <param name="limit">The numeric limit</param>
        /// <returns>An IViewQuery object for chaining</returns>
        public IViewQuery Limit(int limit)
        {
            _limit = limit;
            return this;
        }

        /// <summary>
        /// Sets the response in the event of an error
        /// </summary>
        /// <param name="stop">True to stop in the event of an error; true to continue</param>
        /// <returns>An IViewQuery object for chaining</returns>
        public IViewQuery OnError(bool stop)
        {
            _continueOnError = stop;
            return this;
        }

        /// <summary>
        /// Use the reduction function
        /// </summary>
        /// <param name="reduce">True to use the reduduction property. Default is false;</param>
        /// <returns>An IViewQuery object for chaining</returns>
        public IViewQuery Reduce(bool reduce)
        {
            _reduce = reduce;
            return this;
        }

        /// <summary>
        /// Return records with a value equal to or greater than the specified key. Key must be specified as a JSON value.
        /// </summary>
        /// <param name="startKey">The key to return records greater than or equal to.</param>
        /// <returns>An IViewQuery object for chaining</returns>
        public IViewQuery StartKey(object startKey)
        {
            _startKey = EncodeParameter(startKey);
            return this;
        }

        /// <summary>
        /// Return records with a value equal to or greater than the specified key. Key must be specified as a JSON value.
        /// </summary>
        /// <param name="startKey">The key to return records greater than or equal to.</param>
        /// <param name="encode">True to JSON encode and URI escape the value.</param>
        /// <returns>An IViewQuery object for chaining</returns>
        public IViewQuery StartKey(object startKey, bool encode)
        {
            _startKey = encode ? EncodeParameter(startKey) : startKey;
            return this;
        }

        /// <summary>
        /// Return records starting with the specified document ID.
        /// </summary>
        /// <param name="startKeyDocId">The docId to return records greater than or equal to.</param>
        /// <returns>An IViewQuery object for chaining</returns>
        public IViewQuery StartKeyDocId(object startKeyDocId)
        {
            _startKeyDocId = startKeyDocId;
            return this;
        }

        /// <summary>
        /// The number of seconds before the request will be terminated if it has not completed.
        /// </summary>
        /// <param name="timeout">The period of time in seconds</param>
        /// <returns></returns>
        public IViewQuery ConnectionTimeout(int timeout)
        {
            _connectionTimeout = timeout;
            return this;
        }

        /// <summary>
        /// Sets the base uri for the query if it's not set in the constructor.
        /// </summary>
        /// <param name="uri">The base uri to use - this is normally set internally and may be overridden by configuration.</param>
        /// <returns>An IViewQuery object for chaining</returns>
        /// <remarks>Note that this will override the baseUri set in the ctor. Additionally, this method may be called internally by the <see cref="IBucket"/> and overridden.</remarks>
        IViewQueryable IViewQueryable.BaseUri(Uri uri)
        {
            _baseUri = uri;
            return this;
        }

        /// <summary>
        /// Toggles the query between development or production dataset and View.
        /// </summary>
        /// <param name="development">If true the development View will be used</param>
        /// <returns>An IViewQuery object for chaining</returns>
        /// <remarks>The default is false; use the published, production view.</remarks>
        public IViewQuery Development(bool development)
        {
            _development = development;
            return this;
        }

        /// <summary>
        /// Toogles the if query result to is to be streamed. This is useful for large result sets in that it limits the
        /// working size of the query and helps reduce the possibility of a <see cref="OutOfMemoryException" /> from occurring.
        /// </summary>
        /// <param name="useStreaming">if set to <c>true</c> streams the results as you iterate through the response.</param>
        /// <returns>An IViewQueryable object for chaining</returns>
        public IViewQueryable UseStreaming(bool useStreaming)
        {
            IsStreaming = useStreaming;
            return this;
        }

        /// <summary>
        /// Gets a value indicating if the result should be streamed.
        /// </summary>
        /// <value><c>true</c> if the query result is to be streamed; otherwise, <c>false</c>.</value>
        public bool IsStreaming { get; private set; }

        /// <summary>
        /// Gets the name of the <see cref="IBucket"/> that the query is targeting.
        /// </summary>
        public string BucketName { get; private set; }

        /// <summary>
        /// JSON encodes the parameter and URI escapes the input parameter.
        /// </summary>
        /// <param name="parameter">The parameter to encode.</param>
        /// <returns>A JSON and URI escaped copy of the parameter.</returns>
        private static string EncodeParameter(object parameter)
        {
            return Uri.EscapeDataString(JsonConvert.SerializeObject(parameter));
        }

        /// <summary>
        /// Returns the raw REST URI which can be executed in a browser or using curl.
        /// </summary>
        /// <returns></returns>
        public Uri RawUri()
        {
            if (_baseUri == null)
            {
                var protocol = UseSsl ? Https : Http;
                var port = UseSsl ? DefaultSslPort : DefaultPort;
                _baseUri = new Uri(string.Format(UriFormat, protocol, DefaultHost, port, BucketName));
            }

            return new Uri(_baseUri, GetRelativeUri());
        }

        public string GetRelativeUri()
        {
            var relativeUri = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(BucketName) &&
                string.IsNullOrWhiteSpace(_baseUri.PathAndQuery) || _baseUri.PathAndQuery.Equals("/"))
            {
                relativeUri.Append(ForwardSlash);
                relativeUri.Append(BucketName);
                relativeUri.Append(ForwardSlash);
            }

            relativeUri.Append(Design);
            relativeUri.Append(ForwardSlash);

            if (_development.HasValue && _development.Value)
            {
                relativeUri.Append(DevelopmentViewPrefix);
            }

            relativeUri.Append(_designDoc);
            relativeUri.Append(ForwardSlash);
            relativeUri.Append(ViewMethod);
            relativeUri.Append(ForwardSlash);
            relativeUri.Append(_viewName);

            var queryParameters = GetQueryParams();
            if (!string.IsNullOrEmpty(queryParameters))
            {
                relativeUri.Append(QueryOperator);
                relativeUri.Append(queryParameters);
            }

            return relativeUri.ToString();
        }

        public string GetQueryParams()
        {
            var queryParams = new StringBuilder();
            if (_staleState != StaleState.None)
            {
                queryParams.AppendFormat(QueryArgPattern, QueryArguments.Stale, _staleState.ToLowerString());
            }
            if (_descending.HasValue)
            {
                queryParams.AppendFormat(QueryArgPattern, QueryArguments.Descending, _descending.ToLowerString());
            }
            if (_continueOnError.HasValue)
            {
                queryParams.AppendFormat(QueryArgPattern, QueryArguments.OnError, _continueOnError.Value ? "continue" : "stop");
            }
            if (_endDocId != null)
            {
                queryParams.AppendFormat(QueryArgPattern, QueryArguments.EndKeyDocId, _endDocId);
            }
            if (_endKey != null)
            {
                queryParams.AppendFormat(QueryArgPattern, QueryArguments.EndKey, _endKey);
            }
            if (_fullSet.HasValue)
            {
                queryParams.AppendFormat(QueryArgPattern, QueryArguments.FullSet, _fullSet.ToLowerString());
            }
            if (_group.HasValue)
            {
                queryParams.AppendFormat(QueryArgPattern, QueryArguments.Group, _group.ToLowerString());
            }
            if (_groupLevel.HasValue)
            {
                queryParams.AppendFormat(QueryArgPattern, QueryArguments.GroupLevel, _groupLevel);
            }
            if (_inclusiveEnd.HasValue)
            {
                queryParams.AppendFormat(QueryArgPattern, QueryArguments.InclusiveEnd, _inclusiveEnd.ToLowerString());
            }
            if (_key != null)
            {
                queryParams.AppendFormat(QueryArgPattern, QueryArguments.Key, _key);
            }
            if (_limit.HasValue)
            {
                queryParams.AppendFormat(QueryArgPattern, QueryArguments.Limit, _limit);
            }
            if (_reduce.HasValue)
            {
                queryParams.AppendFormat(QueryArgPattern, QueryArguments.Reduce, _reduce.ToLowerString());
            }
            if (_startKey != null)
            {
                queryParams.AppendFormat(QueryArgPattern, QueryArguments.StartKey, _startKey);
            }
            if (_startKeyDocId != null)
            {
                queryParams.AppendFormat(QueryArgPattern, QueryArguments.StartKeyDocId, _startKeyDocId);
            }
            if (_skipCount.HasValue)
            {
                queryParams.AppendFormat(QueryArgPattern, QueryArguments.Skip, _skipCount);
            }
            if (_connectionTimeout.HasValue)
            {
                queryParams.AppendFormat(QueryArgPattern, QueryArguments.ConnectionTimeout, _connectionTimeout);
            }

            return queryParams.ToString().TrimEnd('&');
        }

        /// <summary>
        /// The number of times the view request was retried if it fails before succeeding or giving up.
        /// </summary>
        /// <remarks>Used internally.</remarks>
        public int RetryAttempts { get; set; }

        /// <summary>
        /// Builds a JSON string of the <see cref="IViewQueryable"/> used for posting the query to a Couchbase Server.
        /// </summary>
        public string CreateRequestBody()
        {
            var json = new JObject();
            if (_keys != null)
            {
                json[QueryArguments.Keys] = JToken.FromObject(_keys);
            }

            return json.ToString(Formatting.None);
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
