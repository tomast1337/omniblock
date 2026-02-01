using System.Collections.Concurrent;

namespace betareborn
{
    public sealed class TaskPool : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = [];
        private readonly Thread[] _workers;
        private readonly int sleep = 0;
        private volatile bool running = true;

        public TaskPool(int threadCount, int sleepMs = 0)
        {
            _workers = new Thread[threadCount];

            for (int i = 0; i < threadCount; i++)
            {
                _workers[i] = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = $"TaskPool Worker {i}"
                };
                _workers[i].Start();
            }

            sleep = sleepMs;
        }

        private void WorkerLoop()
        {
            long c = 0;
            //while (running)
            //{
            //    if (_queue.TryDequeue(out Action? job))
            //    {
            //        job();
            //    }

            //    if (c % 1000 == 0)
            //    {
            //    }

            //    if (sleep > 0)
            //    {
            //        Thread.Sleep(sleep);
            //    }
            //}
            foreach (var job in _queue.GetConsumingEnumerable())
            {
                job();
                c++;
                if (c % 50 == 0)
                {
                    Console.WriteLine("queue size: " + _queue.Count);
                }
            }
        }

        public void Enqueue(Action job)
        {
            _queue.Add(job);
        }

        public void Dispose()
        {
            _queue.CompleteAdding();

            running = false;
            foreach (var t in _workers)
                t.Join();
        }
    }
}
