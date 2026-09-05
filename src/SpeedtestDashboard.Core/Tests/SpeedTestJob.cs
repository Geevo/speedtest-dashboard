using SpeedtestDashboard.Core.Network;
using SpeedtestDashboard.Core.Providers;

namespace SpeedtestDashboard.Core.Tests;

public enum SpeedTestJobStatus
{
    Queued,
    Starting,
    Running,
    ProcessingResult,
    Completed,
    Failed,
    Cancelled
}

public static class SpeedTestFailureCodes
{
    public const string ProviderNotFound = "provider_not_found";
    public const string ProviderUnavailable = "provider_unavailable";
    public const string QueueFull = "queue_full";
    public const string CapabilityNotSupported = "capability_not_supported";
    public const string ProcessStartFailed = "process_start_failed";
    public const string ProcessTimeout = "process_timeout";
    public const string ProcessOutputLimit = "process_output_limit";
    public const string ProviderFailed = "provider_failed";
    public const string InvalidProviderResult = "invalid_provider_result";
    public const string Cancelled = "cancelled";
    public const string InternalError = "internal_error";
    public const string InvalidRequest = "invalid_request";
    public const string OoklaDisabled = "ookla_disabled";
    public const string OoklaNotInstalled = "ookla_not_installed";
    public const string OoklaLicenseNotAccepted = "ookla_license_not_accepted";
    public const string OoklaServerNotFound = "ookla_server_not_found";
    public const string OoklaNetworkUnavailable = "ookla_network_unavailable";
    public const string OoklaTimeout = "ookla_timeout";
    public const string OoklaFailed = "ookla_failed";
    public const string OoklaInvalidOutput = "ookla_invalid_output";
    public const string OoklaResultIncomplete = "ookla_result_incomplete";
    public const string LibreSpeedDisabled = "librespeed_disabled";
    public const string LibreSpeedNotInstalled = "librespeed_not_installed";
    public const string LibreSpeedServerListFailed = "librespeed_server_list_failed";
    public const string LibreSpeedServerNotFound = "librespeed_server_not_found";
    public const string LibreSpeedNetworkUnavailable = "librespeed_network_unavailable";
    public const string LibreSpeedTimeout = "librespeed_timeout";
    public const string LibreSpeedFailed = "librespeed_failed";
    public const string LibreSpeedInvalidOutput = "librespeed_invalid_output";
    public const string LibreSpeedResultIncomplete = "librespeed_result_incomplete";
    public const string PersistenceFailed = "persistence_failed";
    public const string ApplicationRestarted = "application_restarted";
}

public sealed record SpeedTestFailure(string Code, string Message);

public sealed record SpeedTestJob(
    Guid Id,
    SpeedTestRequest Request,
    SpeedTestJobStatus Status,
    string Stage,
    long Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    NetworkIdentity? EgressIdentity,
    SpeedTestResult? Result,
    SpeedTestFailure? Failure)
{
    public bool IsTerminal => Status is SpeedTestJobStatus.Completed or SpeedTestJobStatus.Failed or SpeedTestJobStatus.Cancelled;
}

public enum SpeedTestJobEventType
{
    Snapshot,
    State,
    Result,
    Error,
    Heartbeat
}

public sealed record SpeedTestJobEvent(
    SpeedTestJobEventType Type,
    SpeedTestJob? Job,
    long Version,
    DateTimeOffset EmittedAtUtc);

public enum JobMutationResult
{
    Success,
    NotFound,
    InvalidTransition,
    Terminal,
    PersistenceFailed
}
