namespace SpeedtestDashboard.Core.Tests;

public sealed class SpeedTestOptions
{
    public const string SectionName = "SpeedTests";

    public int QueueCapacity { get; set; } = 4;

    public int QueueFullRetryAfterSeconds { get; set; } = 5;

    public int SseHeartbeatSeconds { get; set; } = 20;
}
