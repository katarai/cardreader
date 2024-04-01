using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cardreader;

class Program
{
    public static void Main()
    {
        ThreadQueueTest();
    }

    public static void ThreadQueueTest()
    {
        CardQueue queue = new CardQueue(5000);

        CancellationTokenSource cts0 = new CancellationTokenSource();
        CancellationTokenSource cts1 = new CancellationTokenSource();
        CancellationTokenSource cts2 = new CancellationTokenSource();
        CancellationTokenSource cts3 = new CancellationTokenSource();
        CancellationTokenSource cts4 = new CancellationTokenSource();

        queue.AddJob(0, WriteOut, ProcessFinalize, cts0.Token);
        queue.AddJob(1, WriteOut, ProcessFinalize, cts1.Token);
        queue.AddJob(2, WriteOut, ProcessFinalize, cts2.Token);
        queue.AddJob(3, WriteOut, ProcessFinalize, cts3.Token);
        queue.AddJob(4, WriteOut, ProcessFinalize, cts4.Token);

        Thread.Sleep(500);

        cts0.Cancel();
        cts2.Cancel();

        Thread.Sleep(1000);
    }

    public static void SyncAwaitTest()
    {
        CardReader reader = new CardReader();
        reader.ConnectReader();
        CardReaderResponse resp;
        bool alreadyPresent = false;

        reader.SetTimeoutWithLock(CardReader.INFINITE);

        Console.WriteLine("Waiting for cards. Ctrl+C to exit.");
        Console.WriteLine("----------------------------------\n");

        while (true)
        {
            try
            {
                resp = reader.GetUUIDWithLockAndID(0, alreadyPresent);

                //if (!resp.Success)
                //{
                //    Console.WriteLine("error: " + resp.Error);
                //}

                string id = resp.ID;

                if (id == "")
                {
                    Console.WriteLine("-------------------------------\n");
                    alreadyPresent = false;
                }
                else
                {
                    Console.WriteLine("\nCard ID: " + id + "\n");
                    alreadyPresent = true;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("error, please keep card on reader for longer");
            }
        }
    }

    public static void WriteOut(String output)
    {
        Console.WriteLine("WRITING OUT: " + output);
    }

    public static void ProcessFinalize(CardReaderResponse response)
    {
        Console.WriteLine("OUTPUT RECEIVED: \nID: " + response.ID
            + "\nSuccess: " + response.Success
            + "\nError: " + response.Error);
    }
}

