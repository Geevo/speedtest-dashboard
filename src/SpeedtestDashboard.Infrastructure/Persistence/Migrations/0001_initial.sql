PRAGMA foreign_keys = ON;

CREATE TABLE SpeedTestJobs (
    Id TEXT NOT NULL PRIMARY KEY,
    ProviderId TEXT NOT NULL
        CHECK (length(ProviderId) BETWEEN 1 AND 32
            AND ProviderId GLOB '[a-z]*'
            AND ProviderId NOT GLOB '*[^a-z0-9-]*'),
    Status TEXT NOT NULL
        CHECK (Status IN ('queued', 'starting', 'running', 'processingResult', 'completed', 'failed', 'cancelled')),
    Version INTEGER NOT NULL CHECK (Version >= 1),
    Stage TEXT NOT NULL CHECK (length(Stage) > 0),
    CreatedAtUtc TEXT NOT NULL,
    StartedAtUtc TEXT NULL,
    CompletedAtUtc TEXT NULL,
    RequestedServerId TEXT NULL,
    FailureCode TEXT NULL,
    FailureMessage TEXT NULL,
    CHECK (
        (Status IN ('completed', 'failed', 'cancelled') AND CompletedAtUtc IS NOT NULL)
        OR
        (Status NOT IN ('completed', 'failed', 'cancelled') AND CompletedAtUtc IS NULL)
    ),
    CHECK (
        (Status = 'completed' AND FailureCode IS NULL)
        OR
        (Status IN ('failed', 'cancelled') AND FailureCode IS NOT NULL)
        OR
        Status NOT IN ('completed', 'failed', 'cancelled')
    )
) STRICT;

CREATE TABLE SpeedTestResults (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    JobId TEXT NOT NULL UNIQUE,
    ProviderId TEXT NOT NULL
        CHECK (length(ProviderId) BETWEEN 1 AND 32
            AND ProviderId GLOB '[a-z]*'
            AND ProviderId NOT GLOB '*[^a-z0-9-]*'),
    Status TEXT NOT NULL CHECK (Status IN ('completed', 'failed', 'cancelled')),
    QueuedAtUtc TEXT NOT NULL,
    StartedAtUtc TEXT NULL,
    CompletedAtUtc TEXT NOT NULL,
    RequestedServerId TEXT NULL,
    ServerId TEXT NULL,
    ServerName TEXT NULL,
    ServerLocation TEXT NULL,
    ServerCountry TEXT NULL,
    DownloadMbps REAL NULL CHECK (DownloadMbps IS NULL OR DownloadMbps >= 0),
    UploadMbps REAL NULL CHECK (UploadMbps IS NULL OR UploadMbps >= 0),
    LatencyMs REAL NULL CHECK (LatencyMs IS NULL OR LatencyMs >= 0),
    JitterMs REAL NULL CHECK (JitterMs IS NULL OR JitterMs >= 0),
    PacketLossPercent REAL NULL CHECK (PacketLossPercent IS NULL OR PacketLossPercent BETWEEN 0 AND 100),
    ResultUrl TEXT NULL,
    FailureCode TEXT NULL,
    FailureMessage TEXT NULL,
    ProviderMetadataJson TEXT NULL CHECK (ProviderMetadataJson IS NULL OR json_valid(ProviderMetadataJson)),
    NetworkState TEXT NULL CHECK (NetworkState IS NULL OR NetworkState IN ('complete', 'partial', 'unavailable')),
    NetworkCheckedAtUtc TEXT NULL,
    NetworkIsStale INTEGER NOT NULL CHECK (NetworkIsStale IN (0, 1)),
    NetworkWarning TEXT NULL CHECK (NetworkWarning IS NULL OR NetworkWarning = 'RefreshFailed'),
    IPv4Address TEXT NULL,
    IPv4Asn TEXT NULL,
    IPv4AsName TEXT NULL,
    IPv4Isp TEXT NULL,
    IPv4CountryCode TEXT NULL,
    IPv4CountryName TEXT NULL,
    IPv4Region TEXT NULL,
    IPv4City TEXT NULL,
    IPv4AddressSource TEXT NULL,
    IPv4MetadataSource TEXT NULL,
    IPv6Address TEXT NULL,
    IPv6Asn TEXT NULL,
    IPv6AsName TEXT NULL,
    IPv6Isp TEXT NULL,
    IPv6CountryCode TEXT NULL,
    IPv6CountryName TEXT NULL,
    IPv6Region TEXT NULL,
    IPv6City TEXT NULL,
    IPv6AddressSource TEXT NULL,
    IPv6MetadataSource TEXT NULL,
    CHECK ((NetworkState IS NULL) = (NetworkCheckedAtUtc IS NULL)),
    CHECK (
        (Status = 'completed' AND FailureCode IS NULL)
        OR
        (Status IN ('failed', 'cancelled') AND FailureCode IS NOT NULL)
    ),
    FOREIGN KEY (JobId) REFERENCES SpeedTestJobs (Id) ON DELETE CASCADE
) STRICT;
CREATE INDEX IX_SpeedTestResults_CompletedAtUtc_Id ON SpeedTestResults (CompletedAtUtc DESC, Id DESC);
CREATE INDEX IX_SpeedTestResults_ProviderId_CompletedAtUtc_Id ON SpeedTestResults (ProviderId, CompletedAtUtc DESC, Id DESC);
CREATE INDEX IX_SpeedTestResults_Status_CompletedAtUtc_Id ON SpeedTestResults (Status, CompletedAtUtc DESC, Id DESC);

