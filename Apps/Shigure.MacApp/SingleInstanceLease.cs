namespace Shigure.MacApp;

public sealed class SingleInstanceLease : IDisposable
{
    public const string DefaultName = "com.arasaka.shigure.mac.runtime";

    private static readonly object ProcessGate = new();
    private static readonly HashSet<string> ProcessLeases = new(StringComparer.Ordinal);

    private readonly string _name;
    private ManualResetEventSlim? _release;
    private Thread? _ownerThread;

    private SingleInstanceLease(
        string name,
        ManualResetEventSlim release,
        Thread ownerThread)
    {
        _name = name;
        _release = release;
        _ownerThread = ownerThread;
    }

    public static SingleInstanceLease? TryAcquire(string name = DefaultName)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("单实例名称不能为空。", nameof(name));
        }

        lock (ProcessGate)
        {
            if (!ProcessLeases.Add(name))
            {
                return null;
            }
        }

        var ready = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        Exception? acquisitionError = null;
        var acquired = false;
        var ownerThread = new Thread(() =>
        {
            Mutex? mutex = null;
            try
            {
                mutex = new Mutex(initiallyOwned: false, name);
                try
                {
                    acquired = mutex.WaitOne(TimeSpan.Zero);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }
            }
            catch (Exception exception)
            {
                acquisitionError = exception;
            }
            finally
            {
                ready.Set();
            }

            if (acquired)
            {
                try
                {
                    release.Wait();
                    mutex!.ReleaseMutex();
                }
                finally
                {
                    mutex?.Dispose();
                }
            }
            else
            {
                mutex?.Dispose();
            }
        })
        {
            IsBackground = true,
            Name = "Shigure single-instance owner"
        };
        ownerThread.Start();
        ready.Wait();
        ready.Dispose();

        if (acquisitionError is not null)
        {
            ownerThread.Join();
            release.Dispose();
            RemoveProcessLease(name);
            throw new InvalidOperationException("无法获取 Mac 单实例锁。", acquisitionError);
        }

        if (!acquired)
        {
            ownerThread.Join();
            release.Dispose();
            RemoveProcessLease(name);
            return null;
        }

        return new SingleInstanceLease(name, release, ownerThread);
    }

    public void Dispose()
    {
        var release = Interlocked.Exchange(ref _release, null);
        var ownerThread = Interlocked.Exchange(ref _ownerThread, null);
        if (release is null || ownerThread is null)
        {
            return;
        }

        try
        {
            release.Set();
            ownerThread.Join();
        }
        finally
        {
            release.Dispose();
            RemoveProcessLease(_name);
        }
    }

    private static void RemoveProcessLease(string name)
    {
        lock (ProcessGate)
        {
            ProcessLeases.Remove(name);
        }
    }
}
