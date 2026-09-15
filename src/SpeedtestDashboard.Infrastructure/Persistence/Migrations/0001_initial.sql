PRAGMA foreign_keys = ON;

CREATE TABLE SpeedTestJobs (
    Id TEXT NOT NULL PRIMARY KEY,
    ProviderId TEXT NOT NULL,
    Status TEXT NOT NULL,
    Version INTEGER NOT NULL,
    Stage TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    StartedAtUtc TEXT NULL,
    CompletedAtUtc TEXT NULL,
    RequestedServerId TEXT NULL,
    FailureCode TEXT NULL,
    FailureMessage TEXT NULL
);

CREATE TABLE SpeedTestResults (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    JobId TEXT NOT NULL UNIQUE,
    ProviderId TEXT NOT NULL,
    Status TEXT NOT NULL,
    QueuedAtUtc TEXT NOT NULL,
    StartedAtUtc TEXT NULL,
    CompletedAtUtc TEXT NOT NULL,
    RequestedServerId TEXT NULL,
    ServerId TEXT NULL,
    ServerName TEXT NULL,
    ServerLocation TEXT NULL,
    ServerCountry TEXT NULL,
    DownloadMbps REAL NULL,
    UploadMbps REAL NULL,
    LatencyMs REAL NULL,
    JitterMs REAL NULL,
    PacketLossPercent REAL NULL,
    ResultUrl TEXT NULL,
    FailureCode TEXT NULL,
    FailureMessage TEXT NULL,
    ProviderMetadataJson TEXT NULL,
    NetworkState TEXT NULL,
    NetworkCheckedAtUtc TEXT NULL,
    NetworkIsStale INTEGER NOT NULL,
    NetworkWarning TEXT NULL,
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
    FOREIGN KEY (JobId) REFERENCES SpeedTestJobs (Id) ON DELETE CASCADE
);
CREATE INDEX IX_SpeedTestResults_CompletedAtUtc_Id ON SpeedTestResults (CompletedAtUtc DESC, Id DESC);
CREATE INDEX IX_SpeedTestResults_ProviderId_CompletedAtUtc_Id ON SpeedTestResults (ProviderId, CompletedAtUtc DESC, Id DESC);
CREATE INDEX IX_SpeedTestResults_Status_CompletedAtUtc_Id ON SpeedTestResults (Status, CompletedAtUtc DESC, Id DESC);

CREATE TABLE Users (
    Id TEXT NOT NULL PRIMARY KEY,
    UserName TEXT NOT NULL,
    NormalizedUserName TEXT NOT NULL UNIQUE,
    PasswordHash TEXT NOT NULL,
    SecurityStamp TEXT NOT NULL,
    AccessFailedCount INTEGER NOT NULL DEFAULT 0,
    LockoutEndUtc TEXT NULL,
    CreatedAtUtc TEXT NOT NULL,
    LastLoginAtUtc TEXT NULL
);

CREATE TABLE DashboardSettings (
    Id INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
    AuthenticationEnabled INTEGER NOT NULL,
    ShowAuthenticationDisabledWarning INTEGER NOT NULL
);
INSERT INTO DashboardSettings (Id, AuthenticationEnabled, ShowAuthenticationDisabledWarning) VALUES (1, 0, 1);

CREATE TABLE ApiCredentials (
    Id INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
    ProtectedSecret TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    LastUsedAtUtc TEXT NULL
);

CREATE TABLE ApiIdempotencyRecords (
    Key TEXT NOT NULL PRIMARY KEY,
    RequestHash TEXT NOT NULL,
    JobId TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL
);
CREATE INDEX IX_ApiIdempotencyRecords_CreatedAtUtc ON ApiIdempotencyRecords (CreatedAtUtc);

CREATE TABLE SpeedTestSchedules (
    Id TEXT NOT NULL PRIMARY KEY,
    Name TEXT NOT NULL,
    ProviderId TEXT NOT NULL,
    ServerId TEXT NULL,
    RecurrenceKind TEXT NOT NULL,
    RunAtUtc TEXT NULL,
    IntervalMinutes INTEGER NULL,
    TimeOfDayMinutes INTEGER NULL,
    DayOfWeek INTEGER NULL,
    TimeZoneId TEXT NOT NULL,
    Enabled INTEGER NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL,
    LastRunAtUtc TEXT NULL,
    NextRunAtUtc TEXT NULL,
    LastJobId TEXT NULL,
    LastRunStatus TEXT NULL
);
CREATE INDEX IX_SpeedTestSchedules_Enabled_NextRunAtUtc ON SpeedTestSchedules (Enabled, NextRunAtUtc);

CREATE TABLE ScheduleRuns (
    Id TEXT NOT NULL PRIMARY KEY,
    ScheduleId TEXT NOT NULL,
    ScheduledForUtc TEXT NOT NULL,
    AttemptedAtUtc TEXT NOT NULL,
    JobId TEXT NULL,
    Status TEXT NOT NULL,
    FailureCode TEXT NULL
);
CREATE INDEX IX_ScheduleRuns_ScheduleId_AttemptedAtUtc ON ScheduleRuns (ScheduleId, AttemptedAtUtc DESC);
