﻿using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Couchbase.Logging;
using Couchbase.Authentication;
using Couchbase.Configuration;
using Couchbase.Configuration.Client;
using Couchbase.Configuration.Server.Serialization;
using Couchbase.Utils;

namespace Couchbase.IO.Http
{
    public class CouchbaseHttpClient : HttpClient
    {
        private const string UserAgentHeaderName = "User-Agent";
        private static readonly ILog Log = LogManager.GetLogger<CouchbaseHttpClient>();

        //used by all http services
        internal CouchbaseHttpClient(ClientConfiguration config, IBucketConfig bucketConfig)
            : this (CreateClientHandler(config, bucketConfig))
        {
            DefaultRequestHeaders.ExpectContinue = config.Expect100Continue;
        }

        internal CouchbaseHttpClient(ConfigContextBase context)
            : this (CreateClientHandler(context.ClientConfig, context.BucketConfig))
        {
            DefaultRequestHeaders.ExpectContinue = context.ClientConfig.Expect100Continue;
        }

        /// <summary>
        /// used by BucketManager and ClientManager
        /// </summary>
        /// <param name="bucketName"></param>
        /// <param name="password"></param>
        /// <param name="config"></param>
        internal CouchbaseHttpClient(string bucketName, string password, ClientConfiguration config)
            : this(CreateClientHandler(bucketName, password, config))
        {
        }

#if NET452
        internal CouchbaseHttpClient(WebRequestHandler handler)
#else
        internal CouchbaseHttpClient(HttpClientHandler handler)
#endif
            : base(handler)
        {
            DefaultRequestHeaders.Add(UserAgentHeaderName, ClientIdentifier.GetClientDescription());
        }


#if NET452
        private static WebRequestHandler CreateClientHandler(ClientConfiguration clientConfiguration, IBucketConfig bucketConfig)
#else
        private static HttpClientHandler CreateClientHandler(ClientConfiguration clientConfiguration, IBucketConfig bucketConfig)
#endif
        {
            Log.Debug("Creating CouchbaseClientHandler.");
            if (clientConfiguration.HasCredentials && clientConfiguration.Authenticator.AuthenticatorType == AuthenticatorType.Password)
            {
                var credentials = clientConfiguration.Authenticator.GetCredentials(AuthContext.BucketKv).First();
                return CreateClientHandler(credentials.Key, credentials.Value, clientConfiguration);
            }

            if (bucketConfig != null)
            {
                return CreateClientHandler(bucketConfig.Name, bucketConfig.Password, clientConfiguration);
            }

            // This is not a bucket-specific client, use no authentication if there are not cluster level credentials
            return CreateClientHandler(null, null, clientConfiguration);
        }

#if NET452
        private static WebRequestHandler CreateClientHandler(string username, string password, ClientConfiguration config)
#else
        private static HttpClientHandler CreateClientHandler(string username, string password, ClientConfiguration config)
#endif
        {
#if NET452
            WebRequestHandler handler;
#else
            HttpClientHandler handler;
#endif
            //for x509 cert authentication
            if (config != null && config.EnableCertificateAuthentication)
            {
                handler = new NonAuthenticatingHttpClientHandler
                {
                    ClientCertificateOptions = ClientCertificateOption.Manual
                };
#if NETSTANDARD
                handler.SslProtocols = SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12;
                handler.ClientCertificates.AddRange(config.CertificateFactory());

#endif
            }
            else
            {
                handler = new AuthenticatingHttpClientHandler(username, password);
            }

#if NET452
            // ReSharper disable once PossibleNullReferenceException
            handler.ServerCertificateValidationCallback = config.HttpServerCertificateValidationCallback ??
                                                          OnCertificateValidation;
#else
            try
            {
                handler.CheckCertificateRevocationList = config.EnableCertificateRevocation;
                handler.ServerCertificateCustomValidationCallback = config?.HttpServerCertificateValidationCallback ??
                                                                    OnCertificateValidation;
            }
            catch (NotImplementedException)
            {
                Log.Debug("Cannot set ServerCertificateCustomValidationCallback, not supported on this platform");
            }

            if (config != null)
            {
                try
                {
                    handler.MaxConnectionsPerServer = config.DefaultConnectionLimit;
                }
                catch (PlatformNotSupportedException e)
                {
                    Log.Debug("Cannot set MaxConnectionsPerServer, not supported on this platform", e);
                }
            }
#endif
            return handler;
        }

#if NET452
        private static bool OnCertificateValidation(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
#else
        private static bool OnCertificateValidation(HttpRequestMessage request, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
#endif
        {
            Log.Info("Validating certificate [IgnoreRemoteCertificateNameMismatch={0}]: {1}", ClientConfiguration.IgnoreRemoteCertificateNameMismatch, sslPolicyErrors);

            if (ClientConfiguration.IgnoreRemoteCertificateNameMismatch)
            {
                if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateNameMismatch)
                {
                    return true;
                }
            }

            return sslPolicyErrors == SslPolicyErrors.None;
        }
    }
}

#region [ License information          ]

/* ************************************************************
 *
 *    @author Couchbase <info@couchbase.com>
 *    @copyright 2015 Couchbase, Inc.
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
