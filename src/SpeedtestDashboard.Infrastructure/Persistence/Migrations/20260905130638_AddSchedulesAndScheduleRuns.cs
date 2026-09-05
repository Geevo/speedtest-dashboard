using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpeedtestDashboard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulesAndScheduleRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduleRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScheduledForUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AttemptedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpeedTestSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ServerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RecurrenceKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    RunAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IntervalMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    TimeOfDayMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    DayOfWeek = table.Column<int>(type: "INTEGER", nullable: true),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastRunAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextRunAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastJobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastRunStatus = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeedTestSchedules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleRuns_ScheduleId_AttemptedAtUtc",
                table: "ScheduleRuns",
                columns: new[] { "ScheduleId", "AttemptedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SpeedTestSchedules_Enabled_NextRunAtUtc",
                table: "SpeedTestSchedules",
                columns: new[] { "Enabled", "NextRunAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduleRuns");

            migrationBuilder.DropTable(
                name: "SpeedTestSchedules");
        }
    }
}
