using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpeedtestDashboard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialHistoryPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SpeedTestJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    Stage = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RequestedServerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RequestedIperfServerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    FailureMessage = table.Column<string>(type: "TEXT", maxLength: 240, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeedTestJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpeedTestResults",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    QueuedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RequestedServerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RequestedIperfServerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    ServerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ServerName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ServerLocation = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ServerCountry = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    DownloadMbps = table.Column<double>(type: "REAL", nullable: true),
                    UploadMbps = table.Column<double>(type: "REAL", nullable: true),
                    LatencyMs = table.Column<double>(type: "REAL", nullable: true),
                    JitterMs = table.Column<double>(type: "REAL", nullable: true),
                    PacketLossPercent = table.Column<double>(type: "REAL", nullable: true),
                    ResultUrl = table.Column<string>(type: "TEXT", maxLength: 780, nullable: true),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    FailureMessage = table.Column<string>(type: "TEXT", maxLength: 240, nullable: true),
                    ProviderMetadataJson = table.Column<string>(type: "TEXT", maxLength: 32768, nullable: true),
                    NetworkState = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    NetworkCheckedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NetworkIsStale = table.Column<bool>(type: "INTEGER", nullable: false),
                    NetworkWarning = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    IPv4Address = table.Column<string>(type: "TEXT", maxLength: 45, nullable: true),
                    IPv4Asn = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    IPv4AsName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    IPv4Isp = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    IPv4CountryCode = table.Column<string>(type: "TEXT", maxLength: 2, nullable: true),
                    IPv4CountryName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IPv4Region = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IPv4City = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IPv4AddressSource = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IPv4MetadataSource = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IPv6Address = table.Column<string>(type: "TEXT", maxLength: 45, nullable: true),
                    IPv6Asn = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    IPv6AsName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    IPv6Isp = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    IPv6CountryCode = table.Column<string>(type: "TEXT", maxLength: 2, nullable: true),
                    IPv6CountryName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IPv6Region = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IPv6City = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IPv6AddressSource = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IPv6MetadataSource = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeedTestResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeedTestResults_SpeedTestJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "SpeedTestJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpeedTestResults_CompletedAtUtc_Id",
                table: "SpeedTestResults",
                columns: new[] { "CompletedAtUtc", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_SpeedTestResults_JobId",
                table: "SpeedTestResults",
                column: "JobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpeedTestResults_ProviderId_CompletedAtUtc_Id",
                table: "SpeedTestResults",
                columns: new[] { "ProviderId", "CompletedAtUtc", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_SpeedTestResults_Status_CompletedAtUtc_Id",
                table: "SpeedTestResults",
                columns: new[] { "Status", "CompletedAtUtc", "Id" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpeedTestResults");

            migrationBuilder.DropTable(
                name: "SpeedTestJobs");
        }
    }
}
