﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Couchbase.Configuration.Client;
using Couchbase.Configuration.Server.Serialization;
using Couchbase.Core;
using Couchbase.Core.Buckets;
using Couchbase.IO;
using Couchbase.IO.Strategies.Awaitable;
using Couchbase.Tests.Helpers;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Couchbase.Tests
{
    [TestFixture]
    public class VBucketTests
    {
        private IVBucket _vBucket;
        private List<IServer> _servers;
            
        [TestFixtureSetUp]
        public void SetUp()
        {
            var bucket = ConfigUtil.ServerConfig.Buckets.First();
            var vBucketServerMap = bucket.VBucketServerMap;

            _servers = vBucketServerMap.
                ServerList.
                Select(server => new Server(ObjectFactory.CreateIOStrategy(server), new Node(), new ClientConfiguration())).
                Cast<IServer>().
                ToList();

            var vBucketMap = vBucketServerMap.VBucketMap.First();
            var primary = vBucketMap[0];
            var replicas = new int[]{vBucketMap[1]};
            _vBucket = new VBucket(_servers, 0, primary, replicas);
        }

        [Test]
        public void TestLocatePrimary()
        {
            var primary = _vBucket.LocatePrimary();
            Assert.IsNotNull(primary);

            var expected = _servers.First();
            Assert.AreSame(expected, primary);
        }

        [Test]
        public void TestLocateReplica()
        {
            const int replicaIndex = 0;
            var replica = _vBucket.LocateReplica(replicaIndex);
            Assert.IsNotNull(replica);

            var expected = _servers[replicaIndex];
            Assert.AreSame(expected, replica);
        }

        [Test]
        public void When_BucketConfig_Has_Replicas_VBucketKeyMapper_Replica_Count_Is_Equal()
        {
            var json = File.ReadAllText(@"Data\\Configuration\\config-with-replicas-complete.json");
            var bucketConfig = JsonConvert.DeserializeObject<BucketConfig>(json);
            var servers = bucketConfig.VBucketServerMap.
               ServerList.
               Select(server => new Server(ObjectFactory.CreateIOStrategy(server), new Node(), new ClientConfiguration())).
               Cast<IServer>().
               ToList();

            var mapper = new VBucketKeyMapper(servers, bucketConfig.VBucketServerMap);
            var vBucket = (IVBucket)mapper.MapKey("somekey");

            const int expected = 3;
            Assert.AreEqual(expected, vBucket.Replicas.Count());
        }

        [Test]
        public void When_BucketConfig_Has_Replicas_VBucketKeyMapper_Replicas_Are_Equal()
        {
            var json = File.ReadAllText(@"Data\\Configuration\\config-with-replicas-complete.json");
            var bucketConfig = JsonConvert.DeserializeObject<BucketConfig>(json);
            var servers = bucketConfig.VBucketServerMap.
               ServerList.
               Select(server => new Server(ObjectFactory.CreateIOStrategy(server), new Node(), new ClientConfiguration())).
               Cast<IServer>().
               ToList();

            var mapper = new VBucketKeyMapper(servers, bucketConfig.VBucketServerMap);
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
            var json = File.ReadAllText(@"Data\\Configuration\\config-with-replicas-complete.json");
            var bucketConfig = JsonConvert.DeserializeObject<BucketConfig>(json);
            var servers = bucketConfig.VBucketServerMap.
               ServerList.
               Select(server => new Server(ObjectFactory.CreateIOStrategy(server), new Node(), new ClientConfiguration())).
               Cast<IServer>().
               ToList();

            var mapper = new VBucketKeyMapper(servers, bucketConfig.VBucketServerMap);
            var vBucket = (IVBucket)mapper.MapKey("somekey");

            foreach (var index in vBucket.Replicas)
            {
                var server = vBucket.LocateReplica(index);
                Assert.IsNotNull(server);

                var expected = bucketConfig.VBucketServerMap.ServerList[index];
                Assert.AreEqual(server.EndPoint.Address.ToString(), expected.Split(':').First());
            }
        }

         [TestFixtureTearDown]
        public void TearDown()
        {
            _servers.ForEach(x=>x.Dispose());
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