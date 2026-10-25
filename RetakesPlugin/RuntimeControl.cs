using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Timers;
using System.Text.Json;
using RetakesPlugin.Services;

namespace RetakesPlugin;

public partial class RetakesPlugin
{
    internal ShaedyTimers.PluginTimers TimersOwned { get; private set; } = null!;
    private readonly ActivationGate _activation = new();
    private bool _commandsRegistered;
    private string? _initializedMap;

    internal ShaedyTimers.PluginTimers.Handle ScheduleGameplay(float interval, Action action, TimerFlags? flags = null)
    {
        int generation = _activation.Generation;
        return TimersOwned.Schedule(interval, () => { if (_activation.Allows(generation) && IsPluginEnabled) action(); }, flags ?? TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void InitializeRuntimeControl()
    {
        TimersOwned = new(this);
        // Only the persisted confirmed mode decides startup state. Legacy mode
        // switching writes it before boot and still controls plugin directories.
        try
        {
            var path = Path.Combine(Server.GameDirectory, "csgo", "cfg", "current_mode.json");
            using var state = JsonDocument.Parse(File.ReadAllText(path));
            string? mode = state.RootElement.GetProperty("current_mode").GetString();
            if (mode is "surf" or "kz") RetakesEnabledConVar.Value = false;
        }
        catch (IOException) { }
        catch (JsonException) { }
        catch (KeyNotFoundException) { }
        catch (UnauthorizedAccessException) { }
        _lastEnabledState = RetakesEnabledConVar.Value;
        _activation.Reset(_lastEnabledState);
        Capabilities.RegisterPluginCapability(new PluginCapability<Func<string, Dictionary<string, string>>>("retakes_plugin:runtime_v1"), () => RuntimeControl);
    }

    private Dictionary<string, string> RuntimeControl(string action)
    {
        if (action == "enabled") return new() { ["enabled"] = IsPluginEnabled ? "true" : "false" };
        if (action == "disable") RetakesEnabledConVar.Value = false;
        else if (action == "enable") RetakesEnabledConVar.Value = true;
        else if (action != "status") throw new InvalidOperationException("Unknown Retakes runtime action");
        bool ready = IsPluginEnabled && _initializedMap == Server.MapName && _roundEventHandlers != null;
        try { ready &= _mapConfigService?.GetSpawnsClone().Count > 0; } catch { ready = false; }
        return new() { ["enabled"] = IsPluginEnabled ? "true" : "false", ["ready"] = ready ? "true" : "false" };
    }

    private void ClearRuntimeState(bool enabled)
    {
        try { new PluginCapability<Action<bool>>("retakes_allocator:state_v1").Get()?.Invoke(enabled); } catch { }
        _activation.Reset(enabled);
        TimersOwned.CancelAll();
        SpawnService.ClearAllSpawnModels();
        _showSpawnsCommand?.SetShowingSpawnsForBombsite(null);
        _gameManager?.CancelWaitingForPlayers();
        _gameManager?.QueueManager.ClearAllQueues();
        _roundEventHandlers = null;
        _playerEventHandlers = null;
        _gameManager = null;
        _spawnManager = null;
        _initializedMap = null;
    }
}
