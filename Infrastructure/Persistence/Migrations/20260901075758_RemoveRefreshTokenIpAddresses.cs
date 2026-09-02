using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRefreshTokenIpAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_by_ip",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "revoked_by_ip",
                table: "refresh_tokens");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "created_by_ip",
                table: "refresh_tokens",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "revoked_by_ip",
                table: "refresh_tokens",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);
        }
    }
}
