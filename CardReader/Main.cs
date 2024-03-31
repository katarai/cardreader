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
}

