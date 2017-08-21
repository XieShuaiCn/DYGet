using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading;

namespace DouyuGet
{
    public class DouyuClient
    {
        const int keepliveInterval = 40;
        private Socket dyClient;
        private string roomid;
        private IPEndPoint ipEndPoint;
        // 接收数据的Buffer
        private byte[] receiveBuffer = new byte[10240];
        // 心跳
        private Random random = new Random();
        private bool hasKeeylive = false;


        public DouyuClient(string roomid, string host, int port)
        {
            this.roomid = roomid;

            // HOST TO IP 
            IPHostEntry hostinfo = Dns.GetHostEntry(host);    // SocketException
            IPAddress[] parseIPs = hostinfo.AddressList;
            string ipStr = parseIPs[0].ToString();
            ipEndPoint = new IPEndPoint(IPAddress.Parse(ipStr), port);
        }

        public DouyuClient(string roomid, string host) : this(roomid, host, 8601)
        {
        }

        public DouyuClient(string roomid) : this(roomid, "openbarrage.douyutv.com", 8601)
        {
        }

        // 清空接收消息的buffer
        private byte[] resetReceiveBuffer()
        {
            Array.Clear(receiveBuffer, 0, receiveBuffer.Length);
            return receiveBuffer;
        }
            
        // 格式化发送内容（斗鱼协议）
        private byte[] formatMsg(string msg)
        {
            byte[] contentBytes = Encoding.UTF8.GetBytes(msg);       // 消息正文
            byte[] sendBytes = new byte[contentBytes.Length + 13];   // 发送的消息 
            // https://github.com/zephyrzoom/douyu/blob/master/nodejs/app.js
            // 4字节总消息长度 + 4字节总消息长度 + 689(2个字节,int低位) + '0' + '0' + 消息 + \0
            int messageLength = contentBytes.Length + 9;             // 不是很明白为什么+9？
            byte[] messageLengthBytes = BitConverter.GetBytes(messageLength);
            Buffer.BlockCopy(messageLengthBytes, 0, sendBytes, 0, messageLengthBytes.Length);   // 消息长度1
            Buffer.BlockCopy(messageLengthBytes, 0, sendBytes, 4, messageLengthBytes.Length);   // 消息长度2
            byte[] typeBytes = BitConverter.GetBytes(689);
            Buffer.BlockCopy(typeBytes, 0, sendBytes, 8, 2);                                    // 消息类型 689客户端到服务器, 690服务器到客户端?
            // 2个0
            byte[] emptyBytes = new byte[]{0, 0};
            Buffer.BlockCopy(emptyBytes, 0, sendBytes, 10, emptyBytes.Length);
            // 正文消息
            Buffer.BlockCopy(contentBytes, 0, sendBytes, 12, contentBytes.Length);
            // 消息结尾
            sendBytes[sendBytes.Length - 1] = 0x0000;
            return sendBytes;
        }

        // DEBUG输出
        private void debugOutput(string content, params object[] args)
        {
            #if DEBUG
            Console.WriteLine(content, args);
            #endif
        }
            
        // 开始
        public void Start()
        {
            if (dyClient == null)
            {
                // Socket Client
                dyClient = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);  
                dyClient.BeginConnect(ipEndPoint, new AsyncCallback(ConnectCallback), dyClient);
            }
        }

        // 退出
        public void Exit()
        {
            if (dyClient != null && dyClient.Connected)
            {
                dyClient.Close();
                dyClient = null;
            }
        }
 
        // 登录消息
        private byte[] loginContent()
        {
            string str = String.Format("type@=loginreq/roomid@={0}/", roomid);
            return formatMsg(str);
        }

        // 进入指定分组消息
        private byte[] enterGroupContent(int groupid)
        {
            string str = String.Format("type@=joingroup/rid@={0}/gid@={1}/", roomid, groupid);
            return formatMsg(str);
        }

        // 进入海量弹幕分组消息
        private byte[] enterGroupContent()
        {
            return enterGroupContent(-9999);
        }

        // 心跳消息
        private byte[] keepliveContent()
        {
            string str = String.Format("type@=keeplive/tick@={0}/", random.Next(Int32.MaxValue));
            return formatMsg(str);
        }

