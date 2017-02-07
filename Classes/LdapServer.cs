﻿using log4net;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Flexinets.Ldap
{
    public class LdapServer
    {
        private readonly ILog _log = LogManager.GetLogger(typeof(LdapServer));
        private readonly TcpListener _server;

        public Boolean Running
        {
            get;
            private set;
        }


        /// <summary>
        /// Create a new server on endpoint
        /// </summary>
        /// <param name="serverEndpoint"></param>
        public LdapServer(IPEndPoint serverEndpoint)
        {
            _server = new TcpListener(serverEndpoint);
        }


        /// <summary>
        /// Start listening for requests
        /// </summary>
        public void Start()
        {
            _server.Start();
            _server.BeginAcceptTcpClient(ReceiveCallback, null);
            Running = true;
        }


        /// <summary>
        /// Stop listening
        /// </summary>
        public void Stop()
        {
            _server.Stop();
            Running = false;
        }


        /// <summary>
        /// Receive packets
        /// </summary>
        /// <param name="ar"></param>
        private void ReceiveCallback(IAsyncResult ar)
        {
            if (Running)
            {
                var client = _server.EndAcceptTcpClient(ar);
                _server.BeginAcceptTcpClient(ReceiveCallback, null);

                _log.Debug($"Connection from {client.Client.RemoteEndPoint}");

                try
                {
                    var stream = client.GetStream();

                    int i = 0;
                    while (true)
                    {
                        var bytes = new Byte[1024];
                        i = stream.Read(bytes, 0, bytes.Length);
                        if (i == 0)
                        {
                            break;
                        }

                        var data = Encoding.UTF8.GetString(bytes, 0, i);
                        _log.Debug($"Received {i} bytes: {data}");
                        _log.Debug(Utils.ByteArrayToString(bytes));
                        ParseLdapPacket(bytes);

                        if (data.Contains("cn=bindUser,cn=Users,dc=dev,dc=company,dc=com"))
                        {
                            var bindresponse = Utils.StringToByteArray("300c02010161070a010004000400"); // bind success...
                            stream.Write(bindresponse, 0, bindresponse.Length);
                        }
                        if (data.Contains("sAMAccountName"))
                        {
                            var searchresponse = Utils.StringToByteArray("300c02010265070a012004000400");   // object not found
                            _log.Debug(Utils.BitsToString(new System.Collections.BitArray(searchresponse)));
                            stream.Write(searchresponse, 0, searchresponse.Length);
                        }
                    }

                    _log.Debug($"Connection closed to {client.Client.RemoteEndPoint}");
                    client.Close();
                }
                catch (IOException ioex)
                {
                    _log.Warn("oops", ioex);
                }
            }
        }


        /// <summary>
        /// Parse a raw ldap packet and return something more useful
        /// </summary>
        /// <param name="packetBytes">Buffer containing packet bytes</param>
        public void ParseLdapPacket(Byte[] packetBytes)
        {
            int packetLength = 0;
            int i = 0;

            while (i <= packetLength)
            {
                var tag = Tag.Parse(packetBytes[i]);
                i++;

                int position;
                var attributeLength = Utils.BerLengthToInt(packetBytes, i, out position);
                i += position;
            
                // The first length is the length of the packet, set and forget. The rest are attributes
                if (packetLength == 0)
                {
                    packetLength = attributeLength + 2;
                }


                if (tag.TagType == TagType.Application)
                {
                    _log.Debug($"Attribute length: {attributeLength}, Tagtype: {tag.TagType}, sequence {tag.IsSequence}, operation: {tag.LdapOperation}");
                }
                else if (tag.TagType == TagType.Context)
                {
                    _log.Debug($"Attribute length: {attributeLength}, Tagtype: {tag.TagType}, sequence {tag.IsSequence}, context specific ??? profit");
                }
                else
                {
                    _log.Debug($"Attribute length: {attributeLength}, TagType: {tag.TagType}, sequence {tag.IsSequence}, datatype: {tag.DataType}");
                }

                if (!tag.IsSequence && attributeLength > 0)
                {
                    if (tag.TagType == TagType.Universal)
                    {
                        if (tag.DataType == UniversalDataType.Boolean)
                        {
                            _log.Debug(BitConverter.ToBoolean(packetBytes, i));
                        }
                        else if (tag.DataType == UniversalDataType.Integer)
                        {
                            var intbytes = new Byte[4];
                            Buffer.BlockCopy(packetBytes, i, intbytes, 4 - attributeLength, attributeLength);
                            _log.Debug(BitConverter.ToUInt32(intbytes.Reverse().ToArray(), 0));
                        }
                        else
                        {
                            var data = Encoding.UTF8.GetString(packetBytes, i, attributeLength);
                            _log.Debug(data);
                        }
                    }
                    else
                    {
                        var data = Encoding.UTF8.GetString(packetBytes, i, attributeLength);
                        _log.Debug(data);
                    }

                    i += attributeLength;
                }
            }
        }
    }
}