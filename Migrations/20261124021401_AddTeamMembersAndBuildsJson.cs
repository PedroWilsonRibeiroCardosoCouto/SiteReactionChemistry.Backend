using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReactionChemistry.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamMembersAndBuildsJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BuildsJson",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TeamMembersJson",
                table: "Games",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuildsJson",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "TeamMembersJson",
                table: "Games");
        }
    }
}
