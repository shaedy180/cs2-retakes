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
        // Native ownership outlives a self-cancelled callback until the queued
        // safe-phase deletion. Never let SingleUse/Invoke release that root early.
        var reference = FunctionReference.Create((Action)(() => timer!.Invoke()), FunctionLifetime.Permanent);
        timer = new Handle(this, callback, reference, options);
        try
        {
            // Own map cancellation too. CSS skips native map-timer cleanup if a
            // native addon restarts the map before the first GameFrame. Forwarding
            // STOP would give two systems conflicting ownership of this pointer.
            timer.Native = NativeAPI.CreateTimer(interval, reference, (int)(options & ~TimerFlags.STOP_ON_MAPCHANGE));
            _pending.Add(timer);
            return timer;
        }
        catch { FunctionReference.Remove(reference.Identifier); throw; }
    }

    private void OnMapEnd()
    {
        foreach (var timer in _pending.Concat(_retiring).ToArray())
            if ((timer.Flags & TimerFlags.STOP_ON_MAPCHANGE) != 0) timer.Kill();
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

    internal sealed class Handle(PluginTimers owner, Action callback, FunctionReference reference, TimerFlags flags)
    {
        private Action? _callback = callback;
        private bool _active = true;
        private FunctionReference? _reference = reference;
        private bool _nativeAlive = true;
        internal IntPtr Native;
        internal readonly TimerFlags Flags = flags;

        internal void Invoke()
        {
            if ((Flags & TimerFlags.REPEAT) == 0) _nativeAlive = false;
            if (!_active) return;
            var action = _callback;
            if ((Flags & TimerFlags.REPEAT) == 0) Deactivate();
            try { action?.Invoke(); }
            finally
            {
                // Retain the actual delegate object until native code has left
                // this callback, including cancelled repeats awaiting deletion.
                if (!_active && !_nativeAlive) owner.Retire(this);
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
            var owned = _reference;
            if (owned == null) return;
            _reference = null;
            FunctionReference.Remove(owned.Identifier);
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
