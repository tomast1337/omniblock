using System.Collections.Concurrent;

namespace betareborn
{
    public sealed class TaskPool : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = [];
        private readonly Thread[] _workers;
        private readonly int sleep = 0;

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
            foreach (var job in _queue.GetConsumingEnumerable())
                job();

            if (sleep > 0)
            {
                Thread.Sleep(sleep);
            }
        }

        public void Enqueue(Action job)
        {
            _queue.Add(job);
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            foreach (var t in _workers)
                t.Join();
        }
    }
}
