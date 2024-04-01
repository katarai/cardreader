using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Cardreader;

/*******
 * 
 * See https://pcsclite.apdu.fr/api/group__API.html for API details
 * See https://pcsclite.apdu.fr/api/pcsclite_8h.html for constants
 * 
 ******/

public struct CardReaderResponse
{
    public string ID;
    public bool Success;
    public string Error;
}

public class CardReader
{
    // Constants for winscard functions.
    const int SCARD_S_SUCCESS = 0;
    const int SCARD_SCOPE_SYSTEM = 2;
    const int SCARD_SHARE_SHARED = 2;
    const int SCARD_PROTOCOL_T0 = 0x0001;
    const int SCARD_PROTOCOL_T1 = 0x0002;
    const int SCARD_STATE_UNAWARE = 0x0000;
    const int SCARD_STATE_IGNORE = 0x0001;
    const int SCARD_STATE_CHANGED = 0x0002;
    const int SCARD_STATE_EMPTY = 0x0010;
    const int SCARD_STATE_PRESENT = 0x00000020;

    public const ulong INFINITE = 0xFFFFFFFF;   // ~50 days of wait time if used for timeout.

    // Enums for some of the errors returned from winscard functions.
    enum SCardErrors
    {
        SCARD_F_INTERNAL_ERROR = -2146435071,
        SCARD_E_CANCELLED = -2146435070,
        SCARD_E_INVALID_HANDLE = -2146435069,
        SCARD_E_INVALID_PARAMETER = -2146435068,
        SCARD_E_NO_MEMORY = -2146435066,
        SCARD_E_INSUFFICIENT_BUFFER = -2146435064,
        SCARD_E_UNKNOWN_READER = -2146435063,
        SCARD_E_TIMEOUT = -2146435062,
        SCARD_E_SHARING_VIOLATION = -2146435061,
        SCARD_E_NO_SMARTCARD = -2146435060,
        SCARD_E_PROTO_MISMATCH = -2146435057,
        SCARD_E_NOT_READY = -2146435056,
        SCARD_E_INVALID_VALUE = -2146435055,
        SCARD_F_COMM_ERROR = -2146435053,
        SCARD_F_UNKNOWN_ERROR = -2146435052,
        SCARD_E_INVALID_ATR = -2146435051,
        SCARD_E_NOT_TRANSACTED = -2146435050,
        SCARD_E_READER_UNAVAILABLE = -2146435049,
        SCARD_E_DUPLICATE_READER = -2146435045,
        SCARD_E_NO_SERVICE = -2146435043,
        SCARD_E_UNSUPPORTED_FEATURE = -2146435041,
        SCARD_E_NO_READERS_AVAILABLE = -2146435026,
        SCARD_W_UNRESPONSIVE_CARD = -2146434970,
        SCARD_W_UNPOWERED_CARD = -2146434969,
        SCARD_W_RESET_CARD = -2146434968,
        SCARD_W_REMOVED_CARD = -2146434967
    }

    // Winscard functions imports, see p-invoke.net
    [DllImport("winscard.dll")]
    static extern int SCardEstablishContext(
        uint dwScope,
        IntPtr pvReserved1,
        IntPtr pvReserved2,
        out IntPtr phContext
    );

    [DllImport("winscard.dll")]
    static extern int SCardReleaseContext(
        IntPtr hContext
    );

    [DllImport("winscard.dll", EntryPoint = "SCardListReadersA", CharSet = CharSet.Ansi)]
    static extern int SCardListReaders(
        IntPtr hContext,
        byte[] mszGroups,
        byte[] mszReaders,
        ref uint pcchReaders
    );

    [DllImport("winscard.dll", EntryPoint = "SCardConnectA", CharSet = CharSet.Ansi)]
    static extern int SCardConnect(
        IntPtr hContext,
        string szReader,
        uint dwShareMode,
        uint dwPreferredProtocols,
        out IntPtr phCard,
        out uint pdwActiveProtocol
    );

