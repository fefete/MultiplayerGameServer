using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Threading;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Data.SQLite;
using System.Collections.Concurrent;
using System.Globalization;

namespace csharserver
{

    public struct player
    {
        public string ID;   
        public string name;
        public bool jump;
        public bool left;
        public bool right;
        public bool dash;
        public float x;
        public float y;

    }

    public class UdpState
    {
        public IPEndPoint e;
        public UdpClient u;
        public int id;
    }


    public class stateObject
    {
        //client
        public Socket work_socket = null;
        //size of the buffer 
        public const int buf_size = 1024;
        //recieve buffer
        public byte[] buffer = new byte[buf_size];
        //recieved data string
        public StringBuilder string_builder = new StringBuilder();

    }


    class AsyncSocket
    {
        public static ManualResetEvent allDone = new ManualResetEvent(false);
        public static bool messageReceived = false;
        public static bool messageSent = false;
        public const int listenPort = 53035;
        public static ConcurrentDictionary<Socket, player> clients;
        public static SQLiteConnection dbConnection;
        public AsyncSocket()
        {

        }
        public static void init()
        {

            int numProcs = Environment.ProcessorCount;
            int concurrencyLevel = numProcs * 2;
            clients = new ConcurrentDictionary<Socket, player>(concurrencyLevel, numProcs);
            dbConnection = new SQLiteConnection("Data Source=MyDatabase.sqlite;Version=3;");
            dbConnection.Open();
            string sql = "CREATE TABLE IF NOT EXISTS players (ID INTEGER PRIMARY KEY AUTOINCREMENT, NAME varchar(20), PASSWORD varchar(20), HIGHSCORE INT)";
            SQLiteCommand com = new SQLiteCommand(sql, dbConnection);
            com.ExecuteNonQuery();
            dbConnection.Close();
        }

