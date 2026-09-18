namespace SpeedtestDashboard.Infrastructure.Persistence;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    public const int MaximumProviderMetadataBytes = 32 * 1024;

    public string DatabasePath { get; set; } = "/data/speedtest.db";

    public int CommandTimeoutSeconds { get; set; } = 10;

    public bool CreateMigrationBackups { get; set; } = true;

    public string? MigrationBackupDirectory { get; set; }

    public int MigrationBackupRetentionCount { get; set; } = 3;
}
