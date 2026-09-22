using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReactionChemistry.Backend.Migrations
{
    /// <inheritdoc />
    public partial class ExpansaoTabelaJogos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Games",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DeveloperName",
                table: "Games",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DevelopmentPhase",
                table: "Games",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "Games",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SystemRequirements",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrailerUrl",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Visibility",
                table: "Games",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "DeveloperName",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "DevelopmentPhase",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "SystemRequirements",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "TrailerUrl",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "Games");
        }
    }
}