        public static void startListening()
        {
            //data buffer for incomming data
            //byte[] bytes = new byte[1024];

            // Establish the local endpoint for the socket.
            // The DNS name of the computer
            // running the listener is localhost.
            IPHostEntry ip_host_info = Dns.Resolve("localhost");
            IPAddress ip_address = ip_host_info.AddressList[0];
            IPAddress multicast = IPAddress.Parse("224.0.1.1");
            IPEndPoint localEndPoint = new IPEndPoint(ip_address, 53000);
            IPEndPoint UDP_localEndPoint = new IPEndPoint(ip_address, 53035);
            //IPEndPoint UDP_localEndPoint_multicast = new IPEndPoint(multicast, 53035);
            // Create a TCP/IP socket.
            Socket listener = new Socket(AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);
    

            UdpClient c = new UdpClient(listenPort);
            c.EnableBroadcast = true;
            UdpState state = new UdpState();

            state.e = UDP_localEndPoint;
            state.u = c;
            
            c.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            //c.JoinMulticastGroup(multicast);
            // Bind the socket to the local endpoint and listen for incoming connections.
            try
            {
                listener.Bind(localEndPoint);
                listener.Listen(100);
                c.BeginReceive(new AsyncCallback(recieveCallbackUDP), state);
                while (true)
                {
                    //set the event to nonsignaled state
                    allDone.Reset();
                    Console.WriteLine("Waiting for a connection...");

                    // Start an asynchronous socket to listen for connections.
                    listener.BeginAccept(
                        new AsyncCallback(AcceptCallbackTCP),
                        listener);
                    // Wait until a connection is made before continuing.
                    allDone.WaitOne();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            Console.WriteLine("\nPress ENTER to continue...");
            Console.Read();
        }

        public static void startListeningUDP()
        {
            while (true) {
                ReceiveMessagesUDP();
            }
        }

        public static void AcceptCallbackTCP(IAsyncResult ar)
        {
            // Signal the main thread to continue.
            allDone.Set();

            // Get the socket that handles the client request.
            Socket listener = (Socket)ar.AsyncState;
            Socket handler = listener.EndAccept(ar);

            // Create the state object.
            stateObject state = new stateObject();
            state.work_socket = handler;
            handler.BeginReceive(state.buffer, 0, stateObject.buf_size, 0,
                new AsyncCallback(ReadCallbackTCP), state);
        }

        public static void ReadCallbackTCP(IAsyncResult ar)
        {
            String content = String.Empty;

            // Retrieve the state object and the handler socket
            // from the asynchronous state object.
            stateObject state = (stateObject)ar.AsyncState;
            Socket handler = state.work_socket;

            // Read data from the client socket. 
            int bytesRead = handler.EndReceive(ar);
            if (bytesRead > 0)
            {
                content = Encoding.ASCII.GetString(state.buffer, 0, bytesRead);

                if (content.IndexOf("\0") > -1)
                {
                    //SendTCP(handler, content);
                    char[] delimiters = { ':' };
                    String[] parts = content.Split(delimiters);
                    Console.WriteLine("{0}", content);

                    if (parts[0] == "reg")
                    {
                        String sql = "SELECT COUNT(*) FROM players WHERE NAME LIKE @name";
                        dbConnection.Open();
                        SQLiteCommand com = new SQLiteCommand(sql, dbConnection);
                        com.Parameters.AddWithValue("@name", parts[1]);
                        int err = Convert.ToInt32(com.ExecuteScalar());
                        dbConnection.Close();

                        if (err > 0)
                        {
                            //return error
                            SendTCP(handler, "reg:FAILURE:");

                        }
                        else
                        {
                            String sql_add = "INSERT INTO players (NAME, PASSWORD, HIGHSCORE) VALUES (@username, @password, 0)";
                            dbConnection.Open();
                            SQLiteCommand command = new SQLiteCommand(sql_add, dbConnection);
                            command.Parameters.AddWithValue("@username", parts[1]);
                            command.Parameters.AddWithValue("@password", parts[2]);
                            command.ExecuteNonQuery();
                            dbConnection.Close();
                            SendTCP(handler, "reg:OK:");
                        }
                    }
                    if (parts[0] == "login")
                    {
                        String sql = "SELECT COUNT(*) FROM players WHERE NAME LIKE @username AND PASSWORD LIKE @password";
                        dbConnection.Open();
                        SQLiteCommand command = new SQLiteCommand(sql, dbConnection);
                        command.Parameters.AddWithValue("@username", parts[1]);
                        command.Parameters.AddWithValue("@password", parts[2]);
                        int exists = Convert.ToInt32(command.ExecuteScalar());
                        dbConnection.Close();
                        if (exists > 0)
                        {
                            player p = new player();
                            p.name = parts[1];
                            SendTCP(handler, "login:OK:"+parts[1]+":");
                            foreach(KeyValuePair<Socket, player> entry in clients)
                            {
                                SendTCP(entry.Key, "game:newPlayer:"+p.name+":" + 400/30 + ":" + 300/30 + ":");
                                SendTCP(handler, "game:newPlayer:" + entry.Value.name + ":" + entry.Value.x + ":" + entry.Value.y +":");
                            }
                            clients[handler] = p;

                        }
                    }
                    if (parts[0] == "remove")
                    {
                        String sql = "SELECT COUNT(*) FROM players WHERE NAME LIKE @name";
                        dbConnection.Open();
                        SQLiteCommand com = new SQLiteCommand(sql, dbConnection);
                        com.Parameters.AddWithValue("@name", parts[1]);
                        int err = Convert.ToInt32(com.ExecuteScalar());
                        dbConnection.Close();
                        if (err > 0)
                        {
                            //return error
                            SendTCP(handler, "remove:FAILURE:");

                        }
                        else
                        {
                            String sql_add = "DELETE FROM players WHERE name = @username AND passworld = @password";
                            dbConnection.Open();
                            SQLiteCommand command = new SQLiteCommand(sql_add, dbConnection);
                            command.Parameters.AddWithValue("@username", parts[1]);
                            command.Parameters.AddWithValue("@password", parts[2]);
                            command.ExecuteNonQuery();
                            dbConnection.Close();
                            SendTCP(handler, "remove:OK:");
                        }
                    }
                }
                else
                {
                    // Not all data received. Get more.
                    handler.BeginReceive(state.buffer, 0, stateObject.buf_size, 0,
                    new AsyncCallback(ReadCallbackTCP), state);
                }
            }
        }

        private static void SendTCP(Socket handler, String data)
        {
            // Convert the string data to byte data using ASCII encoding.
            byte[] byteData = Encoding.ASCII.GetBytes(data);

            // Begin sending the data to the remote device.
            handler.BeginSend(byteData, 0, byteData.Length, 0,
                new AsyncCallback(SendCallbackTCP), handler);
        }

        private static void SendCallbackTCP(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                Socket handler = (Socket)ar.AsyncState;

                // Complete sending the data to the remote device.
                int bytesSent = handler.EndSend(ar);
                Console.WriteLine("Sent {0} bytes to client.", bytesSent);

                //handler.Shutdown(SocketShutdown.Both);
                //handler.Close();
                stateObject state = new stateObject();
                state.work_socket = handler;
                handler.BeginReceive(state.buffer, 0, stateObject.buf_size, 0,
                    new AsyncCallback(ReadCallbackTCP), state);

            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }


        //UDP
        private static void recieveCallbackUDP(IAsyncResult ar)
        {
            //while (messageReceived == false) { }
            UdpClient u = (UdpClient)((UdpState)(ar.AsyncState)).u;
            IPEndPoint e = (IPEndPoint)((UdpState)(ar.AsyncState)).e;

            Byte[] receiveBytes = u.EndReceive(ar, ref e);
            string receiveString = Encoding.ASCII.GetString(receiveBytes);
            Console.WriteLine("Received: {0}", receiveString);
            char[] delimiters = { ':' };
            String[] parts = receiveString.Split(delimiters);
            // The message then needs to be handled
            messageReceived = true;
            string sendString = "update:";
            foreach (KeyValuePair<Socket, player> entry in clients)
            {
                if(entry.Value.name == parts[0])
                {
                    player p = entry.Value;
                    p.right = Convert.ToBoolean(parts[1]);
                    p.left = Convert.ToBoolean(parts[2]);
                    p.jump = Convert.ToBoolean(parts[3]);
                    p.dash = Convert.ToBoolean(parts[4]);
                    p.x = float.Parse(parts[5], CultureInfo.InvariantCulture);
                    p.y = float.Parse(parts[6], CultureInfo.InvariantCulture);
                    clients[entry.Key] = p;
                }
                else{
                    sendString = sendString + entry.Value.name + ":" + entry.Value.right + ":" + entry.Value.left + ":" + entry.Value.jump + ":" + entry.Value.dash + ":" + entry.Value.x + ":" + entry.Value.y + ":";
                }
            }
            SendMessageUDP(e, sendString, u);
            //ReceiveMessagesUDP();
            u.BeginReceive(new AsyncCallback(recieveCallbackUDP), (UdpState)(ar.AsyncState));
            
        }

        public static void ReceiveMessagesUDP()
        {
            // Receive a message and write it to the console.
            IPEndPoint e = new IPEndPoint(IPAddress.Any, listenPort);
            UdpClient u = new UdpClient(e);
            UdpState s = new UdpState();
            s.e = e;
            s.u = u;
            //Console.WriteLine("listening for messages");
            u.BeginReceive(new AsyncCallback(recieveCallbackUDP), s);
            // Do some work while we wait for a message.
            while (!messageReceived)
            {
                // Do something
            }

        }

        public static void SendCallbackUDP(IAsyncResult ar)
        {
            UdpClient u = (UdpClient)ar.AsyncState;
            u.EndSend(ar);
            messageSent = true;
        }

        static void SendMessageUDP(IPEndPoint server, string message, UdpClient u)
        {
            // create the udp socket
            //UdpClient u = new UdpClient();
            u.Connect(server.Address, server.Port);
            Byte[] sendBytes = Encoding.ASCII.GetBytes(message);
            Console.WriteLine("SENT: {0}", message);
            // send the message
            // the destination is defined by the call to .Connect()
            u.BeginSend(sendBytes, sendBytes.Length, new AsyncCallback(SendCallbackUDP), u);
            while (!messageSent)
            {
                //dosomething
            }
        }

        public static void broadcasting()
        {

        }

        public static int Main(String[] args)
        {
            init();
            SQLiteConnection.CreateFile("ClientsDB.sqlite");
            startListening();
            return 0;
        }
    }
}
