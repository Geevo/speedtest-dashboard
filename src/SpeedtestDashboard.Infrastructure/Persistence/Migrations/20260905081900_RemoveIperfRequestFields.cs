using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpeedtestDashboard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveIperfRequestFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Direction",
                table: "SpeedTestResults");

            migrationBuilder.DropColumn(
                name: "RequestedIperfServerId",
                table: "SpeedTestResults");

            migrationBuilder.DropColumn(
                name: "Direction",
                table: "SpeedTestJobs");

            migrationBuilder.DropColumn(
                name: "RequestedIperfServerId",
                table: "SpeedTestJobs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Direction",
                table: "SpeedTestResults",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedIperfServerId",
                table: "SpeedTestResults",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Direction",
                table: "SpeedTestJobs",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedIperfServerId",
                table: "SpeedTestJobs",
                type: "TEXT",
                nullable: true);
        }
    }
}
