using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Linq;

namespace UMBPoCApp
{
    class Program
    {
        static private TcpClient client;
        static private int retries = 0;
        static int[] channels;
        static int deviceid;
        
        static void Main(string[] args)
        {
            if(args.Length == 0)
            {
                Console.WriteLine("Missing args");
                System.Environment.Exit(-1);
            }
            deviceid = Int32.Parse(args[1]);
            channels = StringToIntList(args[2]).ToArray();
            IPAddress ipAddress = IPAddress.Parse(args[0]);
            Console.CancelKeyPress += new ConsoleCancelEventHandler(myHandler);
            Console.WriteLine("Opening a socket... v2");


            try
            {

                Program.client = new TcpClient();

                Program.client.Client.Connect(ipAddress, 3001);
                if (client.Connected)
                {

                    Console.WriteLine("Connected!");

                    Console.WriteLine("Making request to read channel: 101");

                    Program.client.ReceiveTimeout = 510;
                    SendRequest();
                
                    Task.Run(() => ReadDataLoop()).Wait();

                }


            }
            catch (TimeoutException tex)
            {
                Console.WriteLine("Timeout occured. {0}", tex.Message);
            }
            catch (Exception ex)
            {
                if (ex.InnerException != null)
                {
                    Console.WriteLine("Exception caught: {0}", ex.InnerException.Message);
                }
                else
                {
                    Console.WriteLine("Exception caught: {0}", ex.Message);
                }

            }
            finally
            {
                Console.WriteLine("Exiting.");
                if (Program.client.Connected)
                {
                    Program.client.Close();
                }
            }

        }

        public static IEnumerable<int> StringToIntList(string str)
        {
            if (String.IsNullOrEmpty(str))
                yield break;

            foreach (var s in str.Split(','))
            {
                int num;
                if (int.TryParse(s, out num))
                    yield return num;
            }
        }

        private static byte[] GetHexId(int id)
        {
           return BitConverter.GetBytes(id).Take(2).ToArray<byte>();

        }

        private static byte[] ToByte(int number)
        {
            return BitConverter.GetBytes(number).Take(1).ToArray<byte>();
        }

        private static byte[] buildMultiChannelRequest(int deviceId, int[] channels)
        {
            List<byte> request = new List<byte>();
            request.AddRange(new List<byte> { 0x01, 0x10 }); //SOH, Ver
            request.AddRange(GetHexId(deviceId)); // TO
            request.AddRange(new List<byte>{0x09, 0xF0}); //FROM
            List<byte> command = new List<byte> { 0x2f, 0x10 };
            command.AddRange(ToByte(channels.Length));
            foreach (int channel in channels)
            {
                command.AddRange(GetHexId(channel));
            }
            request.AddRange(ToByte(command.Count()));
            request.Add(0x02); //STX
            request.AddRange(command);
            request.Add(0x03); //ETX
            int crc = 0xFFFF;
            for (int n = 0; n < request.Count; n++)
            {
                crc = CalcCrc(crc, request[n]);
            }
            request.AddRange(BitConverter.GetBytes(crc).Take(2));
            request.Add(0x04); //EOT

            return request.ToArray();

            
        }

        protected static void SendRequest()
        {

            // Single Channel request
            //                                    SOH | VER  | TO        | from     | len  | STX | CMD | verc| payload |  ETX 
            //List<byte> request = new List<byte> { 0x01, 0x10, 0x01, 0x90, 0xFF, 0xFF, 0x04, 0x02, 0x23, 0x10, 0x65, 0x00, 0x03 };

            // Multi Channel request

            //List<byte> multiChannlRequest = new List<byte> { 0x01, 0x10, 0x01, 0x90, 0xFF, 0xFF, 0x04, 0x02, 0x2f, 0x04, 0x63, 0x02, 0x04, };
            byte[] multiChannelRequest = buildMultiChannelRequest(deviceid, channels);
            
            foreach (byte b in multiChannelRequest)
            {
                Console.Write("0x{0:x} | ", b);
            }
            Console.WriteLine("Sending request.");
            int bytesSent = Program.client.Client.Send(multiChannelRequest);
            Console.WriteLine("Sent {0} bytes as part of request.", bytesSent);
        }

        protected static void myHandler(object sender, ConsoleCancelEventArgs args)
        {
            Console.WriteLine("Closing connection and exiting...");
            if (Program.client.Connected)
            {
                Program.client.Close();
            }
            System.Environment.Exit(0);
        }

        private static int CalcCrc(int crc_buff, int input)
        {
            byte i;
            ushort x16;
            
            for(i=0; i<8; i++)
            {
                if (Convert.ToBoolean((crc_buff & 0x0001) ^ (input & 0x01)))
                    x16 = 0x8408;
                else
                    x16 = 0x0000;

                crc_buff = crc_buff >> 1;
                crc_buff ^= x16;
                input = input >> 1;

            }
            return crc_buff;
        }



