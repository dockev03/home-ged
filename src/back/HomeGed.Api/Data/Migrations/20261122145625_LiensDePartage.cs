using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HomeGed.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class LiensDePartage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "share_links",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(22)", maxLength: 22, nullable: false),
                    document_id = table.Column<int>(type: "integer", nullable: false),
                    owner_id = table.Column<int>(type: "integer", nullable: false),
                    password_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    max_downloads = table.Column<int>(type: "integer", nullable: true),
                    download_count = table.Column<int>(type: "integer", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_share_links", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "share_access_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    share_link_id = table.Column<string>(type: "character varying(22)", maxLength: 22, nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    success = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_share_access_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_share_access_log_share_links_share_link_id",
                        column: x => x.share_link_id,
                        principalTable: "share_links",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_share_access_log_share_link_id_timestamp",
                table: "share_access_log",
                columns: new[] { "share_link_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_share_links_expires_at",
                table: "share_links",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_share_links_owner_id",
                table: "share_links",
                column: "owner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "share_access_log");

            migrationBuilder.DropTable(
                name: "share_links");
        }
    }
}
