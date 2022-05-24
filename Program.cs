using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Net.Http;
using System.IO;
using Newtonsoft.Json;

namespace MCScanner
{
    internal class MCServerConnection : IDisposable
    {
        internal class Buffer : IDisposable
        {
            private List<byte> buff;
            public int Size { get => buff.Count; }
            public List<byte> Data { get => buff; }

            public Buffer()
            {
                buff = new List<byte>();
            }

            public void Write(byte[] val) => buff.AddRange(val);

            public void Write(int val)
            {
                while ((val & 128) != 0)
                {
                    buff.Add((byte)(val & 127 | 128));
                    val = (int)((uint)val) >> 7;
                }

                buff.Add((byte)val);
            }

            public void Write(short val) => Write(BitConverter.GetBytes(val));

            public void Write(string val)
            {
                byte[] by = Encoding.UTF8.GetBytes(val);
                Write(by.Length);
                Write(by);
            }

            public byte[] ToArray() => buff.ToArray();

            public void Clear() => buff.Clear();

            public void Dispose()
            {
                buff.Clear();
                buff = null;
                GC.SuppressFinalize(this);
            }
        }

        internal class ByteReader : IDisposable
        {
            byte[] arr;
            private int offset;

            public ByteReader(byte[] arr)
            {
                this.arr = arr;
                offset = 0;
            }

            public byte ReadByte() => arr[offset++];
            public byte[] ReadBytes(int len)
            {
                byte[] buff = new byte[len];
                Array.Copy(arr, offset, buff, 0, len);

                offset += len;
                return buff;
            }

            public int ReadInt()
            {
                var value = 0;
                var size = 0;
                int b;

                while (((b = ReadByte()) & 0x80) == 0x80)
                    value |= (b & 0x7F) << (size++ * 7);

                return value | ((b & 0x7F) << (size * 7));
            }

            public string ReadString(int len) => Encoding.UTF8.GetString(ReadBytes(len));

            public string ReadString(int offset, int len) => Encoding.UTF8.GetString(ReadBytes(len), offset, len - offset);

            public void Dispose()
            {
                arr = null;
                GC.SuppressFinalize(this);
            }
        }

        internal class ServerInfo
        {
            internal class Version
            {
                public string name { get; set; }
                public int protocol { get; set; }
            }

            internal class  PlayerSample
            {
                public string name { get; set; }
                public string id { get; set; }
            }

            internal class Players
            {
                public int max { get; set; }
                public int online { get; set; }

                [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
                public PlayerSample[] sample { get; set; }
            }

            internal class Description
            {
                public string text { get; set; }
            }

            public Version version { get; set; }
            public Players players { get; set; }

            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public Description description { get; set; }
            public string favicon { get; set; }
        }

        private TcpClient client;
        private NetworkStream stream;
        private Buffer buffer;
        private string ip;
        private int port;

        public MCServerConnection(string ip, int port)
        {
            this.ip = ip;
            this.port = port;
            buffer = new Buffer();
            client = new TcpClient();
        }

        public ServerInfo Connect()
        {
            client.Connect(ip, port);

            if (!client.Connected)
                throw new Exception("Could not connect to server.");

            stream = client.GetStream();
            return performHandshake();
        }

        private ServerInfo performHandshake()
        {
            buffer.Write(47);
            buffer.Write(ip);
            buffer.Write((short)port);
            buffer.Write(1);

            sendBuffer();
            sendBuffer();

            byte[] buff = new byte[Int16.MaxValue];
            stream.Read(buff, 0, buff.Length);

            ByteReader reader = new ByteReader(buff);
            string json = reader.ReadString(0, reader.ReadInt());
            json = json.Substring(json.IndexOf('{'));

            buff = null;
            reader.Dispose();

            return JsonConvert.DeserializeObject<ServerInfo>(json);
        }

        private void sendBuffer()
        {
            byte[] buff = buffer.ToArray();
            buffer.Clear();

            buffer.Write(0);

            byte[] packetData = buffer.ToArray();
            buffer.Clear();

            buffer.Write(buff.Length + packetData.Length);
            byte[] bufferLength = buffer.ToArray();
            buffer.Clear();

            stream.Write(bufferLength, 0, bufferLength.Length);
            stream.Write(packetData, 0, packetData.Length);
            stream.Write(buff, 0, buff.Length);
        }

        public void Dispose()
        {
            stream.Close();
            stream.Dispose();
            buffer.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    internal class Program
    {
        static void Main()
        {  
            MCServerConnection mc = new MCServerConnection("localhost", 25565);
            MCServerConnection.ServerInfo info = mc.Connect();

            Console.WriteLine(JsonConvert.SerializeObject(info, Formatting.Indented));

            Console.ReadKey();
        }
    }
}
