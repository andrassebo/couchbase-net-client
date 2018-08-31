﻿using System;
using System.Collections.Generic;

#if NET452
using System.Runtime.Serialization;
#endif

namespace Couchbase.Configuration.Server.Serialization
{
    public class BootstrapException : AggregateException
    {
        public BootstrapException()
        {
        }

        public BootstrapException(string message) : base(message)
        {
        }

        public BootstrapException(string message, Exception innerException) : base(message, innerException)
        {
        }

        public BootstrapException(string message, IEnumerable<Exception> innerExceptions) : base(message, innerExceptions)
        {
        }


#if NET452
        protected BootstrapException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#endif
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