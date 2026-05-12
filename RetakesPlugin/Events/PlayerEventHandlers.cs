using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

using RetakesPlugin.Managers;
using RetakesPlugin.Utils;

namespace RetakesPlugin.Events;

public class PlayerEventHandlers
{
    private readonly RetakesPlugin _plugin;
    private readonly GameManager _gameManager;
    private readonly HashSet<CCSPlayerController> _hasMutedVoices;

    private const int AutoJoinMaxAttempts = 8;

    public PlayerEventHandlers(RetakesPlugin plugin, GameManager gameManager, HashSet<CCSPlayerController> hasMutedVoices)
    {
        _plugin = plugin;
        _gameManager = gameManager;
        _hasMutedVoices = hasMutedVoices;
    }

    public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;

        if (!PlayerHelper.IsValid(player))
        {
            return HookResult.Continue;
        }

        player.ForceTeamTime = 3600.0f;

        // Grant VIP to contributors
        if (new List<ulong> { 76561198028510846, 76561198044886803, 76561198414501446, 76561199074660131 }.Contains(player.SteamID))
        {
            var grant = "@css/vip";
            Logger.LogInfo("Queue", $"You have been given queue priority {grant} for being a Retakes contributor!");
            AdminManager.AddPlayerPermissions(player, grant);
            Logger.LogInfo("Player", $"Granted VIP to contributor {player.PlayerName}");
        }

        Logger.LogInfo("Player", $"{player.PlayerName} connected");

        if (_plugin.Config.Queue.ShouldAutoJoinPlayers)
        {
            var userId = player.UserId;

            if (userId != null)
            {
                Logger.LogInfo("AutoJoin", $"connected player={player.PlayerName} userid={userId}");
                var delay = Math.Max(0.25f, _plugin.Config.Queue.AutoJoinDelaySeconds);
                _plugin.AddTimer(delay, () => AutoJoinPlayer(userId.Value, 1), TimerFlags.STOP_ON_MAPCHANGE);
            }

            return HookResult.Continue;
        }

        if (_plugin.Config.Queue.ShouldAutoJoinSpectators)
        {
            _plugin.AddTimer(1.0f, () =>
            {
                if (!PlayerHelper.IsValid(player))
                {
                    return;
                }

                player.ChangeTeam(CsTeam.Spectator);
                player.ExecuteClientCommand("teammenu");
            });
        }

        return HookResult.Continue;
    }

    private void AutoJoinPlayer(int userId, int attempt = 1)
    {
        var player = Utilities.GetPlayerFromUserid(userId);

        if (!PlayerHelper.IsValid(player) || !PlayerHelper.IsConnected(player))
        {
            Logger.LogDebug("AutoJoin", $"Abort invalid player userid={userId} attempt={attempt}");
            return;
        }

        if (player!.IsBot || player.IsHLTV)
        {
            Logger.LogDebug("AutoJoin", $"Skip bot/hltv {player.PlayerName}");
            return;
        }

        if (_gameManager.QueueManager.ActivePlayers.Contains(player))
        {
            Logger.LogDebug("AutoJoin", $"Skip already active {player.PlayerName}");
            return;
        }

        if (_gameManager.QueueManager.QueuePlayers.Contains(player))
        {
            Logger.LogDebug("AutoJoin", $"Skip already queued {player.PlayerName}");
            return;
        }

        var gameRules = GameRulesHelper.GetGameRulesOrNull();
        if (gameRules == null)
        {
            Logger.LogDebug("AutoJoin", $"GameRules not ready for {player.PlayerName}, attempt={attempt}");
            RetryAutoJoin(userId, attempt);
            return;
        }

        Logger.LogInfo("AutoJoin", $"attempt={attempt} player={player.PlayerName} userid={userId} currentTeam={player.Team} warmup={gameRules.WarmupPeriod}");

        var toTeam = (CsTeam)_plugin.Config.Queue.AutoJoinTeam;

        if (toTeam != CsTeam.Terrorist && toTeam != CsTeam.CounterTerrorist)
        {
            Logger.LogDebug("AutoJoin", $"Invalid AutoJoinTeam '{_plugin.Config.Queue.AutoJoinTeam}', falling back to CounterTerrorist");
            toTeam = CsTeam.CounterTerrorist;
        }

        _plugin.HandlePlayerJoinedTeam(player, toTeam, "auto");

        if (!_gameManager.QueueManager.ActivePlayers.Contains(player) &&
            !_gameManager.QueueManager.QueuePlayers.Contains(player))
        {
            RetryAutoJoin(userId, attempt);
        }
    }

    private void RetryAutoJoin(int userId, int attempt)
    {
        if (attempt >= AutoJoinMaxAttempts)
        {
            Logger.LogWarning("AutoJoin", $"Giving up autojoin for userid={userId} after {attempt} attempts");
            return;
        }

        _plugin.AddTimer(0.5f, () => AutoJoinPlayer(userId, attempt + 1), TimerFlags.STOP_ON_MAPCHANGE);
    }

    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;

        if (!PlayerHelper.IsValid(player) || !PlayerHelper.IsConnected(player))
        {
            return HookResult.Continue;
        }

        Logger.LogDebug("Player", $"[{player.PlayerName}] Spawned");

        if (!_gameManager.QueueManager.ActivePlayers.Contains(player))
        {
            if (player.PlayerPawn.Value != null && player.PlayerPawn.IsValid && player.PlayerPawn.Value.IsValid)
            {
                player.PlayerPawn.Value.CommitSuicide(false, true);
            }

            if (!player.IsBot)
            {
                player.ChangeTeam(CsTeam.Spectator);
            }
            else if (!player.IsHLTV)
            {
                _gameManager.QueueManager.ActivePlayers.Add(player);
                Logger.LogInfo("Player", $"Force added bot {player.PlayerName} to active players");
            }

            return HookResult.Continue;
        }

        return HookResult.Continue;
    }

    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var attacker = @event.Attacker;
        var assister = @event.Assister;

        if (PlayerHelper.IsValid(attacker))
        {
            _gameManager.AddKill(attacker);
        }

        if (PlayerHelper.IsValid(assister))
        {
            _gameManager.AddAssist(assister);
        }

        return HookResult.Continue;
    }

    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;

        if (player == null)
        {
            return HookResult.Continue;
        }

        _gameManager.QueueManager.RemovePlayerFromQueues(player);
        _hasMutedVoices.Remove(player);

        Logger.LogInfo("Player", $"{player.PlayerName} disconnected");
        return HookResult.Continue;
    }

    public HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        @event.Silent = true;
        return _gameManager.RemoveSpectators(@event, _hasMutedVoices);
    }
}