CREATE TABLE Users (
    Id TEXT NOT NULL PRIMARY KEY,
    UserName TEXT NOT NULL CHECK (length(UserName) BETWEEN 3 AND 64),
    NormalizedUserName TEXT NOT NULL UNIQUE CHECK (length(NormalizedUserName) BETWEEN 3 AND 64),
    PasswordHash TEXT NOT NULL CHECK (length(PasswordHash) > 0),
    SecurityStamp TEXT NOT NULL CHECK (length(SecurityStamp) > 0),
    AccessFailedCount INTEGER NOT NULL DEFAULT 0 CHECK (AccessFailedCount >= 0),
    LockoutEndUtc TEXT NULL,
    CreatedAtUtc TEXT NOT NULL,
    LastLoginAtUtc TEXT NULL,
    Singleton INTEGER NOT NULL DEFAULT 1 UNIQUE CHECK (Singleton = 1)
) STRICT;

CREATE TABLE DashboardSettings (
    Id INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
    AuthenticationEnabled INTEGER NOT NULL CHECK (AuthenticationEnabled IN (0, 1)),
    ShowAuthenticationDisabledWarning INTEGER NOT NULL CHECK (ShowAuthenticationDisabledWarning IN (0, 1))
) STRICT;
INSERT INTO DashboardSettings (Id, AuthenticationEnabled, ShowAuthenticationDisabledWarning) VALUES (1, 0, 1);

CREATE TABLE ApiCredentials (
    Id INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
    ProtectedSecret TEXT NOT NULL CHECK (length(ProtectedSecret) > 0),
    CreatedAtUtc TEXT NOT NULL,
    LastUsedAtUtc TEXT NULL
) STRICT;

CREATE TABLE ApiIdempotencyRecords (
    Key TEXT NOT NULL PRIMARY KEY CHECK (length(Key) BETWEEN 1 AND 128),
    RequestHash TEXT NOT NULL
        CHECK (length(RequestHash) = 64 AND RequestHash NOT GLOB '*[^0-9A-F]*'),
    JobId TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    FOREIGN KEY (JobId) REFERENCES SpeedTestJobs (Id) ON DELETE CASCADE
) STRICT;
CREATE INDEX IX_ApiIdempotencyRecords_CreatedAtUtc ON ApiIdempotencyRecords (CreatedAtUtc);

