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
        reader.SetTimeoutWithLock(5000);
        CardReaderResponse resp = reader.GetUUIDWithLock();
        if (!resp.Success)
        {
            Console.WriteLine("error: " + resp.Error);
        }
        uint uuid = resp.Uuid;

        if (uuid == 0)
        {
            Console.WriteLine("no uuid received");
        }
        else
        {
            Console.WriteLine("success!");
        }
        Console.WriteLine(uuid);
    }
}

