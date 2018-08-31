﻿using System;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Couchbase.Configuration.Client;
using Couchbase.Configuration.Server.Serialization;
using Couchbase.IO.Http;
using Couchbase.Tests.Documents;
using Couchbase.Tests.Utils;
using Couchbase.Utils;
using Couchbase.Views;
using NUnit.Framework;

namespace Couchbase.Tests.Views
{
    [TestFixture]
    public class ViewClientTests
    {
        private readonly string _server = ConfigurationManager.AppSettings["serverIp"];

        private Uri _baseUri;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _baseUri = new Uri(string.Format("http://{0}:8092/", _server));

            using (var cluster = new Cluster(ClientConfigUtil.GetConfiguration()))
            {
                using (var bucket = cluster.OpenBucket("beer-sample"))
                {
                    var manager = bucket.CreateManager("Administrator", "password");

                    var get = manager.GetDesignDocument("beer_ext");
                    if (!get.Success)
                    {
                        var designDoc = ResourceHelper.ReadResource(@"Data\DesignDocs\beers_ext.json");
                        var inserted = manager.InsertDesignDocument("beer_ext", designDoc);
                        if (inserted.Success)
                        {
                            Console.WriteLine("Created 'beer_ext' design doc.");
                        }
                    }
                }
            }
        }

        [Test]
        public void When_Row_Is_Dynamic_Query_By_Key_Succeeds()
        {
            //arrange
            var query = new ViewQuery().
                From("beer_ext", "all_beers").
                Bucket("beer-sample").
                Limit(1).
                Development(false).
                BaseUri(_baseUri);

            var client = GetViewClient("beer-sample");

            //act
            var result = client.Execute<Beer>(query);

            var query2 = new ViewQuery().
                From("beer_ext", "all_beers").
                Bucket("beer-sample").
                Key(result.Rows.First().Id).
                BaseUri(_baseUri);

            var result2 = client.Execute<Beer>(query2);

            //assert
            Assert.AreEqual(result.Rows.First().Id, result2.Rows.First().Id);
        }

        [Test]
        public void When_Poco_Is_Supplied_Map_Results_To_It()
        {
            //arrange
            var query = new ViewQuery().
              From("beer_ext", "all_beers").
              Bucket("beer-sample").
              Limit(10).
              Development(false).
              BaseUri(_baseUri);

            var client = GetViewClient("beer-sample");

            //act
            var result = client.Execute<Beer>(query);

            //assert
            foreach (var viewRow in result.Rows)
            {
                Assert.IsNotNull(viewRow.Id);
            }
            Assert.IsNotNull(result.Rows);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(result.Rows.Count(), result.Values.Count());
        }

        [Test]
        public void When_Query_Is_Succesful_Rows_Are_Returned()
        {
            //arrange
            var query = new ViewQuery().
                From("beer", "brewery_beers").
                Bucket("beer-sample").
                Limit(10).
                BaseUri(_baseUri);

            var client = GetViewClient("beer-sample", 5000);

            //act
            var result = client.Execute<dynamic>(query);

            //assert
            Assert.IsNotNull(result.Rows);
            foreach (var viewRow in result.Rows)
            {
                Assert.IsNotNull(viewRow.Id);
            }
            Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
        }

        [Test]
        public void When_View_Is_Not_Found_404_Is_Returned()
        {
            //arrange
            var query = new ViewQuery().
                From("beer", "view_that_does_not_exist").
                Bucket("beer-sample").
                BaseUri(_baseUri);

            var client = GetViewClient("beer-sample");

            //act
            var result = client.Execute<dynamic>(query);

            //assert
            Assert.IsNotNull(result.Message);
            Assert.AreEqual(HttpStatusCode.NotFound, result.StatusCode);
            Assert.IsFalse(result.Success);
        }

        [Test]
        public void When_View_Is_Called_With_Invalid_Parameters_Error_Is_Returned()
        {
            //arrange
            var query = new ViewQuery().
                From("beer", "brewery_beers").
                Bucket("beer-sample").
                Group(true).
                BaseUri(_baseUri);

            var client = GetViewClient("beer-sample");

            //act
            var result = client.Execute<dynamic>(query);

            //assert
            Assert.AreEqual("query_parse_error", result.Error);
            Assert.AreEqual(HttpStatusCode.BadRequest, result.StatusCode);
            Assert.IsFalse(result.Success);
        }

        [Test]
        public void When_Url_Is_Invalid_Exception_Is_Returned()
        {
            //arrange
            var query = new ViewQuery().
                From("beer", "brewery_beers").
                Bucket("beer-sample").
                BaseUri(new Uri("http://192.168.56.105:8092/"));

            var client = GetViewClient("beer-sample", 5000);

            //act
            var result = client.Execute<dynamic>(query);

            //assert
            Assert.IsNotNull(result.Rows);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, result.StatusCode);
            Assert.IsNotNull(result.Exception);
        }

        [Test]
        public void When_Url_Is_Invalid_Exception_Is_Returned_2()
        {
            //arrange
            var query = new ViewQuery().
                From("beer", "brewery_beers").
                Bucket("beer-sample").
                BaseUri(new Uri("http://192.168.62.200:8092/"));

            var client = GetViewClient("beer-sample", 5000);

            //act
            var result = client.Execute<dynamic>(query);

            //assert
            Assert.IsNotNull(result.Rows);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, result.StatusCode);
            Assert.IsNotNull(result.Exception);
        }

        [Test]
        public void Test_ExecuteAsync()
        {
            //arrange
            var query = new ViewQuery().
                From("docs", "all_docs").
                Bucket("default").
                BaseUri(_baseUri);

            var client = GetViewClient("travel-sample");

            int n = 10000;
            var options = new ParallelOptions { MaxDegreeOfParallelism = 4};

            //act - needs to be refactored
            Parallel.For(0, n, options, async i =>
            {
                var result = await client.ExecuteAsync<dynamic>(query).ContinueOnAnyContext();
                Console.WriteLine("{0} {1} {2}", i, result.Success, result.Message);
            });
        }

        [Test]
        public void Test_Geo_Spatial_View()
        {
            //arrange
            var uriString = ClientConfigUtil.GetConfiguration().Servers.First().ToString();
            uriString = uriString.Replace("8091", "8092").Replace("pools", "travel-sample/");

            var query = new SpatialViewQuery().From("spatial", "routes")
                .Bucket("travel-sample")
                .Stale(StaleState.False)
                .Limit(10)
                .Skip(0)
                .BaseUri(new Uri(uriString));

            var client = GetViewClient("travel-sample");

            //act
            var results = client.Execute<dynamic>(query);

            //assert
            Assert.IsTrue(results.Success, results.Error);
        }

        public static IViewClient GetViewClient(string bucketName, int timeout = 75000)
        {
            var clientConfig = new ClientConfiguration
            {
                  ViewRequestTimeout = timeout
            };
            var bucketConfig = new BucketConfig {Name = bucketName};
            return new ViewClient(new CouchbaseHttpClient(clientConfig, bucketConfig)
            {
                Timeout = new TimeSpan(0, 0, 0, 0, clientConfig.ViewRequestTimeout)
            },
            new JsonDataMapper(clientConfig), ContextFactory.GetCouchbaseContext(clientConfig, bucketConfig));
        }
    }
}

#region [ License information ]

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