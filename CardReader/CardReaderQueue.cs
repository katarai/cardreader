using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;


namespace Cardreader
{

    class JobWrapper
    {
        public int ID;
        public Action<String> WriteMessage;
        public Action<CardReaderResponse> Finish;
        public CancellationToken Cancel;
    }

    class CardQueue
    {
        public AutoResetEvent CardQueueNotifier = new AutoResetEvent(false);
        public BlockingCollection<JobWrapper> Queue = new BlockingCollection<JobWrapper>(new ConcurrentQueue<JobWrapper>());

        private Thread _thread;
        private CancellationTokenSource _cancelWork = new CancellationTokenSource();

        private CardReader reader;

        ~CardQueue()
        {
            this.abort();
        }

        public CardQueue(ulong timeout)
        {
            reader = new CardReader();
            reader.ConnectReader();
            reader.SetTimeoutWithLock(timeout);
            _thread = new Thread(this.work);
            _thread.Start();
        }

        public void abort()
        {
            if (_thread != null)
            {
                _cancelWork.Cancel();
                _thread.Interrupt();
                _thread.Join();
                _thread = null;
            }
        }

        private void work()
        {
            try
            {
                foreach (JobWrapper job in Queue.GetConsumingEnumerable(_cancelWork.Token))
                {
                    try
                    {
                        CardReaderResponse resp = reader.GetUUIDWithCancel(job.ID, false, job.Cancel);

                        string id = resp.ID;

                        job.WriteMessage("\nCard ID: " + id + "\n");
                        job.Finish(resp);
                    }
                    catch (ThreadInterruptedException e)
                    {
                        Console.WriteLine("interrupt detected, re-raising exception...");
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                        throw;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("error, failed to read card: " + e.ToString());
                    }
                    Thread.Sleep(500);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("cardreader thread interrupted: " + ex.ToString());
            }
        }

        public void AddJob(int ID, Action<String> message, Action<CardReaderResponse> finish, CancellationToken token)
        {
            // wrap job
            var wrap = new JobWrapper()
            {
                ID = ID,
                WriteMessage = message,
                Finish = finish,
                Cancel = token,
            };
            // Attempt to add to queue; block for up to 10ms.
            this.Queue.TryAdd(wrap, 10);
        }
    }
}