        // 接收消息事件处理
        private void onReceiveEvent()
        {
            byte[] buffer = resetReceiveBuffer();
            // 接收消息
            dyClient.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, new AsyncCallback(ReceiveCallback), dyClient);
        }

        // 发送信息指令
        private void regSendEvent(byte[] content)
        {
            dyClient.BeginSend(content, 0, content.Length, SocketFlags.None, new AsyncCallback(SendCallback), dyClient);
        }

        // 心跳
        private void doKeeplive()
        {
            if (!hasKeeylive)
            {
                Thread thread = new Thread(
                    new ThreadStart(() =>
                    {
                        while (dyClient != null && dyClient.Connected)
                        {
                            Thread.Sleep(keepliveInterval * 1000);
                            regSendEvent(keepliveContent());
                        }
                    })
                );
                thread.IsBackground = true;
                thread.Start();
                hasKeeylive = true;
            }
        }

        // 登录回调
        private void ConnectCallback(IAsyncResult ar)
        {
            debugOutput("正在登录 {0} ...", roomid);
            Socket client = (Socket)ar.AsyncState;
            client.EndConnect(ar);

            // 登录
            regSendEvent(loginContent());

            // 注册接受消息
            onReceiveEvent();

            // 心跳
            doKeeplive();
        }

        // 发送回调
        private void SendCallback(IAsyncResult ar)
        {
            Socket client = (Socket)ar.AsyncState;
            if (client.Connected)
            {
                int sendLength = client.EndSend(ar);
                debugOutput("发送消息 {0} ...", sendLength);
            }
        }

        // 接收消息回调
        private void ReceiveCallback(IAsyncResult ar)
        {
            debugOutput("数据接收中……");

            Socket client = (Socket)ar.AsyncState;
            if (client.Connected)
            {
                int sendLength = client.EndReceive(ar);
                Dictionary<string, string> message = parseMessageContent(receiveBuffer, sendLength);

                if (message != null && message.ContainsKey("type"))
                {
                    switch (message["type"])
                    {
                        case "loginres":
                            debugOutput("登录完成 {0}/{1}/{2}/{3}", message["userid"], message["sessionid"], message["username"], message["nickname"]);
                        // 登录成功后进入分组
                            regSendEvent(enterGroupContent());
                            break;
                        case "qausrespond":
                            debugOutput("进入分组完成");
                            break;
                        case "chatmsg":
                            Console.WriteLine("{0}({1}) : {2}", message["nn"], message["level"], message["txt"]);
                            break;
                        case "keeplive":
                            debugOutput("完成心跳");
                            break;
                    }
                }

                // 接收下一次消息
                onReceiveEvent();
            }
        }

        // STT协议反序列化 (抄的)
        private Dictionary<string, string> parseMessageContent(byte[] contentBuffer, int contentLength)
        {
            string str = String.Empty;
            Dictionary<string, string> data = null;
            if (contentLength > 19)                                             // 13个字节 + 必要消息type@=
            {
                debugOutput("接收字节长度 {0}", contentLength);
                byte[] messageBuffer = new byte[contentLength - 12 - 1];        // 12个字节消息头 1个字节\0
                Buffer.BlockCopy(contentBuffer, 12, messageBuffer, 0, messageBuffer.Length);
                str = Encoding.UTF8.GetString(messageBuffer);
                debugOutput("解析消息: {0}", str);
            }
            if (str != String.Empty)
            {
                data = new Dictionary<string, string>();
                string key = "";
                string val = "";

                if (str.Substring(str.Length - 1, 1) != "/")
                {
                    str += "/";
                }
                for (int i = 0; i < str.Length; i++)
                {
                    if (str.Substring(i, 1) == "/")
                    {

                        if (!data.ContainsKey(key))
                        {
                            data.Add(key, val);

                        }
                        else
                        {
                            data[key] = val;
                        }

                        if (val.Contains("@S/"))
                        {
                            /** 
                            string[] arr = Regex.Split(val, "js", RegexOptions.IgnoreCase);
                            foreach (string ss in arr) {
                            } 
                            */
                        }
                        key = val = "";

                    }
                    else
                    {
                        if (str.Substring(i, 1) == "@")
                        {
                            i++;
                            if (str.Substring(i, 1) == "A")
                            {
                                val += "@";
                            }
                            else
                            {
                                if (str.Substring(i, 1) == "S")
                                {
                                    val += "/";
                                }
                                else
                                {
                                    if (str.Substring(i, 1) == "=")
                                    {
                                        key = val;
                                        val = "";
                                    }
                                }
                            }
                        }
                        else
                        {
                            val += str.Substring(i, 1);
                        }
                    }
                }
            }
            return data;
        }

    }
}

