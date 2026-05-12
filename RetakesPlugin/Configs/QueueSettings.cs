using System.Text.Json.Serialization;

namespace RetakesPlugin.Configs;

public class QueueSettings
{
    [JsonPropertyName("QueuePriorityFlag")]
    public List<QueuePriorityFlagConfig>? QueuePriorityFlag { get; set; } = new()
    {
        new QueuePriorityFlagConfig { DisplayName = "VIP", Flag = "@css/vip", Priority = 0 }
    };

    [JsonPropertyName("QueueImmunityFlag")]
    public List<QueuePriorityFlagConfig>? QueueImmunityFlag { get; set; } = new()
    {
        new QueuePriorityFlagConfig { DisplayName = "VIP", Flag = "@css/vip", Priority = 0 }
    };

    [JsonPropertyName("ShouldRemoveSpectators")]
    public bool ShouldRemoveSpectators { get; set; } = true;

    [JsonPropertyName("ShouldAutoJoinSpectators")]
    public bool ShouldAutoJoinSpectators { get; set; } = true;

    [JsonPropertyName("ShouldAutoJoinPlayers")]
    public bool ShouldAutoJoinPlayers { get; set; } = false;

    [JsonPropertyName("AutoJoinDelaySeconds")]
    public float AutoJoinDelaySeconds { get; set; } = 1.0f;

    [JsonPropertyName("AutoJoinTeam")]
    public int AutoJoinTeam { get; set; } = 3;

    public List<QueuePriorityFlagConfig> GetPriorityFlags()
    {
        if (QueuePriorityFlag == null || QueuePriorityFlag.Count == 0)
        {
            return new List<QueuePriorityFlagConfig>();
        }

        return QueuePriorityFlag;
    }

    public List<QueuePriorityFlagConfig> GetImmunityFlags()
    {
        if (QueueImmunityFlag == null || QueueImmunityFlag.Count == 0)
        {
            return new List<QueuePriorityFlagConfig>();
        }

        return QueueImmunityFlag;
    }
}