        private static void ReadDataLoop()
        {

            if (!Program.client.Connected)
            {
                Console.WriteLine("Client not connected");

            }
            else
            {

                string response = "";
                Console.WriteLine("About to read response.");
                response = ReadData();
                Console.WriteLine("Read the following: {0:x}", response);
            }
            
        }

        private static void DisplayValues(byte[] payload)
        {
            foreach(byte b in payload)
            {
                Console.WriteLine("0x{0:x}", b);
            }
            int status = payload[10];

            if (status != 0)
            {
                Console.WriteLine("Problem happened with request. Status: {0}. ", status);
                throw new Exception(String.Format("Problem happened with request. Status: {0}. ", status));

            } else
            {
                
                Console.WriteLine("Poll Success!");
                int numberOfChannels = payload[11];
                
                int channelIndex = 12;
                Console.WriteLine("Channel Index: {0}", channelIndex);
                Console.WriteLine("This response contains {0} channels", numberOfChannels);
                for(int i = 0; i < numberOfChannels; i++)
                {
                    int numberOfBytesInChannel = 0;
                    Console.WriteLine("Channel #: {0} ", i);
                    Console.WriteLine("NumberOfBytesInChannel: {0}", payload[channelIndex]);
                    numberOfBytesInChannel = payload[channelIndex];
                   
                    byte channelDataType = 0x0;
                    //j starts at 1, because after the length there is always a 00h byte.
                    
                    for(int j = channelIndex + 1; j <= channelIndex + numberOfBytesInChannel; j++)
                    {

                        if(j == channelIndex + 2)
                        {
                            //Channel number
                            Console.WriteLine("Channel: {0}", BitConverter.ToInt16(payload, j));
                        }
                        if(j == channelIndex + 4)
                        {
                            //Data type
                            channelDataType = payload[j];
                            Console.WriteLine("DataType: {0:x}", payload[j]);
                        }
                        float value = -9999;
                        if (j == channelIndex + 5)
                        {
                            
                            switch (channelDataType)
                            {
                                case 0x10:
                                    value = payload[j];
                                    break;
                                case 0x16:
                                    value = BitConverter.ToSingle(payload, j);
                                    break;
                                default:
                                    Console.WriteLine("Something happened, invalid datatype.");
                                    break;
                            }


                            //The rest is the int data.
                            Console.WriteLine("Value: {0}", value);
                        }
                        
                    }
                    channelIndex = channelIndex + numberOfBytesInChannel + 1;
                    Console.WriteLine("Next Channel Index: {0}", channelIndex);


                }
            }
        }

        private static string ReadData()
        {
            Console.WriteLine("Reading Data");
            string retVal;
            

            NetworkStream stream = Program.client.GetStream();

            Console.WriteLine("Stream acquired.");
            byte[] myReadBuffer = new byte[128];
            StringBuilder myCompleteMessage = new StringBuilder();
            int numberOfBytesRead = 0;

            do
            {
                numberOfBytesRead = stream.Read(myReadBuffer, 0, myReadBuffer.Length);
                Console.WriteLine("Read {0} number of bytes", numberOfBytesRead);
                DisplayValues(myReadBuffer.Take(numberOfBytesRead).ToArray());

                
                Console.WriteLine("Status: {0:x}", myReadBuffer[10]);
                
                
               /* Console.WriteLine("SOH: {0:x}", myReadBuffer[0]);
                Console.WriteLine("VER: {0:x}", myReadBuffer[1]);
                Console.WriteLine("To: {0:x}{1:x}", myReadBuffer[2], myReadBuffer[3]);
                Console.WriteLine("From: {0:x}{1:x}", myReadBuffer[4], myReadBuffer[5]);
                Console.WriteLine("Len: {0:x}", myReadBuffer[6]);
                Console.WriteLine("STX: {0:x}", myReadBuffer[7]);
                Console.WriteLine("cmd: {0:x}", myReadBuffer[8]);
                Console.WriteLine("verc: {0:x}", myReadBuffer[9]);
                Console.WriteLine("Status: {0:x}", myReadBuffer[10]);
                Console.WriteLine("Channel: {0}", BitConverter.ToInt16(myReadBuffer, 12));
                Console.WriteLine("Type: {0:x}", myReadBuffer[13]);
                Console.WriteLine("Value: {0}", BitConverter.ToSingle(myReadBuffer, 14));
                Console.WriteLine("ETX: {0:x}", myReadBuffer[18]);
                Console.WriteLine("checksum: {0:x}{0:x}", myReadBuffer[19],myReadBuffer[20]);
                Console.WriteLine("EOT: {0:x}", myReadBuffer[21]);*/
                
            }
            while (stream.DataAvailable);



            retVal = myCompleteMessage.ToString();


            return retVal;
        }
    }
}
