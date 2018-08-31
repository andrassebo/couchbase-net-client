﻿using Couchbase.Logging;
using Couchbase.IO.Converters;
using Couchbase.IO.Operations;
using System;
using System.Text;
using Couchbase.Core.Serialization;

namespace Couchbase.Core.Transcoders
{
    /// <summary>
    /// Provides the default implementation for <see cref="ITypeTranscoder"/> interface.
    /// </summary>
    public class DefaultTranscoder : ITypeTranscoder
    {
        private static readonly ILog Log = LogManager.GetLogger<DefaultTranscoder>();

        public DefaultTranscoder()
            : this(new DefaultConverter())
        {
        }

        public DefaultTranscoder(IByteConverter converter)
            : this(converter, new DefaultSerializer())
        {
        }

        public DefaultTranscoder(IByteConverter converter, ITypeSerializer serializer)
        {
            Serializer = serializer;
            Converter = converter;
        }

        /// <summary>
        /// Gets or sets the serializer used by the <see cref="ITypeTranscoder" /> implementation.
        /// </summary>
        public ITypeSerializer Serializer { get; set; }

        /// <summary>
        /// Gets or sets the byte converter used by used by the <see cref="ITypeTranscoder" /> implementation.
        /// </summary>
        public IByteConverter Converter { get; set; }

