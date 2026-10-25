using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using ShaedyTimers;

var checks = 0;
void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
void Check(string name, Action test)
{
    Require(NativeAPI.Count == 0 && FunctionReference.Count == 0, "previous test retained timer/callback");
    test();
    Server.UpdateWorld();
    Require(NativeAPI.Count == 0 && FunctionReference.Count == 0, "test retained timer/callback");
    checks++; Console.WriteLine("PASS " + name);
}
Check("10000 completions release callbacks and leave no stale plugin timer wrappers", () =>
{
    var plugin = new BasePlugin(); using var timers = new PluginTimers(plugin); var calls = 0;
    for (var i = 0; i < 10000; i++) { var t = timers.Schedule(.01f, () => calls++); NativeAPI.Fire(t.Native); }
    Require(calls == 10000 && timers.Count == 0 && plugin.Timers.Count == 0, "completed timers retained");
});
Check("killing a completed timer never kills a different timer at its recycled address", () =>
{
    using var timers = new PluginTimers(new()); var calls = 0;
    var old = timers.Schedule(.01f, () => { }); NativeAPI.Fire(old.Native);
    var next = timers.Schedule(1, () => calls++); Require(old.Native == next.Native, "fixture did not recycle native address");
    var kills = NativeAPI.KillCalls.Count; old.Kill();
    Require(NativeAPI.KillCalls.Count == kills, "stale address sent to native KillTimer");
    NativeAPI.Fire(next.Native); Require(calls == 1, "replacement timer cancelled");
});
Check("cancelled repeats release payloads and cancellation remains idempotent after address reuse", () =>
{
    using var timers = new PluginTimers(new()); var calls = 0;
    var old = timers.Schedule(1, () => calls++, TimerFlags.REPEAT); NativeAPI.Fire(old.Native); old.Kill(); Server.UpdateWorld();
    var next = timers.Schedule(1, () => calls++); var kills = NativeAPI.KillCalls.Count; old.Kill();
    Require(NativeAPI.KillCalls.Count == kills, "repeat cancellation repeated native kill");
    NativeAPI.Fire(next.Native); Require(calls == 2, "repeat/replacement execution changed");
});
Check("both map-end cleanup orders release map timers without touching reused addresses", () =>
{
    foreach (var nativeFirst in new[] { false, true })
    {
        var plugin = new BasePlugin(); using var timers = new PluginTimers(plugin); var calls = 0;
        var old = timers.Schedule(1, () => calls++, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        var survivor = timers.Schedule(1, () => calls++, TimerFlags.REPEAT);
        if (nativeFirst) NativeAPI.MapEnd();
        plugin.MapEnd();
        if (!nativeFirst) NativeAPI.MapEnd();
        var next = timers.Schedule(1, () => calls++); var kills = NativeAPI.KillCalls.Count; old.Kill();
        Require(NativeAPI.KillCalls.Count == kills && timers.Count == 2, "map cleanup issued stale kill or stopped persistent repeat");
        NativeAPI.Fire(next.Native); NativeAPI.Fire(survivor.Native); survivor.Kill(); Server.UpdateWorld();
        Require(calls == 2, "map timer survived or persistent timer stopped");
    }
});
Check("pre-first-frame map reload deletes owned native timers before releasing callbacks", () =>
{
    var plugin = new BasePlugin(); using var timers = new PluginTimers(plugin); var calls = 0;
    var old = timers.Schedule(10, () => calls++, TimerFlags.STOP_ON_MAPCHANGE);
    NativeAPI.MapEnd(beforeFirstTick: true); plugin.MapEnd(); Server.UpdateWorld();
    NativeAPI.Fire(old.Native);
    Require(calls == 0 && NativeAPI.Count == 0, "pre-frame reload left an orphan native timer");
});
Check("self-cancellation during a repeat and callback exceptions release references", () =>
{
    using var timers = new PluginTimers(new()); PluginTimers.Handle? repeat = null;
    repeat = timers.Schedule(1, () => repeat!.Kill(), TimerFlags.REPEAT); NativeAPI.Fire(repeat.Native);
    // Native deletion is deferred. The pointer may still be invoked in that gap,
    // but its managed callback must remain rooted and its action must stay cancelled.
    NativeAPI.Fire(repeat.Native);
    Server.UpdateWorld();
    var failing = timers.Schedule(1, () => throw new InvalidOperationException("fixture"));
    try { NativeAPI.Fire(failing.Native); throw new Exception("fixture failed to throw"); }
    catch (InvalidOperationException) { }
    Require(timers.Count == 0, "exception or self-cancellation retained timer");
});
Check("unload cancels pending/repeating callbacks and rejects future schedules", () =>
{
    var timers = new PluginTimers(new()); var calls = 0;
    timers.Schedule(1, () => calls++); timers.Schedule(1, () => calls++, TimerFlags.REPEAT);
    timers.Dispose(); timers.Dispose();
    try { timers.Schedule(1, () => { }); throw new Exception("schedule accepted after unload"); }
    catch (ObjectDisposedException) { }
    Require(calls == 0, "unload invoked cancelled callback");
});
Check("native creation failure releases the newly registered reference", () =>
{
    using var timers = new PluginTimers(new()); NativeAPI.FailCreate = true;
    try { timers.Schedule(1, () => { }); throw new Exception("fixture did not throw"); }
    catch (InvalidOperationException) { }
    finally { NativeAPI.FailCreate = false; }
    Require(timers.Count == 0, "failed creation retained handle");
});
Check("cross-cancellation waits for a safe phase and ignores a native address recycled before cleanup", () =>
{
    var plugin = new BasePlugin(); using var timers = new PluginTimers(plugin); var calls = 0;
    var victim = timers.Schedule(60, () => calls++);
    var canceller = timers.Schedule(1, () => victim.Kill()); NativeAPI.Fire(canceller.Native);
    NativeAPI.Fire(victim.Native); // It may become due before the queued native deletion.
    var replacement = timers.Schedule(1, () => calls++);
    Require(replacement.Native == victim.Native, "fixture did not reuse the completed cancelled timer");
    Server.UpdateWorld(); NativeAPI.Fire(replacement.Native);
    Require(calls == 1, "cancelled callback ran or cleanup killed replacement");
});
Console.WriteLine($"{checks}/{checks} timer lifecycle checks passed (native scheduling not emulated)");
