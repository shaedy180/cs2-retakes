// Models callback ownership, execution and native address recycling, not CS2 timing.
namespace CounterStrikeSharp.API.Core
{
    public enum FunctionLifetime { SingleUse, Permanent }
    public sealed class FunctionReference
    {
        private static readonly Dictionary<int, FunctionReference> References = new();
        private static int _next;
        private readonly Action _action;
        private readonly FunctionLifetime _lifetime;
        public int Identifier { get; }
        public static int Count => References.Count;
        public static bool IsRegistered(int id) => References.ContainsKey(id);
        private FunctionReference(Action action, FunctionLifetime lifetime) { _action = action; _lifetime = lifetime; Identifier = ++_next; }
        public static FunctionReference Create(Action action, FunctionLifetime lifetime)
        { var result = new FunctionReference(action, lifetime); References.Add(result.Identifier, result); return result; }
        public static void Remove(int id) => References.Remove(id);
        public void Invoke() { try { _action(); } finally { if (_lifetime == FunctionLifetime.SingleUse) Remove(Identifier); } }
    }
    public static class Listeners { public delegate void OnMapEnd(); }
    public class BasePlugin
    {
        private Action? _mapEnd;
        public readonly List<object> Timers = new();
        public void RegisterListener<T>(T callback) where T : Delegate => _mapEnd += () => callback.DynamicInvoke();
        public void MapEnd() => _mapEnd?.Invoke();
    }
}
namespace CounterStrikeSharp.API.Modules.Timers
{
    [Flags] public enum TimerFlags { REPEAT = 1, STOP_ON_MAPCHANGE = 2 }
}
namespace CounterStrikeSharp.API
{
    using CounterStrikeSharp.API.Core;
    using CounterStrikeSharp.API.Modules.Timers;
    public static class Server
    {
        private static readonly Queue<Action> WorldTasks = new();
        public static void NextWorldUpdate(Action action) => WorldTasks.Enqueue(action);
        public static void UpdateWorld() { while (WorldTasks.TryDequeue(out var action)) action(); }
    }
    public static class NativeAPI
    {
        private sealed class Entry(FunctionReference reference, TimerFlags flags)
        { public FunctionReference Reference = reference; public TimerFlags Flags = flags; public bool Running, KillPending; }
        private static readonly Dictionary<IntPtr, Entry> Active = new();
        private static readonly Stack<IntPtr> Recycled = new();
        private static int _next;
        public static bool FailCreate;
        public static readonly List<IntPtr> KillCalls = new();
        public static int Count => Active.Count;
        public static IntPtr CreateTimer(float interval, FunctionReference reference, int flags)
        {
            if (FailCreate) throw new InvalidOperationException("injected native failure");
            var handle = Recycled.Count > 0 ? Recycled.Pop() : new IntPtr(++_next);
            Active.Add(handle, new(reference, (TimerFlags)flags)); return handle;
        }
        public static void KillTimer(IntPtr handle)
        {
            if (Active.Values.Any(entry => entry.Running)) throw new Exception("native kill during timer-vector iteration");
            KillCalls.Add(handle);
            if (!Active.TryGetValue(handle, out var entry)) return;
            if (entry.Running) { entry.KillPending = true; return; }
            Active.Remove(handle); Recycled.Push(handle);
        }
        public static void Fire(IntPtr handle)
        {
            if (!Active.TryGetValue(handle, out var entry)) return;
            if (!FunctionReference.IsRegistered(entry.Reference.Identifier)) throw new Exception("native callback lost its managed delegate root before native deletion");
            entry.Running = true;
            try { entry.Reference.Invoke(); }
            finally
            {
                entry.Running = false;
                if ((entry.Flags & TimerFlags.REPEAT) == 0 || entry.KillPending)
                { Active.Remove(handle); Recycled.Push(handle); }
            }
        }
        public static void MapEnd(bool beforeFirstTick = false)
        {
            // CSS TimerSystem::OnStartupServer skips OnLevelEnd before its first
            // frame. A native addon can restart the map during that interval.
            if (beforeFirstTick) return;
            foreach (var (handle, entry) in Active.ToArray())
                if ((entry.Flags & TimerFlags.STOP_ON_MAPCHANGE) != 0)
                { Active.Remove(handle); Recycled.Push(handle); }
        }
    }
}
