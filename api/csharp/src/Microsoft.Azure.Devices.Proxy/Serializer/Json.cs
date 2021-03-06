﻿// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Microsoft.Azure.Devices.Proxy.Model {
    using System;
    using Newtonsoft.Json.Linq;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;
    using System.Reflection;
    using System.Runtime.Serialization;

    /// <summary>
    /// Resolver implementation for the Proxy object model.
    /// </summary>
    public class CustomResolver : DefaultContractResolver {
        public static readonly CustomResolver Instance = new CustomResolver();

        protected override JsonProperty CreateProperty(MemberInfo member, 
            MemberSerialization memberSerialization) {
            JsonProperty property = base.CreateProperty(member, memberSerialization);

            var dataMember = member.GetCustomAttribute<DataMemberAttribute>();
            property.ShouldSerialize = (i) => {
                return dataMember != null;
            };
            property.PropertyName = dataMember != null ? dataMember.Name : "";
            return property;
        }

        protected override JsonContract CreateContract(Type objectType) {
            JsonContract contract = base.CreateContract(objectType);
            /**/ if (typeof(Reference).IsAssignableFrom(objectType)) {
                contract.Converter = new AddressConverter();
            }
            else if (typeof(SocketAddress).IsAssignableFrom(objectType)) {
                contract.Converter = new SocketAddressConverter();
            }
            else if (typeof(Message).IsAssignableFrom(objectType)) {
                contract.Converter = new MessageConverter();
            }
            else if (typeof(VoidMessage).IsAssignableFrom(objectType)) {
                contract.Converter = new VoidConverter();
            }
            else if (typeof(IMessageContent).IsAssignableFrom(objectType)) {
                contract.Converter = new MessageContentConverter();
            }
            return contract;
        }

        class MessageReferencePlaceHolder : IMessageContent {
            public Message Ref { get; set; }  
        }

        class MessageConverter : JsonConverter {
            public override bool CanConvert(Type objectType) {
                return false;
            }
            public override object ReadJson(JsonReader reader, Type objectType, 
                object existingValue, JsonSerializer serializer) {
                Message message = new Message();
                // Hand this message to the content converter as existing object
                message.Content = new MessageReferencePlaceHolder { Ref = message };
                serializer.Populate(reader, message);
                return message;
            }

            public override void WriteJson(JsonWriter writer, object value,
                JsonSerializer serializer) {
                throw new NotImplementedException();
            }
            public override bool CanWrite {
                get { return false; }
            }
        }

        class MessageContentConverter : JsonConverter {
            public override bool CanConvert(Type objectType) {
                return false;
            }
            private object Deserialize(Type t, JsonReader reader, JsonSerializer serializer) {
                var message = Activator.CreateInstance(t);
                if (reader.TokenType != JsonToken.Null)
                    serializer.Populate(reader, message);
                return message;
            }
            public override object ReadJson(JsonReader reader, Type objectType,
                object existingValue, JsonSerializer serializer) {
                Message reference = ((MessageReferencePlaceHolder)existingValue).Ref;
                return Deserialize(MessageContent.TypeOf(reference.TypeId,
                    reference.IsResponse), reader, serializer);
            }
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
                throw new NotImplementedException();
            }
            public override bool CanWrite {
                get { return false; }
            }
        }

        class VoidConverter : MessageContentConverter {
            public override void WriteJson(JsonWriter writer, 
                object value, JsonSerializer serializer) {
                writer.WriteNull();
            }
            public override bool CanWrite {
                get { return true; }
            }
        }

        class AddressConverter : JsonConverter {
            public override bool CanConvert(Type objectType) {
                return false;
            }
            public override object ReadJson(JsonReader reader, Type objectType,
                object existingValue, JsonSerializer serializer) {
                Reference address = null;
                if (reader.TokenType == JsonToken.StartObject) {
                    do {
                        reader.Read();
                        if (reader.TokenType == JsonToken.PropertyName &&
                            reader.Value.ToString().Equals("id",
                                StringComparison.CurrentCultureIgnoreCase)) {
                            reader.Read();
                            if (reader.TokenType == JsonToken.String)
                                address = Reference.Parse(reader.Value.ToString());
                            else
                                throw new InvalidDataContractException();
                        }
                    } while (reader.TokenType != JsonToken.EndObject);
                }
                return address;
            }

            public override void WriteJson(JsonWriter writer, object value, 
                JsonSerializer serializer) {
                writer.WriteStartObject();
                writer.WritePropertyName("id");
                if (value == null) {
                    writer.WriteNull();
                }
                else {
                    writer.WriteValue(value.ToString());
                }
                writer.WriteEndObject();
            }
        }

        class SocketAddressConverter : JsonConverter {
            public override bool CanConvert(Type objectType) {
                return false;
            }

            public override object ReadJson(JsonReader reader, Type objectType, 
                object existingValue, JsonSerializer serializer) {
                JObject jsonObject = JObject.Load(reader);
                AddressFamily family = (AddressFamily)jsonObject.Value<int>("family");
                switch(family) {
                    case AddressFamily.Unspecified:
                        return new NullSocketAddress();
                    case AddressFamily.Unix:
                        return jsonObject.ToObject<UnixSocketAddress>();
                    case AddressFamily.Proxy:
                        return jsonObject.ToObject<ProxySocketAddress>();
                    case AddressFamily.InterNetwork:
                        return jsonObject.ToObject<Inet4SocketAddress>();
                    case AddressFamily.InterNetworkV6:
                        return jsonObject.ToObject<Inet6SocketAddress>();
                    default:
                        throw new SerializationException($"Bad socket address family {family}");
                }
            }
            public override void WriteJson(JsonWriter writer, object value, 
                JsonSerializer serializer) {
                throw new NotImplementedException();
            }
            public override bool CanWrite {
                get { return false; }
            }
        }
    }
}
