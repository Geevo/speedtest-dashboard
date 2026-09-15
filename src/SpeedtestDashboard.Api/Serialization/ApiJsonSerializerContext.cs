using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using SpeedtestDashboard.Api.Endpoints;
using SpeedtestDashboard.Core.Statistics;

namespace SpeedtestDashboard.Api.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(ChangePasswordRequest))]
[JsonSerializable(typeof(SetupAuthenticationRequest))]
[JsonSerializable(typeof(DisableAuthenticationRequest))]
[JsonSerializable(typeof(AuthenticationPreferencesRequest))]
[JsonSerializable(typeof(CsrfResponse))]
[JsonSerializable(typeof(SessionResponse))]
[JsonSerializable(typeof(ApiKeyResponse))]
[JsonSerializable(typeof(CreateTestRequest))]
[JsonSerializable(typeof(CreateTestResponse))]
[JsonSerializable(typeof(SpeedTestJobResponse))]
[JsonSerializable(typeof(SpeedTestEventResponse))]
[JsonSerializable(typeof(NetworkIdentityResponse))]
[JsonSerializable(typeof(ProviderResponse))]
[JsonSerializable(typeof(ProviderResponse[]))]
[JsonSerializable(typeof(SpeedTestServerResponse[]))]
[JsonSerializable(typeof(IEnumerable<SpeedTestServerResponse>))]
[JsonSerializable(typeof(HistoryListResponse))]
[JsonSerializable(typeof(HistoryDetailResponse))]
[JsonSerializable(typeof(HistoryDeleteAllResponse))]
[JsonSerializable(typeof(SpeedTestStatistics))]
[JsonSerializable(typeof(ScheduleRequest))]
[JsonSerializable(typeof(ScheduleResponse))]
[JsonSerializable(typeof(ScheduleResponse[]))]
[JsonSerializable(typeof(DatabaseStorageResponse))]
[JsonSerializable(typeof(DatabaseCompactionResponse))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
internal sealed partial class ApiJsonSerializerContext : JsonSerializerContext;
