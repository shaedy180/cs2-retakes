using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;

namespace ShaedyTimers;

// Source distributed: CSS Timer keeps permanent callback references and stale
// wrappers after completion. Own both lifetimes instead of BasePlugin.Timers.
internal sealed class PluginTimers : IDisposable
{
    private readonly HashSet<Handle> _pending = new();
    private readonly HashSet<Handle> _retiring = new();
    private bool _flushScheduled;
    private bool _disposed;
    internal int Count => _pending.Count;

    internal PluginTimers(BasePlugin plugin) => plugin.RegisterListener<Listeners.OnMapEnd>(OnMapEnd);

    internal Handle Schedule(float interval, Action callback, TimerFlags? flags = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var options = flags ?? 0;
        Handle? timer = null;
        var reference = FunctionReference.Create((Action)(() => timer!.Invoke()),
            (options & TimerFlags.REPEAT) != 0 ? FunctionLifetime.Permanent : FunctionLifetime.SingleUse);
        timer = new Handle(this, callback, reference.Identifier, options);
        try
        {
            timer.Native = NativeAPI.CreateTimer(interval, reference, (int)options);
            _pending.Add(timer);
            return timer;
        }
        catch { FunctionReference.Remove(reference.Identifier); throw; }
    }

    private void OnMapEnd()
    {
        foreach (var timer in _pending.Concat(_retiring).ToArray())
            if ((timer.Flags & TimerFlags.STOP_ON_MAPCHANGE) != 0) timer.MapEnded();
    }

    private void Retire(Handle timer)
    {
        _retiring.Add(timer);
        if (_flushScheduled) return;
        _flushScheduled = true;
        // Native TimerSystem iterates mutable vectors. Killing a different timer
        // inside a timer callback can invalidate that iteration. Cancel actions
        // immediately, then delete native timers outside that callback phase.
        Server.NextWorldUpdate(() =>
        {
            _flushScheduled = false;
            foreach (var pending in _retiring.ToArray()) pending.FinishCancellation();
        });
    }

    internal void CancelAll()
    {
        foreach (var timer in _pending.ToArray()) timer.Kill();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelAll();
    }

    internal sealed class Handle(PluginTimers owner, Action callback, int reference, TimerFlags flags)
    {
        private Action? _callback = callback;
        private bool _active = true;
        private bool _executing;
        private bool _released;
        private bool _nativeAlive = true;
        internal IntPtr Native;
        internal readonly TimerFlags Flags = flags;

        internal void Invoke()
        {
            if ((Flags & TimerFlags.REPEAT) == 0) _nativeAlive = false;
            if (!_active) return;
            var action = _callback;
            _executing = true;
            if ((Flags & TimerFlags.REPEAT) == 0) Deactivate();
            try { action?.Invoke(); }
            finally
            {
                _executing = false;
                if (!_active) ReleaseReference();
            }
        }

        private void Deactivate()
        {
            _active = false;
            _callback = null;
            owner._pending.Remove(this);
        }

        private void ReleaseReference()
        {
            if (_released) return;
            _released = true;
            FunctionReference.Remove(reference);
        }

        internal void MapEnded()
        {
            _nativeAlive = false;
            Deactivate();
            owner._retiring.Remove(this);
            // Native STOP_ON_MAPCHANGE cleanup owns deletion. Its address may
            // already have been recycled; never KillTimer with that stale pointer.
            if (!_executing) ReleaseReference();
        }

        public void Kill()
        {
            if (!_active) return;
            Deactivate();
            owner.Retire(this);
        }

        internal void FinishCancellation()
        {
            owner._retiring.Remove(this);
            try { if (_nativeAlive) NativeAPI.KillTimer(Native); }
            finally { _nativeAlive = false; ReleaseReference(); }
        }
    }
}
