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
