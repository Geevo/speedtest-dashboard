using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpeedtestDashboard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardAuthenticationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DashboardSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AuthenticationEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowAuthenticationDisabledWarning = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DashboardSettings");
        }
    }
}
