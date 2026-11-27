using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReactionChemistry.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddGameMetadataMediaContactAndRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverImageKey",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GalleryImagesJson",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GenresJson",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Niche",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SocialLink",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Subject",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Rating",
                table: "GameComments",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContactPhone",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "CoverImageKey",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "GalleryImagesJson",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "GenresJson",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Niche",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "SocialLink",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Subject",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "GameComments");
        }
    }
}
