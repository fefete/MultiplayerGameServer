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
    [StructLayout(LayoutKind.Explicit)]
    public struct player
    {
        [FieldOffset(0)]
        public int name;
        [FieldOffset(8)]
        public double x;
        [FieldOffset(16)]
        public double y;
        [FieldOffset(24)]
        public double v_x;
        [FieldOffset(32)]
        public double v_y;
        [FieldOffset(40)]
        public double dash_cd;
        [FieldOffset(48)]
        public bool has_ball;
        [FieldOffset(56)]
        public double high_score;
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
            //dbConnection.Open();
            string sql = "CREATE TABLE IF NOT EXISTS players (ID INTEGER PRIMARY KEY AUTOINCREMENT, NAME varchar(20), PASSWORD varchar(20), HIGHSCORE INT)";
            using (dbConnection = new SQLiteConnection("Data Source=MyDatabase.sqlite;Version=3;"))
            {
                dbConnection.Open();
                using (SQLiteCommand com = new SQLiteCommand(sql, dbConnection))
                {
                    com.ExecuteNonQuery();
                }
            }

            //dbConnection.Close();
        }

        public static void startListening()
        {
            //data buffer for incomming data
            //byte[] bytes = new byte[1024];

            // Establish the local endpoint for the socket.
            // The DNS name of the computer
            // running the listener is localhost.
            IPAddress[] localIPs = Dns.GetHostAddresses(Dns.GetHostName());
            IPAddress ip_address;
            ip_address = localIPs[0];
            for (int i = 0; i < localIPs.Length; i++)
            {
                switch (localIPs[i].AddressFamily)
                {
                    case System.Net.Sockets.AddressFamily.InterNetwork:
                        // we have IPv4
                        if (!IPAddress.IsLoopback(localIPs[i]))
                        {
                            ip_address = localIPs[i];
                            Console.WriteLine("FOUND AND SUITABLE IP");
                            i = 100;
                        }
                        break;
                    case System.Net.Sockets.AddressFamily.InterNetworkV6:
                        // we have IPv6
                        break;
                    default:
                        // umm... yeah... mmmm.... nope
                        break;
                }
            }
            IPEndPoint localEndPoint = new IPEndPoint(ip_address, 53000);
            IPEndPoint UDP_localEndPoint = new IPEndPoint(ip_address, 53035);
            Console.WriteLine("Listening at  {0}", ip_address.ToString());
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
            while (true)
            {
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
                        //dbConnection.Open();
                        int err = 0;
                        using (dbConnection = new SQLiteConnection("Data Source=MyDatabase.sqlite;Version=3;"))
                        {
                            dbConnection.Open();
                            using (SQLiteCommand com = new SQLiteCommand(sql, dbConnection))
                            {
                                com.Parameters.AddWithValue("@name", parts[1]);
                                err = Convert.ToInt32(com.ExecuteScalar());
                            }
                        }
                        //dbConnection.Close();

                        if (err > 0)
                        {
                            //return error
                            SendTCP(handler, "reg:FAILURE:");

                        }
                        else
                        {
                            String sql_add = "INSERT INTO players (NAME, PASSWORD, HIGHSCORE) VALUES (@username, @password, 0)";
                            //dbConnection.Open();
                            using (dbConnection = new SQLiteConnection("Data Source=MyDatabase.sqlite;Version=3;"))
                            {
                                dbConnection.Open();
                                using (SQLiteCommand command = new SQLiteCommand(sql_add, dbConnection))
                                {
                                    command.Parameters.AddWithValue("@username", parts[1]);
                                    command.Parameters.AddWithValue("@password", parts[2]);
                                    command.ExecuteNonQuery();
                                }
                            }
                            //dbConnection.Close();
                            SendTCP(handler, "reg:OK:");
                        }
                    }
                    if (parts[0] == "login")
                    {
                        String sql = "SELECT COUNT(*) FROM players WHERE NAME LIKE @username AND PASSWORD LIKE @password";
                        //dbConnection.Open();
                        int exists = 0;
                        using (dbConnection = new SQLiteConnection("Data Source=MyDatabase.sqlite;Version=3;"))
                        {
                            dbConnection.Open();
                            using (SQLiteCommand command = new SQLiteCommand(sql, dbConnection))
                            {
                                command.Parameters.AddWithValue("@username", parts[1]);
                                command.Parameters.AddWithValue("@password", parts[2]);
                                exists = Convert.ToInt32(command.ExecuteScalar());
                            }
                        }
                        //dbConnection.Close();
                        if (exists > 0)
                        {
                            player p = new player();
                            String id;
                            String maxScore;
                            //dbConnection.Open();
                            sql = "SELECT * FROM players WHERE NAME LIKE " + parts[1] + " AND PASSWORD LIKE " + parts[2];
                            using (dbConnection = new SQLiteConnection("Data Source=MyDatabase.sqlite;Version=3;"))
                            {
                                dbConnection.Open();
                                using (SQLiteCommand com = new SQLiteCommand(sql, dbConnection))
                                {
                                    using (SQLiteDataReader reader = com.ExecuteReader())
                                    {
                                        reader.Read();
                                        Console.WriteLine("Name: " + reader["ID"] + "\tScore: " + reader["HIGHSCORE"]);
                                        id = Convert.ToString(reader["ID"]);
                                        maxScore = Convert.ToString(reader["HIGHSCORE"]);
                                    }
                                }
                            }


                            //dbConnection.Close();
                            SendTCP(handler, "login:OK:" + id + ":" + maxScore +":");
                            p.name = Convert.ToInt32(id);
                            p.high_score = Convert.ToDouble(maxScore);
                            foreach (KeyValuePair<Socket, player> entry in clients)
                            {
                                SendTCP(entry.Key, "game:newPlayer:" + p.name + ":" + 400 / 30 + ":" + 300 / 30 + ":");
                                Console.WriteLine("Sent player to old client");
                            }
                            foreach (KeyValuePair<Socket, player> entry in clients)
                            {
                                SendTCP(handler, "game:newPlayer:" + entry.Value.name + ":" + entry.Value.x + ":" + entry.Value.y + ":");
                                Console.WriteLine("Sent player to new client");

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
                    if (parts[0] == "disconnect")
                    {
                        foreach (KeyValuePair<Socket, player> entry in clients)
                        {
                            if (entry.Value.name == Convert.ToInt16(parts[1]))
                            {
                                player r;
                                if (clients.TryRemove(entry.Key, out r))
                                {
                                    SendTCP(entry.Key, "remove:OK:");
                                    String sql = "UPDATE players SET HIGHSCORE = @score WHERE ID = @username";
                                    using (dbConnection = new SQLiteConnection("Data Source=MyDatabase.sqlite;Version=3;"))
                                    {
                                        dbConnection.Open();
                                        using (SQLiteCommand command = new SQLiteCommand(sql, dbConnection))
                                        {
                                            command.Parameters.AddWithValue("@username", parts[1]);
                                            command.Parameters.AddWithValue("@score", parts[2]);
                                            command.ExecuteNonQuery();
                                            foreach (KeyValuePair<Socket, player> connection in clients)
                                            {
                                                SendTCP(connection.Key, "game:disconnection:" + r.name + ":");
                                            }
                                        }
                                    }
                                    Console.WriteLine("REMOVE SUCCESSFULLY");
                                    entry.Key.Disconnect(true);
                                }
                            }
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
                //Console.WriteLine("Sent {0} bytes to client.", bytesSent);

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
            int name = BitConverter.ToInt32(receiveBytes, 0);
            double x = BitConverter.ToDouble(receiveBytes, 8);
            double y = BitConverter.ToDouble(receiveBytes, 16);
            double v_x = BitConverter.ToDouble(receiveBytes, 24);
            double v_y = BitConverter.ToDouble(receiveBytes, 32);
            double dash_cd = BitConverter.ToDouble(receiveBytes, 40);
            bool has_ball = BitConverter.ToBoolean(receiveBytes, 48);

            //Console.WriteLine("xy = {0} {1} {2} {3} {4}" ,name, x, y, v_x, v_y);
            //Console.WriteLine("Received: {0}", parts[0]);
            // The message then needs to be handled
            messageReceived = true;
            foreach (KeyValuePair<Socket, player> entry in clients)
            {
                if (entry.Value.name == name)
                {
                    player p = entry.Value;
                    p.x = x;
                    p.y = y;
                    p.v_x = v_x;
                    p.v_y = v_y;
                    p.dash_cd = dash_cd;
                    p.has_ball = has_ball;

                    clients[entry.Key] = p;
                }
                else
                {
                    SendMessageUDP(e, entry.Value);
                }
            }
            //SendMessageUDP(e, sendString);
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

        static byte[] getBytes(player str)
        {
            int size = Marshal.SizeOf(str);
            byte[] arr = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(str, ptr, true);
            Marshal.Copy(ptr, arr, 0, size);
            int len = arr.Length;
            Marshal.FreeHGlobal(ptr);
            return arr;
        }

        static void SendMessageUDP(IPEndPoint server, player message)
        {
            // create the udp socket
            UdpClient u = new UdpClient();
            u.Connect(server.Address, server.Port);
            byte[] sendBytes = getBytes(message);
            //Console.WriteLine("SENT: {0}", message);
            // send the message
            // the destination is defined by the call to .Connect()
            messageSent = false;
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