    [DllImport("winscard.dll")]
    static extern int SCardDisconnect(
        IntPtr hCard,
        uint dwDisposition
    );

    [DllImport("winscard.dll")]
    static extern int SCardGetStatusChangeW(
        IntPtr hContext,
        ulong dwTimeout,
        [In, Out] SCARD_READERSTATE[] rgReaderStates,
        uint cReaders
    );

    [DllImport("winscard.dll")]
    static extern int SCardTransmit(
        IntPtr hCard,
        IntPtr pioSendPci,
        byte[] pbSendBuffer,
        uint cbSendLength,
        IntPtr pioRecvPci,
        byte[] pbRecvBuffer,
        ref uint pcbRecvLength
    );

    [DllImport("winscard.dll")]
    public static extern int SCardStatus(
       IntPtr hCard,
       byte[] szReaderName,
       ref uint pcchReaderLen,
       out uint pdwState,
       out uint pdwProtocol,
       byte[] pbAtr,
       ref uint pcbAtrLen
    );


    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SCARD_READERSTATE
    {
        public string szReader;
        public IntPtr pvUserData;
        public int dwCurrentState;
        public int dwEventState;
        public int cbAtr;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 36)]
        public byte[] rgbAtr;
    }

    // Class variables
    private IntPtr hContext;        // CardReader context handle
    private IntPtr hCard;           // Smartcard handle
    private string readerName;      // Name of selected reader
    private ulong timeout;          // Timeout for polling for cardreader state changes in milliseconds; 0 is instant return.

    private Mutex mu = new Mutex();  // Mutex for preventing multiple users from accessing card reader at the same time
    private bool cancel = false;     // Boolean to cancel waiting on card reader scan
    private int currUser = -1;

    // Constructor
    public CardReader()
    {
        // Default handles to nullptr and timeout to instant.
        hContext = IntPtr.Zero;
        hCard = IntPtr.Zero;
        timeout = 0;
        readerName = "";
    }

    // Destructor
    ~CardReader()
    {
        Reset();
    }

    public void SetCancel(int id)
    {
        if (id == currUser)
        {
            Console.WriteLine("Cancelling card reader operations.");
            cancel = true;
        }
        else
        {
            Console.WriteLine("Cannot cancel card operations, incorrect user.");
        }
    }

    // Setter method for timeout duration
    public void SetTimeout(ulong newTimeout)
    {
        timeout = newTimeout;
    }

    // Lock wrapper around timeout setter
    public bool SetTimeoutWithLock(ulong newTimeout)
    {
        cancel = false;
        // Skip setting timeout if it's unchanged.
        if (timeout == newTimeout)
        {
            return false;
        }

        if (mu.WaitOne(0))
        {
            SetTimeout(newTimeout);
            mu.ReleaseMutex();
            return true;
        }
        return false;
    }

    // Method to initially connect to a cardreader
    public void ConnectReader()
    {
        if (hContext != IntPtr.Zero)
        {
            Console.WriteLine("CardReader already established, skipping connection setup.");
            return;
        }

        // Establish context
        int ret = SCardEstablishContext(SCARD_SCOPE_SYSTEM, IntPtr.Zero, IntPtr.Zero, out hContext);
        if (ret != SCARD_S_SUCCESS)
        {
            Console.WriteLine("Failed to establish context. Error: " + (SCardErrors)ret);
            hContext = IntPtr.Zero;
            return;
        }

        // Get available card readers.
        byte[] mszReaders = new byte[2048];
        uint pcchReaders = (uint)mszReaders.Length;
        ret = SCardListReaders(hContext, null, mszReaders, ref pcchReaders);
        if (ret != SCARD_S_SUCCESS)
        {
            Console.WriteLine("Failed to list readers. Error: " + (SCardErrors)ret);
            hContext = IntPtr.Zero;
            return;
        }

        // Convert the readers byte array to string array.
        string[] readers = Encoding.ASCII.GetString(mszReaders, 0, (int)pcchReaders - 1).Split('\0');

        // Check if there were any available readers.
        if (readers.Length == 0)
        {
            Console.WriteLine("No card readers available.");
            hContext = IntPtr.Zero;
            return;
        }

        // Choose the first available reader.
        readerName = readers[0];
        Console.WriteLine("Selected reader: " + readerName);
    }

    // Lock wrapper around GetUUID(); returns a struct with success/failure state
    public CardReaderResponse GetUUIDWithLockAndID(int id, bool alreadyPresent)
    {
        cancel = false;
        var response = new CardReaderResponse()
        {
            ID = "",
            Success = false,
            Error = "failed to acquire UUID, see console output",
        };
        if (mu.WaitOne(0))
        {
            // Set the current user to the given ID; this is the user that is allowed to cancel card operations.
            currUser = id;
            try
            {
                response.ID = GetUUID(alreadyPresent, null);
                if (response.ID != "")
                {
                    response.Success = true;
                }
            }
            catch (Exception e)
            {
                response.Error = e.ToString();
            }
            // Reset the current user to -1, which doesn't match any ID.
            currUser = -1;
            mu.ReleaseMutex();
        }
        else
        {
            response.ID = "";
            response.Success = false;
            response.Error = "failed to acquire mutex";
        }
        return response;
    }

    
    public CardReaderResponse GetUUIDWithCancel(int id, bool alreadyPresent, CancellationToken? cancelToken)
    {
        Console.WriteLine("\n\n-------\nProcessing card request for ID: " + id);
        var response = new CardReaderResponse()
        {
            ID = "",
            Success = false,
            Error = "failed to acquire UUID, see console output",
        };

        response.ID = GetUUID(alreadyPresent, cancelToken);
        if (response.ID != "")
        {
            response.Error = "";
        }
        return response;
    }


    // Method to get the UUID from a card on the connected cardreader.
    // Blocks for the given timeout duration while waiting for a card to be placed.
    public string GetUUID(bool alreadyPresent, CancellationToken? cancelToken)
    {
        int ret;
        uint activeProtocol;

        if (hContext == IntPtr.Zero || readerName == "")
        {
            Console.WriteLine("Error: not connected to any cardreader, skipping UUID request.");
            return "";
        }

        // Create a readerState for our selected card reader, with our starting states set to UNAWARE.
        SCARD_READERSTATE[] readerState = new SCARD_READERSTATE[1];
        readerState[0] = new SCARD_READERSTATE();
        readerState[0].szReader = readerName;
        readerState[0].dwCurrentState = SCARD_STATE_UNAWARE;

        /******
        * readerState[0].dwEventState will contain the new state after a change event.
        * - Note: the upper 16 bits of the dwEventState represent the total number of events,
        *   e.g. 0x160012 represents the 16th event, which is a 0x0010 (no card on reader) and 0x0002 (state changed).
        ******/

        // Poll for a change in the status of the reader for the given timeout duration.
        // As long as a card is on the reader, it is considered a status change, as we start from UNAWARE.
        var startTime = DateTime.UtcNow;
        var noCard = true;

        ret = SCardGetStatusChangeW(hContext, 0, readerState, 1);
        while (
            (readerState[0].dwEventState & SCARD_STATE_EMPTY) == SCARD_STATE_EMPTY
            && DateTime.UtcNow - startTime < TimeSpan.FromMilliseconds(timeout)
        )
        {
            noCard = false;
            if (cancel || (cancelToken.HasValue && cancelToken.Value.IsCancellationRequested))
            {
                Console.WriteLine("Card read cancelled.");
                cancel = false;
                return "";
            }
            ret = SCardGetStatusChangeW(hContext, 0, readerState, 1);

            if (ret != SCARD_S_SUCCESS)
            {
                Console.WriteLine("Failed to get status change. Error code: " + (SCardErrors)ret);
                return "";
            }
        }
        Console.WriteLine("Card removed.");

        do
        {
            if (cancel || (cancelToken.HasValue && cancelToken.Value.IsCancellationRequested))
            {
                Console.WriteLine("Card read cancelled.");
                cancel = false;
                return "";
            }
            ret = SCardGetStatusChangeW(hContext, 0, readerState, 1);

            if (ret != SCARD_S_SUCCESS)
            {
                Console.WriteLine("Failed to get status change. Error code: " + (SCardErrors)ret);
                return "";
            }
        } while (
            !((readerState[0].dwEventState & SCARD_STATE_PRESENT) == SCARD_STATE_PRESENT)
            && DateTime.UtcNow - startTime < TimeSpan.FromMilliseconds(timeout)
        );
        Console.WriteLine("Card inserted.");

        if ((readerState[0].dwEventState & SCARD_STATE_PRESENT) != SCARD_STATE_PRESENT)
        {
            Console.WriteLine("Timeout exceeded, no card detected.");
            return "";
        }

        // Log our starting state and the actual state after the change event.
        foreach (var rs in readerState)
        {
            Console.WriteLine("CardStatus - Current: 0x" + $"{rs.dwCurrentState:X}" + " | " + rs.dwCurrentState);
            Console.WriteLine("CardStatus - Event: 0x" + $"{rs.dwEventState:X}" + " | " + rs.dwEventState);
        }

        // Connect to the card.
        ret = SCardConnect(hContext, readerName, SCARD_SHARE_SHARED, SCARD_PROTOCOL_T0 | SCARD_PROTOCOL_T1, out hCard, out activeProtocol);
        if (ret != SCARD_S_SUCCESS)
        {
            Console.WriteLine("Failed to connect to the card. Error code: " + (SCardErrors)ret);
            return "";
        }

        Console.WriteLine("Connected to card on reader: " + readerName);

        // Send command APDU.
        byte[] commandAPDU = {
                0xff,   // CLA - the instruction class
                0xCA,   // INS - the instruction code
                0x00,   // P1 - 1st parameter to the instruction
                0x00,   // P2 - 2nd parameter to the instruction
                0x00    // Le - size of the transfer
            };
        byte[] response = new byte[256];
        uint responseLength = (uint)response.Length;
        ret = SCardTransmit(hCard, IntPtr.Zero, commandAPDU, (uint)commandAPDU.Length, IntPtr.Zero, response, ref responseLength);
        if (ret != SCARD_S_SUCCESS)
        {
            Console.WriteLine("Failed to transmit command APDU. Error code: " + (SCardErrors)ret);
            return "";
        }

        // Get decimal representation of UUID.
        byte[] trimmedResponse = new byte[responseLength - 2];
        Array.Copy(response, trimmedResponse, responseLength - 2);
        string hexID = BitConverter.ToString(trimmedResponse);
        hexID = hexID.Replace("-", "");

        // Display response.
        Console.WriteLine("Card UUID: " + hexID);
        return hexID;
    }

    public static uint HexToUint(string hexID)
    {
        return uint.Parse(hexID, System.Globalization.NumberStyles.HexNumber);
    }

    public void Reset()
    {
        int ret;

        // Release context and disconnect card if needed.
        if (hCard != IntPtr.Zero)
        {
            Console.WriteLine("Disconnecting from card.");
            ret = SCardDisconnect(hCard, 0);
            if (ret != SCARD_S_SUCCESS)
            {
                Console.WriteLine("WARNING: Failed to disconnect from card. Error: " + (SCardErrors)ret);
            }
        }
        else
        {
            Console.WriteLine("No card to disconnect, skipping.");
        }
        if (hContext != IntPtr.Zero)
        {
            Console.WriteLine("Releasing cardreader context.");
            ret = SCardReleaseContext(hContext);
            if (ret != SCARD_S_SUCCESS)
            {
                Console.WriteLine("WARNING: Failed to release cardreader context. Error: " + (SCardErrors)ret);
            }
        }
        else
        {
            Console.WriteLine("No cardreader context to release, skipping.");
        }

        // Reset all class variables.
        hContext = IntPtr.Zero;
        hCard = IntPtr.Zero;
        timeout = 0;
        readerName = "";
    }
}