CREATE TABLE SpeedTestSchedules (
    Id TEXT NOT NULL PRIMARY KEY,
    Name TEXT NOT NULL CHECK (length(Name) BETWEEN 1 AND 120),
    ProviderId TEXT NOT NULL
        CHECK (length(ProviderId) BETWEEN 1 AND 32
            AND ProviderId GLOB '[a-z]*'
            AND ProviderId NOT GLOB '*[^a-z0-9-]*'),
    ServerId TEXT NULL CHECK (ServerId IS NULL OR length(ServerId) BETWEEN 1 AND 128),
    RecurrenceKind TEXT NOT NULL CHECK (RecurrenceKind IN ('oneoff', 'interval', 'daily', 'weekly')),
    RunAtUtc TEXT NULL,
    IntervalMinutes INTEGER NULL CHECK (IntervalMinutes IS NULL OR IntervalMinutes BETWEEN 1 AND 10080),
    TimeOfDayMinutes INTEGER NULL CHECK (TimeOfDayMinutes IS NULL OR TimeOfDayMinutes BETWEEN 0 AND 1439),
    DayOfWeek INTEGER NULL CHECK (DayOfWeek IS NULL OR DayOfWeek BETWEEN 0 AND 6),
    TimeZoneId TEXT NOT NULL CHECK (length(TimeZoneId) > 0),
    Enabled INTEGER NOT NULL CHECK (Enabled IN (0, 1)),
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL,
    LastRunAtUtc TEXT NULL,
    NextRunAtUtc TEXT NULL,
    LastJobId TEXT NULL,
    LastRunStatus TEXT NULL
        CHECK (LastRunStatus IS NULL OR LastRunStatus IN ('pending', 'queued', 'skipped', 'failed')),
    CHECK (
        (RecurrenceKind = 'oneoff'
            AND RunAtUtc IS NOT NULL AND IntervalMinutes IS NULL
            AND TimeOfDayMinutes IS NULL AND DayOfWeek IS NULL)
        OR
        (RecurrenceKind = 'interval'
            AND RunAtUtc IS NULL AND IntervalMinutes IS NOT NULL
            AND TimeOfDayMinutes IS NULL AND DayOfWeek IS NULL)
        OR
        (RecurrenceKind = 'daily'
            AND RunAtUtc IS NULL AND IntervalMinutes IS NULL
            AND TimeOfDayMinutes IS NOT NULL AND DayOfWeek IS NULL)
        OR
        (RecurrenceKind = 'weekly'
            AND RunAtUtc IS NULL AND IntervalMinutes IS NULL
            AND TimeOfDayMinutes IS NOT NULL AND DayOfWeek IS NOT NULL)
    ),
    FOREIGN KEY (LastJobId) REFERENCES SpeedTestJobs (Id) ON DELETE SET NULL
) STRICT;
CREATE INDEX IX_SpeedTestSchedules_Enabled_NextRunAtUtc ON SpeedTestSchedules (Enabled, NextRunAtUtc);

CREATE TABLE ScheduleRuns (
    Id TEXT NOT NULL PRIMARY KEY,
    ScheduleId TEXT NOT NULL,
    ScheduledForUtc TEXT NOT NULL,
    AttemptedAtUtc TEXT NOT NULL,
    JobId TEXT NULL,
    Status TEXT NOT NULL CHECK (Status IN ('pending', 'queued', 'skipped', 'failed')),
    FailureCode TEXT NULL,
    CHECK (
        (Status = 'pending' AND JobId IS NULL AND FailureCode IS NULL)
        OR
        (Status = 'queued' AND FailureCode IS NULL)
        OR
        (Status IN ('skipped', 'failed') AND JobId IS NULL AND FailureCode IS NOT NULL)
    ),
    FOREIGN KEY (ScheduleId) REFERENCES SpeedTestSchedules (Id) ON DELETE CASCADE,
    FOREIGN KEY (JobId) REFERENCES SpeedTestJobs (Id) ON DELETE SET NULL
) STRICT;
CREATE INDEX IX_ScheduleRuns_ScheduleId_AttemptedAtUtc ON ScheduleRuns (ScheduleId, AttemptedAtUtc DESC);
