using System.Security.Cryptography;
using System.Text;
using ProjectLauncher.Core;

namespace ProjectLauncher.Windows;

/// <summary>Owns a named mutex on one dedicated thread; async CLI continuations never release another thread's mutex.</summary>
public sealed class ProjectLease : IDisposable
{
    private readonly ManualResetEventSlim _release = new(false);
    private readonly Thread _owner;
    private int _disposed;
    private ProjectLease(string path)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string name = @"Local\ProjectLauncher-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
        _owner = new Thread(() =>
        {
            Mutex? mutex = null;
            bool acquired = false;
            try
            {
                mutex = new Mutex(false, name);
                try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new ConfigException("该项目已由其他 GUI / CLI 启动器占用。请先关闭对应会话，避免并发修改项目环境。");
                ready.SetResult(); _release.Wait();
            }
            catch (Exception ex) { ready.TrySetException(ex); }
            finally { if (acquired) mutex!.ReleaseMutex(); mutex?.Dispose(); }
        }) { IsBackground = true, Name = "ProjectLauncher lease owner" };
        _owner.Start();
        try { ready.Task.GetAwaiter().GetResult(); }
        catch { _release.Set(); _owner.Join(2000); _release.Dispose(); throw; }
    }
    public static ProjectLease Acquire(string path) => new(path);
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _release.Set(); if (_owner.Join(3000)) _release.Dispose();
    }
}
