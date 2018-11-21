using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Ports;
using System.Threading;
using System.Diagnostics;

// Wenn übertragung unterbrochen wurde, muss wieder puffer arduino seitig auf 50% gefüllt werden
// -> telegram während pausen senden
// Kapazitätsregelung implementieren


namespace SCARA_uC_Serial
{
    class Program
    {
        public static SerialPort _serialPort = new SerialPort();
        //public static MySerialReader serialReader = new MySerialReader();
        public const int dataLength = 7;
        private static bool running = false;
        private static bool bufferLow = false;
        private static bool bufferHigh = false;
        private static double telegramIntervall = 0.05;
        private static List<byte[]> telegramList = new List<byte[]>();
        private static List<byte[]> receivedDataList = new List<byte[]>();
        private static Thread transmitThread = new Thread(TransmitData);
        private static bool lastDataSent = false;
        private static Queue<byte> receivedData = new Queue<byte>();
        private static byte[] validCallSign = new byte[4] { Convert.ToByte('b'), Convert.ToByte('l'), Convert.ToByte('e'), Convert.ToByte('t') };
        private static byte[] endTransaction = new byte[dataLength] { Convert.ToByte('e'), 0, 0, 0, 0, 0, 0 };
        private static byte[] startTransaction = new byte[dataLength] { Convert.ToByte('s'), 0, 0, 0, 0, 0, 0 };
        private static byte[] pauseTransaction = new byte[dataLength] { Convert.ToByte('p'), 0, 0, 0, 0, 0, 0 };
        private static int buffThresLowDat = 3;             // indicates how many datagrams from the telegramList can be unsent
        // in the end of a transaction before the uC notifies "bufferLow"                                    

        static void Main(string[] args)
        {
            _serialPort.BaudRate = 115200;
            _serialPort.PortName = "COM3";
            _serialPort.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);

            Console.WriteLine("Anzahl der zu übertragenden Punkte eingeben:\t");
            telegramList = CreateMockData(Convert.ToInt32(Console.ReadLine()));
            Console.WriteLine("\nErzeugte Datagramme");
            foreach (byte[] telegram in telegramList)
            {
                string telegramStr = "";
                for (int i = 0; i < dataLength; i++)
                {
                    telegramStr += "\t" + telegram[i];
                }
                Console.WriteLine(telegramStr);
            }

            Console.WriteLine("\nUebertragung\n\ts zum Starten\n\te zum Beenden\n");
            while (!lastDataSent)
            {
                switch (Console.ReadLine())
                {
                    case "s":

                        if (transmitThread.ThreadState == System.Threading.ThreadState.Suspended)
                        {
                            _serialPort.Write(pauseTransaction, 0, dataLength);
                            transmitThread.Resume();
                        }
                        else
                        {
                            try
                            {
                                _serialPort.Open();
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e.Message.ToString());
                            }
                            transmitThread.Start();

                            running = true;
                        }

                        break;
                    case "e":
                        _serialPort.Write(pauseTransaction, 0, dataLength);
                        transmitThread.Suspend();
                        Console.WriteLine("Thread unterbrochen");
                        break;
                }

            }

