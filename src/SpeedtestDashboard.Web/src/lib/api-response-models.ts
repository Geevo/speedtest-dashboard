// Response contracts from the API endpoint records and StatisticsContracts.cs.
export type ModelField = { name: string; type: string; nullable: boolean; model?: string }

export const responseModels: Record<string, ModelField[]> = {
  "NetworkIdentityResponse": [
    {
      "name": "state",
      "type": "string",
      "nullable": false
    },
    {
      "name": "checkedAtUtc",
      "type": "string (date-time)",
      "nullable": false
    },
    {
      "name": "ipv4",
      "type": "NetworkAddressIdentityResponse",
      "nullable": true,
      "model": "NetworkAddressIdentityResponse"
    },
    {
      "name": "ipv6",
      "type": "NetworkAddressIdentityResponse",
      "nullable": true,
      "model": "NetworkAddressIdentityResponse"
    },
    {
      "name": "isStale",
      "type": "boolean",
      "nullable": false
    },
    {
      "name": "warning",
      "type": "string",
      "nullable": true
    }
  ],
  "NetworkAddressIdentityResponse": [
    {
      "name": "address",
      "type": "string",
      "nullable": false
    },
    {
      "name": "family",
      "type": "string",
      "nullable": false
    },
    {
      "name": "asn",
      "type": "string",
      "nullable": true
    },
    {
      "name": "asName",
      "type": "string",
      "nullable": true
    },
    {
      "name": "isp",
      "type": "string",
      "nullable": true
    },
    {
      "name": "countryCode",
      "type": "string",
      "nullable": true
    },
    {
      "name": "countryName",
      "type": "string",
      "nullable": true
    },
    {
      "name": "region",
      "type": "string",
      "nullable": true
    },
    {
      "name": "city",
      "type": "string",
      "nullable": true
    },
    {
      "name": "addressSource",
      "type": "string",
      "nullable": false
    },
    {
      "name": "metadataSource",
      "type": "string",
      "nullable": true
    }
  ],
  "ProviderResponse": [
    {
      "name": "id",
      "type": "string",
      "nullable": false
    },
    {
      "name": "displayName",
      "type": "string",
      "nullable": false
    },
    {
      "name": "displayOrder",
      "type": "integer (int32)",
      "nullable": false
    },
    {
      "name": "capabilities",
      "type": "string[]",
      "nullable": false
    },
    {
      "name": "serverSearchLabel",
      "type": "string",
      "nullable": false
    },
    {
      "name": "unavailableGuidance",
      "type": "string",
      "nullable": false
    },
    {
      "name": "disclosures",
      "type": "ProviderDisclosureResponse[]",
      "nullable": false,
      "model": "ProviderDisclosureResponse"
    },
    {
      "name": "installed",
      "type": "boolean",
      "nullable": false
    },
    {
      "name": "healthState",
      "type": "string",
      "nullable": false
    },
    {
      "name": "version",
      "type": "string",
      "nullable": true
    },
    {
      "name": "checkedAtUtc",
      "type": "string (date-time)",
      "nullable": false
    },
    {
      "name": "message",
      "type": "string",
      "nullable": true
    }
  ],
  "ProviderDisclosureResponse": [
    {
      "name": "kind",
      "type": "string",
      "nullable": false
    },
    {
      "name": "message",
      "type": "string",
      "nullable": false
    },
    {
      "name": "url",
      "type": "string",
      "nullable": true
    }
  ],
  "SpeedTestServerResponse": [
    {
      "name": "providerId",
      "type": "string",
      "nullable": false
    },
    {
      "name": "id",
      "type": "string",
      "nullable": false
    },
    {
      "name": "name",
      "type": "string",
      "nullable": false
    },
    {
      "name": "sponsor",
      "type": "string",
      "nullable": true
    },
    {
      "name": "location",
      "type": "string",
      "nullable": true
    },
    {
      "name": "countryCode",
      "type": "string",
      "nullable": true
    },
    {
      "name": "host",
      "type": "string",
      "nullable": true
    },
    {
      "name": "distanceKilometres",
      "type": "number",
      "nullable": true
    },
    {
      "name": "latencyMilliseconds",
      "type": "number",
      "nullable": true
    }
  ],
  "CreateTestResponse": [
    {
      "name": "id",
      "type": "string (uuid)",
      "nullable": false
    },
    {
      "name": "providerId",
      "type": "string",
      "nullable": false
    },
    {
      "name": "status",
      "type": "string",
      "nullable": false
    },
    {
      "name": "stage",
      "type": "string",
      "nullable": false
    },
    {
      "name": "version",
      "type": "integer (int64)",
      "nullable": false
    },
    {
      "name": "createdAtUtc",
      "type": "string (date-time)",
      "nullable": false
    },
    {
      "name": "resourceUrl",
      "type": "string",
      "nullable": false
    },
    {
      "name": "eventsUrl",
      "type": "string",
      "nullable": false
    }
  ],
  "SpeedTestJobResponse": [
    {
      "name": "id",
      "type": "string (uuid)",
      "nullable": false
    },
    {
      "name": "providerId",
      "type": "string",
      "nullable": false
    },
    {
      "name": "status",
      "type": "string",
      "nullable": false
    },
    {
      "name": "stage",
      "type": "string",
      "nullable": false
    },
    {
      "name": "version",
      "type": "integer (int64)",
      "nullable": false
    },
    {
      "name": "createdAtUtc",
      "type": "string (date-time)",
      "nullable": false
    },
    {
      "name": "startedAtUtc",
      "type": "string (date-time)",
      "nullable": true
    },
    {
      "name": "completedAtUtc",
      "type": "string (date-time)",
      "nullable": true
    },
    {
      "name": "egressIdentity",
      "type": "NetworkIdentityResponse",
      "nullable": true,
      "model": "NetworkIdentityResponse"
    },
    {
      "name": "result",
      "type": "SpeedTestResultResponse",
      "nullable": true,
      "model": "SpeedTestResultResponse"
    },
    {
      "name": "failure",
      "type": "SpeedTestFailureResponse",
      "nullable": true,
      "model": "SpeedTestFailureResponse"
    }
  ],
  "SpeedTestResultResponse": [
    {
      "name": "providerId",
      "type": "string",
      "nullable": false
    },
    {
      "name": "serverId",
      "type": "string",
      "nullable": true
    },
    {
      "name": "serverName",
      "type": "string",
      "nullable": true
    },
    {
      "name": "serverLocation",
      "type": "string",
      "nullable": true
    },
    {
      "name": "downloadMbps",
      "type": "number",
      "nullable": true
    },
    {
      "name": "uploadMbps",
      "type": "number",
      "nullable": true
    },
    {
      "name": "latencyMilliseconds",
      "type": "number",
      "nullable": true
    },
    {
      "name": "jitterMilliseconds",
      "type": "number",
      "nullable": true
    },
    {
      "name": "packetLossPercent",
      "type": "number",
      "nullable": true
    },
    {
      "name": "resultUrl",
      "type": "string",
      "nullable": true
    }
  ],
  "SpeedTestFailureResponse": [
    {
      "name": "code",
      "type": "string",
      "nullable": false
    },
    {
      "name": "message",
      "type": "string",
      "nullable": false
    }
  ],
  "HistoryListResponse": [
    {
      "name": "items",
      "type": "HistoryListItemResponse[]",
      "nullable": false,
      "model": "HistoryListItemResponse"
    },
    {
      "name": "nextCursor",
      "type": "string",
      "nullable": true
    }
  ],
  "HistoryListItemResponse": [
    {
      "name": "id",
      "type": "integer (int64)",
      "nullable": false
    },
    {
      "name": "jobId",
      "type": "string (uuid)",
      "nullable": false
    },
    {
      "name": "providerId",
      "type": "string",
      "nullable": false
    },
    {
      "name": "status",
      "type": "string",
      "nullable": false
    },
    {
      "name": "completedAtUtc",
      "type": "string (date-time)",
      "nullable": false
    },
    {
      "name": "serverName",
      "type": "string",
      "nullable": true
    },
    {
      "name": "serverLocation",
      "type": "string",
      "nullable": true
    },
    {
      "name": "downloadMbps",
      "type": "number",
      "nullable": true
    },
    {
      "name": "uploadMbps",
      "type": "number",
      "nullable": true
    },
    {
      "name": "latencyMilliseconds",
      "type": "number",
      "nullable": true
    },
    {
      "name": "jitterMilliseconds",
      "type": "number",
      "nullable": true
    },
    {
      "name": "packetLossPercent",
      "type": "number",
      "nullable": true
    },
    {
      "name": "ipv4Address",
      "type": "string",
      "nullable": true
    },
    {
      "name": "ipv6Address",
      "type": "string",
      "nullable": true
    },
    {
      "name": "failure",
      "type": "SpeedTestFailureResponse",
      "nullable": true,
      "model": "SpeedTestFailureResponse"
    }
  ],
  "HistoryDetailResponse": [
    {
      "name": "id",
      "type": "integer (int64)",
      "nullable": false
    },
    {
      "name": "jobId",
      "type": "string (uuid)",
      "nullable": false
    },
    {
      "name": "providerId",
      "type": "string",
      "nullable": false
    },
    {
      "name": "status",
      "type": "string",
      "nullable": false
    },
    {
      "name": "queuedAtUtc",
      "type": "string (date-time)",
      "nullable": false
    },
    {
      "name": "startedAtUtc",
      "type": "string (date-time)",
      "nullable": true
    },
    {
      "name": "completedAtUtc",
      "type": "string (date-time)",
      "nullable": false
    },
    {
      "name": "requestedServerId",
      "type": "string",
      "nullable": true
    },
    {
      "name": "result",
      "type": "SpeedTestResultResponse",
      "nullable": true,
      "model": "SpeedTestResultResponse"
    },
    {
      "name": "egressIdentity",
      "type": "NetworkIdentityResponse",
      "nullable": true,
      "model": "NetworkIdentityResponse"
    },
    {
      "name": "failure",
      "type": "SpeedTestFailureResponse",
      "nullable": true,
      "model": "SpeedTestFailureResponse"
    },
    {
      "name": "providerMetadata",
      "type": "any JSON value",
      "nullable": true
    }
  ],
  "SpeedTestStatistics": [
    {
      "name": "range",
      "type": "string",
      "nullable": false
    },
    {
      "name": "provider",
      "type": "string",
      "nullable": true
    },
    {
      "name": "fromUtc",
      "type": "string (date-time)",
      "nullable": true
    },
    {
      "name": "toUtc",
      "type": "string (date-time)",
      "nullable": false
    },
    {
      "name": "tests",
      "type": "TestCountStatistics",
      "nullable": false,
      "model": "TestCountStatistics"
    },
    {
      "name": "download",
      "type": "MetricStatistics",
      "nullable": true,
      "model": "MetricStatistics"
    },
    {
      "name": "upload",
      "type": "MetricStatistics",
      "nullable": true,
      "model": "MetricStatistics"
    },
    {
      "name": "latency",
      "type": "MetricStatistics",
      "nullable": true,
      "model": "MetricStatistics"
    },
    {
      "name": "jitter",
      "type": "MetricStatistics",
      "nullable": true,
      "model": "MetricStatistics"
    },
    {
      "name": "packetLoss",
      "type": "MetricStatistics",
      "nullable": true,
      "model": "MetricStatistics"
    },
    {
      "name": "chart",
      "type": "StatisticsChartPoint[]",
      "nullable": false,
      "model": "StatisticsChartPoint"
    },
    {
      "name": "providers",
      "type": "ProviderStatisticsComparison[]",
      "nullable": false,
      "model": "ProviderStatisticsComparison"
    }
  ],
  "TestCountStatistics": [
    {
      "name": "total",
      "type": "integer (int32)",
      "nullable": false
    },
    {
      "name": "completed",
      "type": "integer (int32)",
      "nullable": false
    },
    {
      "name": "failed",
      "type": "integer (int32)",
      "nullable": false
    },
    {
      "name": "cancelled",
      "type": "integer (int32)",
      "nullable": false
    },
    {
      "name": "successRate",
      "type": "number",
      "nullable": true
    }
  ],
  "MetricStatistics": [
    {
      "name": "count",
      "type": "integer (int32)",
      "nullable": false
    },
    {
      "name": "latest",
      "type": "number",
      "nullable": true
    },
    {
      "name": "average",
      "type": "number",
      "nullable": true
    },
    {
      "name": "median",
      "type": "number",
      "nullable": true
    },
    {
      "name": "minimum",
      "type": "number",
      "nullable": true
    },
    {
      "name": "maximum",
      "type": "number",
      "nullable": true
    },
    {
      "name": "p95",
      "type": "number",
      "nullable": true
    },
    {
      "name": "trendPercent",
      "type": "number",
      "nullable": true
    }
  ],
  "StatisticsChartPoint": [
    {
      "name": "bucketStartUtc",
      "type": "string (date-time)",
      "nullable": false
    },
    {
      "name": "downloadMbps",
      "type": "number",
      "nullable": true
    },
    {
      "name": "uploadMbps",
      "type": "number",
      "nullable": true
    },
    {
      "name": "latencyMilliseconds",
      "type": "number",
      "nullable": true
    },
    {
      "name": "jitterMilliseconds",
      "type": "number",
      "nullable": true
    }
  ],
  "ProviderStatisticsComparison": [
    {
      "name": "provider",
      "type": "string",
      "nullable": false
    },
    {
      "name": "tests",
      "type": "TestCountStatistics",
      "nullable": false,
      "model": "TestCountStatistics"
    },
    {
      "name": "medianDownloadMbps",
      "type": "number",
      "nullable": true
    },
    {
      "name": "medianUploadMbps",
      "type": "number",
      "nullable": true
    },
    {
      "name": "medianLatencyMilliseconds",
      "type": "number",
      "nullable": true
    },
    {
      "name": "medianJitterMilliseconds",
      "type": "number",
      "nullable": true
    },
    {
      "name": "medianPacketLossPercent",
      "type": "number",
      "nullable": true
    }
  ],
  "ScheduleResponse": [
    {
      "name": "id",
      "type": "string (uuid)",
      "nullable": false
    },
    {
      "name": "name",
      "type": "string",
      "nullable": false
    },
    {
      "name": "providerId",
      "type": "string",
      "nullable": false
    },
    {
      "name": "serverId",
      "type": "string",
      "nullable": true
    },
    {
      "name": "recurrenceKind",
      "type": "string",
      "nullable": false
    },
    {
      "name": "runAtUtc",
      "type": "string (date-time)",
      "nullable": true
    },
    {
      "name": "intervalMinutes",
      "type": "integer (int32)",
      "nullable": true
    },
    {
      "name": "timeOfDayMinutes",
      "type": "integer (int32)",
      "nullable": true
    },
    {
      "name": "dayOfWeek",
      "type": "integer (int32)",
      "nullable": true
    },
    {
      "name": "timeZoneId",
      "type": "string",
      "nullable": false
    },
    {
      "name": "enabled",
      "type": "boolean",
      "nullable": false
    },
    {
      "name": "completed",
      "type": "boolean",
      "nullable": false
    },
    {
      "name": "createdAtUtc",
      "type": "string (date-time)",
      "nullable": false
    },
    {
      "name": "updatedAtUtc",
      "type": "string (date-time)",
      "nullable": false
    },
    {
      "name": "lastRunAtUtc",
      "type": "string (date-time)",
      "nullable": true
    },
    {
      "name": "nextRunAtUtc",
      "type": "string (date-time)",
      "nullable": true
    },
    {
      "name": "lastJobId",
      "type": "string (uuid)",
      "nullable": true
    },
    {
      "name": "lastRunStatus",
      "type": "string",
      "nullable": true
    }
  ]
}

export const endpointResponseModels: Record<string, string> = {
  "/network": "NetworkIdentityResponse",
  "/providers": "ProviderResponse[]",
  "/providers/{providerId}": "ProviderResponse",
  "/providers/{providerId}/servers": "SpeedTestServerResponse[]",
  "/tests": "CreateTestResponse",
  "/tests/{jobId}": "SpeedTestJobResponse",
  "/tests/{jobId}/cancel": "SpeedTestJobResponse",
  "/history": "HistoryListResponse",
  "/history/{id}": "HistoryDetailResponse",
  "/statistics": "SpeedTestStatistics",
  "/schedules": "ScheduleResponse[]",
  "/schedules/{id}": "ScheduleResponse"
}
