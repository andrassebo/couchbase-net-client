﻿using System.Collections.Generic;
using System.Linq;
using System.Net;
using Couchbase.Configuration.Server.Serialization;
using Couchbase.Core;
using Couchbase.Core.Buckets;
using Couchbase.Tests.Fakes;
using Couchbase.Utils;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Couchbase.Tests
{
    [TestFixture]
    public class VBucketTests
    {
        private IVBucket _vBucket;
        private IDictionary<IPEndPoint, IServer> _servers;
        private VBucketServerMap _vBucketServerMap;

        [OneTimeSetUp]
        public void SetUp()
        {
            var bucketConfig = ConfigUtil.ServerConfig.Buckets.First(x=>x.Name=="default");
            _vBucketServerMap = bucketConfig.VBucketServerMap;

            _servers = new Dictionary<IPEndPoint, IServer>();
            foreach (var node in bucketConfig.GetNodes())
            {
                _servers.Add(new IPEndPoint(node.GetIPAddress(), 8091),
                    new Server(new FakeIOService(node.GetIPEndPoint(), new FakeConnectionPool(), false),
                        node,
                        new FakeTranscoder(),
                        ContextFactory.GetCouchbaseContext()));
            }

            var vBucketMap = _vBucketServerMap.VBucketMap.First();
            var primary = vBucketMap[0];
            var replicas = new []{vBucketMap[1]};
            _vBucket = new VBucket(_servers, 0, primary, replicas, bucketConfig.Rev, _vBucketServerMap, "default");
        }

        [Test]
        public void TestLocatePrimary()
        {
            var primary = _vBucket.LocatePrimary();
            Assert.IsNotNull(primary);

            var expected = _servers.First();
            Assert.AreSame(expected.Value, primary);
        }

        [Test]
        public void TestLocateReplica()
        {
            const int replicaIndex = 0;
            var replica = _vBucket.LocateReplica(replicaIndex);
            Assert.IsNotNull(replica);

            var hostname = _vBucketServerMap.IPEndPoints[replicaIndex];
            var expected = _servers[hostname];
            Assert.AreSame(expected, replica);
        }

        [Test]
        public void When_BucketConfig_Has_Replicas_VBucketKeyMapper_Replica_Count_Is_Equal()
        {
            var json = ResourceHelper.ReadResource(@"Data\Configuration\config-with-replicas-complete.json");
            var bucketConfig = JsonConvert.DeserializeObject<BucketConfig>(json);

            var servers = new Dictionary<IPEndPoint, IServer>();
            foreach (var node in bucketConfig.GetNodes())
            {
                servers.Add(new IPEndPoint(node.GetIPAddress(), 8091),
                    new Server(new FakeIOService(node.GetIPEndPoint(), new FakeConnectionPool(), false),
                        node,
                        new FakeTranscoder(),
                        ContextFactory.GetCouchbaseContext()));
            }

            var mapper = new VBucketKeyMapper(servers, bucketConfig.VBucketServerMap, bucketConfig.Rev, bucketConfig.Name);
            var vBucket = (IVBucket)mapper.MapKey("somekey");

            const int expected = 3;
            Assert.AreEqual(expected, vBucket.Replicas.Count());
        }

        [Test]
        public void When_BucketConfig_Has_Replicas_VBucketKeyMapper_Replicas_Are_Equal()
        {
            var json = ResourceHelper.ReadResource(@"Data\Configuration\config-with-replicas-complete.json");
            var bucketConfig = JsonConvert.DeserializeObject<BucketConfig>(json);

            var servers = new Dictionary<IPEndPoint, IServer>();
            foreach (var node in bucketConfig.GetNodes())
            {
                servers.Add(new IPEndPoint(node.GetIPAddress(), 8091),
                    new Server(new FakeIOService(node.GetIPEndPoint(), new FakeConnectionPool(), false),
                        node,
                        new FakeTranscoder(),
                        ContextFactory.GetCouchbaseContext()));
            }

            var mapper = new VBucketKeyMapper(servers, bucketConfig.VBucketServerMap, bucketConfig.Rev, bucketConfig.Name);
            var vBucket = (IVBucket)mapper.MapKey("somekey");

            var index = mapper.GetIndex("somekey");
            var expected = bucketConfig.VBucketServerMap.VBucketMap[index];
            for (var i = 0; i < vBucket.Replicas.Length; i++)
            {
                Assert.AreEqual(vBucket.Replicas[i], expected[i+1]);
            }
        }

        [Test]
        public void When_BucketConfig_Has_Replicas_VBucketKeyMapper_LocateReplica_Returns_Correct_Server()
        {
            var json = ResourceHelper.ReadResource(@"Data\Configuration\config-with-replicas-complete.json");
            var bucketConfig = JsonConvert.DeserializeObject<BucketConfig>(json);

            var servers = new Dictionary<IPEndPoint, IServer>();
            foreach (var node in bucketConfig.GetNodes())
            {
                servers.Add(new IPEndPoint(node.GetIPAddress(), 8091),
                    new Server(new FakeIOService(node.GetIPEndPoint(), new FakeConnectionPool(), false),
                        node,
                        new FakeTranscoder(),
                        ContextFactory.GetCouchbaseContext()));
            }

            var mapper = new VBucketKeyMapper(servers, bucketConfig.VBucketServerMap, bucketConfig.Rev, bucketConfig.Name);
            var vBucket = (IVBucket)mapper.MapKey("somekey");

            foreach (var index in vBucket.Replicas)
            {
                var server = vBucket.LocateReplica(index);
                Assert.IsNotNull(server);

                var expected = bucketConfig.VBucketServerMap.ServerList[index];
                Assert.AreEqual(server.EndPoint.Address.ToString(), expected.Split(':').First());
            }
        }

        [Test]
        public void When_Primary_Is_Negative_Random_Server_Returned()
        {
            var json = ResourceHelper.ReadResource(@"Data\Configuration\config-with-negative-one-primary.json");
            var bucketConfig = JsonConvert.DeserializeObject<BucketConfig>(json);

            var servers = new Dictionary<IPEndPoint, IServer>();
            foreach (var node in bucketConfig.GetNodes())
            {
                servers.Add(new IPEndPoint(node.GetIPAddress(), 8091),
                    new Server(new FakeIOService(node.GetIPEndPoint(), new FakeConnectionPool(), false),
                        node,
                        new FakeTranscoder(),
                        ContextFactory.GetCouchbaseContext()));
            }

            var mapper = new VBucketKeyMapper(servers, bucketConfig.VBucketServerMap, bucketConfig.Rev, bucketConfig.Name);

            //maps to -1 primary
            const string key = "somekey0";
            var vBucket = (IVBucket)mapper.MapKey(key);
            Assert.AreEqual(-1, vBucket.Primary);

            var primary = vBucket.LocatePrimary();
            Assert.IsNotNull(primary);
        }

        [Test]
        public void When_Primary_Index_Is_Greater_Than_Cluster_Count_Random_Server_Returned()
        {
            var json = ResourceHelper.ReadResource(@"Data\Configuration\config-with-negative-one-primary.json");
            var bucketConfig = JsonConvert.DeserializeObject<BucketConfig>(json);

            var servers = new Dictionary<IPEndPoint, IServer>();
            foreach (var node in bucketConfig.GetNodes())
            {
                servers.Add(new IPEndPoint(node.GetIPAddress(), 8091),
                    new Server(new FakeIOService(node.GetIPEndPoint(), new FakeConnectionPool(), false),
                        node,
                        new FakeTranscoder(),
                        ContextFactory.GetCouchbaseContext()));
            }

            //remove one server
            servers.Remove(_vBucketServerMap.IPEndPoints.Skip(1).First());

            var mapper = new VBucketKeyMapper(servers, bucketConfig.VBucketServerMap, bucketConfig.Rev, bucketConfig.Name);

            //maps to -1 primary
            const string key = "somekey23";
            var vBucket = (IVBucket)mapper.MapKey(key);

            var primary = vBucket.LocatePrimary();
            Assert.IsNotNull(primary);
        }

        [Test]
        public void When_Replica_Index_OOR_LocatePrimary_Returns_Random_Server()
        {
            var server = new Server(
               new FakeIOService(IPEndPointExtensions.GetEndPoint("127.0.0.1:8091"),
               new FakeConnectionPool(), false),
               new NodeAdapter(new Node { Hostname = "127.0.0.1" },
               new NodeExt()),
               new FakeTranscoder(),
               ContextFactory.GetCouchbaseContext());

            var vbucket =
                new VBucket(new Dictionary<IPEndPoint, IServer>
                {
                    {IPEndPointExtensions.GetEndPoint("127.0.0.1:10210"), server},
                    {IPEndPointExtensions.GetEndPoint("127.0.0.2:10210"), server}
                },
                100, -1, new[] {2}, 0, new VBucketServerMap {ServerList = new []{"127.0.0.1:10210"}}, "default");
            var found = vbucket.LocatePrimary();
            Assert.IsNotNull(found);
        }

        [Test]
        public void When_Replica_Index_1_LocatePrimary_Returns_Random_Server()
        {
            var vbucket = new VBucket(new Dictionary<IPEndPoint, IServer>{}, 100, -1, new[] { 0 }, 0, new VBucketServerMap{ ServerList = new []{ "127.0.0.1:10210" }}, "default");
            var found = vbucket.LocatePrimary();
            Assert.IsNull(found);//should be null
        }

        [Test]
        public void When_Replica_Index_Negative_LocatePrimary_Returns_Random_Server()
        {
            var server = new Server(
                new FakeIOService(IPEndPointExtensions.GetEndPoint("127.0.0.1:8091"),
                new FakeConnectionPool(), false),
                new NodeAdapter(new Node { Hostname = "127.0.0.1" },
                new NodeExt()),
                new FakeTranscoder(),
                ContextFactory.GetCouchbaseContext());

            var vbucket =
                new VBucket(new Dictionary<IPEndPoint, IServer>
                {
                    {IPEndPointExtensions.GetEndPoint("127.0.0.1:10210"), server},
                    {IPEndPointExtensions.GetEndPoint("127.0.0.2:10210"), server}
                },
                100, -1, new[] { -1 }, 0,  new VBucketServerMap{ ServerList = new[] { "127.0.0.1:10210" }}, "default");
            var found = vbucket.LocatePrimary();
            Assert.IsNotNull(found);
        }

        [Test]
        public void When_Replica_Index_Postive_LocatePrimary_Returns_It()
        {
            var server = new Server(
                new FakeIOService(IPEndPointExtensions.GetEndPoint("127.0.0.1:8091"),
                new FakeConnectionPool(), false),
                new NodeAdapter(new Node { Hostname = "127.0.0.1" },
                new NodeExt()),
                new FakeTranscoder(),
                ContextFactory.GetCouchbaseContext());

            var vbucket =
                new VBucket(new Dictionary<IPEndPoint, IServer>
                {
                    {IPEndPointExtensions.GetEndPoint("127.0.0.1:10210"), server},
                    {IPEndPointExtensions.GetEndPoint("127.0.0.2:10210"), server}
                },
                100, -1, new[] { 0 }, 0, new VBucketServerMap { ServerList = new[] { "127.0.0.1:10210", "127.0.0.2:10210" } }, "default");
            var found = vbucket.LocatePrimary();
            Assert.IsNotNull(found);
        }

         [OneTimeTearDown]
        public void TearDown()
        {
             foreach (var server in _servers.Values)
             {
                 server.Dispose();
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