            Console.ReadKey();
        }

        private static void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort serialPort = (SerialPort)sender;
            // provisorisch.... muss grund herausfinden, warum 240 anfangs PCseitig gesendet wird
            byte receivedCallSign = Convert.ToByte(serialPort.ReadByte());
            // check for valid call sign
            /*if (receivedCallSign == Convert.ToByte('t'))
            {
                ShowInvalidDatagram(serialPort,receivedCallSign,0);
            }
            */
            switch (Convert.ToChar(receivedCallSign))
            {
                case 'b':
                    byte secondByte = Convert.ToByte(serialPort.ReadByte());
                    switch (secondByte)
                    {
                        case 0x01:
                            bufferLow = true;
                            //ReadRestOfDatagram(serialPort, 2);
                            ShowInvalidDatagram(serialPort, Convert.ToByte(receivedCallSign), secondByte);
                            break;
                        case 0xFF:
                            ShowInvalidDatagram(serialPort, Convert.ToByte(receivedCallSign),secondByte);
                            break;
                        default: ShowInvalidDatagram(serialPort, Convert.ToByte(receivedCallSign),secondByte);
                            break;

                    }
                    break;
                default:
                    if (validCallSign.Contains(receivedCallSign))
                    {
                        receivedData.Enqueue(receivedCallSign);
                        for (int i = 1; i < dataLength; i++)
                        {
                            receivedData.Enqueue(Convert.ToByte(serialPort.ReadByte()));
                            ProcessReceivedData();
                        }
                    }
                    else
                    {
                        ShowInvalidDatagram(serialPort, receivedCallSign, 0);
                    }
                    break;
            }

        }

        private static void ShowInvalidDatagram(SerialPort serialPort, byte callSign, byte secondByte)
        {
            //Console.WriteLine("Ungültiges Datagramm");
            string invalidDatagramStr = "\t" + callSign.ToString();
            if (secondByte == 0)
            {
                for (int i = 1; i < dataLength; i++)
                {
                    invalidDatagramStr += "\t" + Convert.ToByte(serialPort.ReadByte());
                }
            }
            else
            {
                invalidDatagramStr += "\t" + secondByte;
                for (int i = 2; i < dataLength; i++)
                {
                    invalidDatagramStr += "\t" + Convert.ToByte(serialPort.ReadByte());
                }
            }

            Console.WriteLine(invalidDatagramStr);
        }

        private static void ReadRestOfDatagram(SerialPort serialPort, int startIndex)
        {
            for (int i = startIndex; i < dataLength; i++)
            {
                serialPort.ReadByte();
            }
        }

        private static void ProcessReceivedData()
        {
            if (receivedData.Count >= dataLength)
            {
                string receivedDatagramStr = "";
                receivedDataList.Add(receivedData.ToArray());
                foreach (byte b in receivedData)
                {
                    receivedDatagramStr += "\t" + b.ToString();
                }
                Console.WriteLine(receivedDatagramStr + "\t" + DateTime.UtcNow.AddHours(1).ToString("HH:mm:ss.fff"));
                receivedData.Clear();
            }
        }

        private static List<byte[]> CreateMockData(int amount)
        {
            Random rand = new Random();
            int cnt = 0;
            List<byte[]> telegramStorage = new List<byte[]>();
            telegramStorage.Add(startTransaction);
            for (int i = 0; i < amount; i++)
            {
                byte[] newPoint = new byte[dataLength];
                newPoint[0] = Convert.ToByte('l');
                newPoint[1] = Convert.ToByte(cnt++);
                for (int j = 2; j < dataLength; j++)
                {
                    newPoint[j] = Convert.ToByte(rand.Next(10, 100));
                }
                telegramStorage.Add(newPoint);
            }
            telegramStorage.Add(endTransaction);
            return telegramStorage;
        }

        private static void TransmitData()
        {
            //DateTime dateTime = new DateTime();
            Stopwatch stopWatch = new Stopwatch();
            //long lastTicks = 0;
            int index = 0;
            int datagramesToSend = 1;                               // send usually one datagram per intervall
            bool firstLoop = true;

            while (running)
            {
                if (firstLoop)
                {
                    Console.WriteLine("Starte Übertragung");
                    stopWatch.Start();
                    firstLoop = false;
                }
                // check if capacity of ringbuffer must be adjusted
                // buffer naturally runs low when the last few datagrams are sent to uC -> controll if this is the case
                if (bufferLow && ((index++) < (telegramList.Count - buffThresLowDat)))
                {
                    datagramesToSend = 2;
                    Console.WriteLine("Reguliere Ringbuffer Kapazität +");
                    bufferLow = false;
                }
                else if (bufferHigh)
                {
                    datagramesToSend = 0;
                    bufferHigh = false;
                }

                if (stopWatch.ElapsedMilliseconds >= telegramIntervall * 1000)
                {
                    stopWatch.Restart();

                    if (index < telegramList.Count)
                    {
                        for (int i = 0; i < datagramesToSend; i++)
                        {
                            _serialPort.Write(telegramList[index++], 0, dataLength);
                        }
                        datagramesToSend = 1;                               // reset to default;   
                    }
                    else
                    {
                        Console.WriteLine("Alle Datagramme verschickt!");

                        // Test ob Methode CheckreceivedTelegrams funktioniert
                        /*byte[] testTelegram = { 108, 34, 56, 1, 2, 3, 4 };
                        receivedDataList.RemoveAt(receivedDataList.Count - 1;
                        receivedDataList.Add(testTelegram);*/
                        Thread.Sleep(1000);                 // wait for ringbuffer to send data
                        CheckReceivedTelegrams();
                        running = false;
                        lastDataSent = true;
                        _serialPort.Close();
                    }
                }
            }
        }

        private static void CheckReceivedTelegrams()
        {
            if (receivedDataList.Count != telegramList.Count)
            {
                Console.WriteLine("\nMehr/weniger Datagramme empfangen als verschickt");
                Console.WriteLine("{0} empfangen zu {1} gesendet", receivedDataList.Count, telegramList.Count);
                int cnt = 0;
                foreach (byte[] bArr in receivedDataList)
                {
                    if (bArr[1] != cnt++)
                    {
                        Console.WriteLine("Telegram mit Index {0} fehlt", --cnt);
                    }
                }
            }
            else
            {
                for (int i = 0; i < receivedDataList.Count; i++)
                {
                    for (int j = 0; j < dataLength; j++)
                    {
                        if ((receivedDataList[i][j] - telegramList[i][j]) != 0)
                        {
                            Console.WriteLine("Wert bei Telegramm: {0} Byte: {1} weicht ab", i, j);
                        }

                    }
                }

            }

        }
    }
}