        public virtual Flags GetFormat<T>(T value)
        {
            var dataFormat = DataFormat.Json;
            var typeCode = Type.GetTypeCode(typeof(T));
            switch (typeCode)
            {
                case TypeCode.Object:
                    if (typeof(T) == typeof(Byte[]))
                    {
                        dataFormat = DataFormat.Binary;
                    }
                    break;
                case TypeCode.Boolean:
                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                case TypeCode.DateTime:
                    dataFormat = DataFormat.Json;
                    break;
                case TypeCode.Char:
                case TypeCode.String:
                    dataFormat = DataFormat.String;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return new Flags() { Compression = Compression.None, DataFormat = dataFormat, TypeCode = typeCode };
        }

        /// <summary>
        /// Encodes the specified value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value">The value of the key to encode.</param>
        /// <param name="flags">The flags used for decoding the response.</param>
        /// <param name="opcode"></param>
        /// <returns></returns>
        /// <exception cref="System.ArgumentException"></exception>
        /// <exception cref="System.ArgumentOutOfRangeException"></exception>
        public virtual byte[] Encode<T>(T value, Flags flags, OperationCode opcode)
        {
            byte[] bytes;
            switch (flags.DataFormat)
            {
                case DataFormat.Reserved:
                case DataFormat.Private:
                case DataFormat.String:
                    bytes = Encode(value, flags.TypeCode, opcode);
                    break;

                case DataFormat.Json:
                    bytes = SerializeAsJson(value);
                    break;

                case DataFormat.Binary:
                    if (typeof(T) == typeof(byte[]))
                    {
                        bytes = value as byte[];
                    }
                    else
                    {
                        var msg = string.Format("The value of T does not match the DataFormat provided: {0}",
                            flags.DataFormat);
                        throw new ArgumentException(msg);
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
            return bytes;
        }

        /// <summary>
        /// Encodes the specified value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value">The value.</param>
        /// <param name="typeCode">Type to use for encoding</param>
        /// <param name="opcode"></param>
        /// <returns></returns>
        /// <exception cref="System.ArgumentOutOfRangeException"></exception>
        public virtual byte[] Encode<T>(T value, TypeCode typeCode, OperationCode opcode)
        {
            var bytes = new byte[] { };
            switch (typeCode)
            {
                case TypeCode.Empty:
#if NET452
                case TypeCode.DBNull:
#endif
                case TypeCode.String:
                case TypeCode.Char:
                    Converter.FromString(Convert.ToString(value), ref bytes, 0);
                    break;

                case TypeCode.Int16:
                    Converter.FromInt16(Convert.ToInt16(value), ref bytes, 0, false);
                    break;

                case TypeCode.UInt16:
                    Converter.FromUInt16(Convert.ToUInt16(value), ref bytes, 0, false);
                    break;

                case TypeCode.Int32:
                    Converter.FromInt32(Convert.ToInt32(value), ref bytes, 0, false);
                    break;

                case TypeCode.UInt32:
                    Converter.FromUInt32(Convert.ToUInt32(value), ref bytes, 0, false);
                    break;

                case TypeCode.Int64:
                    Converter.FromInt64(Convert.ToInt64(value), ref bytes, 0, false);
                    break;

                case TypeCode.UInt64:
                    if (opcode == OperationCode.Increment || opcode == OperationCode.Decrement)
                    {
                        Converter.FromUInt64(Convert.ToUInt64(value), ref bytes, 0, true);
                    }
                    else
                    {
                        Converter.FromUInt64(Convert.ToUInt64(value), ref bytes, 0, false);
                    }
                    break;

                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                case TypeCode.DateTime:
                case TypeCode.Boolean:
                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Object:
                    bytes = SerializeAsJson(value);
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
            return bytes;
        }

        /// <summary>
        /// Decodes the specified buffer.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="buffer">The buffer.</param>
        /// <param name="offset">The offset.</param>
        /// <param name="length">The length.</param>
        /// <param name="flags">The flags used for decoding the payload.</param>
        /// <param name="opcode"></param>
        /// <returns></returns>
        /// <exception cref="System.ArgumentException"></exception>
        public virtual T Decode<T>(byte[] buffer, int offset, int length, Flags flags, OperationCode opcode)
        {
            object value = default(T);
            switch (flags.DataFormat)
            {
                case DataFormat.Reserved:
                case DataFormat.Private:
                    if (typeof (T) == typeof (byte[]))
                    {
                        value = DecodeBinary(buffer, offset, length);
                    }
                    else
                    {
                        value = Decode<T>(buffer, offset, length, opcode);
                    }
                    break;

                case DataFormat.Json:
                    if (typeof (T) == typeof (string))
                    {
                        value = DecodeString(buffer, offset, length);
                    }
                    else
                    {
                        value = DeserializeAsJson<T>(buffer, offset, length);
                    }
                    break;

                case DataFormat.Binary:
                    if (typeof(T) == typeof(byte[]))
                    {
                        value = DecodeBinary(buffer, offset, length);
                    }
                    else
                    {
                        var msg = string.Format("The value of T does not match the DataFormat provided: {0}",
                            flags.DataFormat);
                        throw new ArgumentException(msg);
                    }
                    break;

                case DataFormat.String:
                    if (typeof(T) == typeof(char))
                    {
                        value = DecodeChar(buffer, offset, length);
                    }
                    else
                    {
                        value = DecodeString(buffer, offset, length);
                    }
                    break;

                default:
                    value = DecodeString(buffer, offset, length);
                    break;
            }
            return (T)value;
        }

        /// <summary>
        /// Decodes the specified buffer.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="buffer">The buffer.</param>
        /// <param name="offset">The offset.</param>
        /// <param name="length">The length.</param>
        /// <returns></returns>
        /// <exception cref="System.ArgumentOutOfRangeException"></exception>
        public virtual T Decode<T>(byte[] buffer, int offset, int length, OperationCode opcode)
        {
            object value = default(T);

            var typeCode = Type.GetTypeCode(typeof(T));
            switch (typeCode)
            {
                case TypeCode.Empty:
#if NET452
                case TypeCode.DBNull:
#endif
                case TypeCode.String:
                    value = DecodeString(buffer, offset, length);
                    break;

                case TypeCode.Char:
                    value = DecodeChar(buffer, offset, length);
                    break;

                case TypeCode.Int16:
                    if (length > 0)
                    {
                        value = Converter.ToInt16(buffer, offset, false);
                    }
                    break;

                case TypeCode.UInt16:
                    if (length > 0)
                    {
                        value = Converter.ToUInt16(buffer, offset, false);
                    }
                    break;

                case TypeCode.Int32:
                    if (length > 0)
                    {
                        value = Converter.ToInt32(buffer, offset, false);
                    }
                    break;

                case TypeCode.UInt32:
                    if (length > 0)
                    {
                        value = Converter.ToUInt32(buffer, offset, false);
                    }
                    break;

                case TypeCode.Int64:
                    if (length > 0)
                    {
                        value = Converter.ToInt64(buffer, offset, false);
                    }
                    break;

                case TypeCode.UInt64:
                    if (length > 0)
                    {
                        if (opcode == OperationCode.Increment || opcode == OperationCode.Decrement)
                        {
                            value = Converter.ToUInt64(buffer, offset, true);
                        }
                        else
                        {
                            value = Converter.ToUInt64(buffer, offset, false);
                        }
                    }
                    break;

                case TypeCode.Single:
                    break;

                case TypeCode.Double:
                    break;

                case TypeCode.Decimal:
                    break;

                case TypeCode.DateTime:
                    break;

                case TypeCode.Boolean:
                    break;

                case TypeCode.SByte:
                    break;

                case TypeCode.Byte:
                    break;

                case TypeCode.Object:
                    value = DeserializeAsJson<T>(buffer, offset, length);
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
            return (T)value;
        }

        /// <summary>
        /// Deserializes as json.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="buffer">The buffer.</param>
        /// <param name="offset">The offset.</param>
        /// <param name="length">The length.</param>
        /// <returns></returns>
        public virtual T DeserializeAsJson<T>(byte[] buffer, int offset, int length)
        {
            return Serializer.Deserialize<T>(buffer, offset, length);
        }

        /// <summary>
        /// Decodes the specified buffer.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="buffer">The buffer representing the value of the key to decode.</param>
        /// <param name="offset">The offset to start reading at.</param>
        /// <param name="length">The length to read from the buffer.</param>
        /// <param name="flags">The flags used to encode the payload.</param>
        /// <param name="opcode"></param>
        /// <returns></returns>
        public virtual T Decode<T>(ArraySegment<byte> buffer, int offset, int length, Flags flags, OperationCode opcode)
        {
            return Decode<T>(buffer.Array, offset, length, flags, opcode);
        }

        /// <summary>
        /// Serializes as json.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns></returns>
        public virtual byte[] SerializeAsJson(object value)
        {
            return Serializer.Serialize(value);
        }

        /// <summary>
        /// Decodes the specified buffer as string.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="offset">The offset.</param>
        /// <param name="length">The length.</param>
        /// <returns></returns>
        protected string DecodeString(byte[] buffer, int offset, int length)
        {
            string result = null;
            if (buffer != null && buffer.Length > 0 && length > 0)
            {
                result = Encoding.UTF8.GetString(buffer, offset, length);
            }
            return result;
        }

        /// <summary>
        /// Decodes the specified buffer as char.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="offset">The offset.</param>
        /// <param name="length">The length.</param>
        /// <returns></returns>
        protected char DecodeChar(byte[] buffer, int offset, int length)
        {
            char result = default(char);
            if (buffer != null && buffer.Length > 0 && length > 0)
            {
                var str = Encoding.UTF8.GetString(buffer, offset, length);
                if (str.Length == 1)
                {
                    result = str[0];
                }
                else if (str.Length > 1)
                {
                    var msg = string.Format("Can not convert string \"{0}\" to char", str);
                    throw new InvalidCastException(msg);
                }
            }
            return result;
        }

        /// <summary>
        /// Decodes the binary.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="offset">The offset.</param>
        /// <param name="length">The length.</param>
        /// <returns></returns>
        protected byte[] DecodeBinary(byte[] buffer, int offset, int length)
        {
            var temp = new byte[length];
            Buffer.BlockCopy(buffer, offset, temp, 0, length);
            return temp;
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
