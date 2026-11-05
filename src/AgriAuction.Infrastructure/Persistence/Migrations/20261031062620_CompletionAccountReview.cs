using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriAuction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompletionAccountReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "approval_state",
                table: "users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "approval_version",
                table: "users",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateTable(
                name: "account_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_reviews", x => x.id);
                    table.CheckConstraint("ck_account_review", "outcome IN ('Approve','Reject') AND length(btrim(reason)) BETWEEN 1 AND 2000 AND version > 1");
                    table.ForeignKey(
                        name: "FK_account_reviews_users_account_id",
                        column: x => x.account_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_reviews_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_approval",
                table: "users",
                sql: "approval_state IS NULL OR approval_state IN ('PendingReview','Approved','Rejected')");

            migrationBuilder.CreateIndex(
                name: "IX_account_reviews_account_id_version",
                table: "account_reviews",
                columns: new[] { "account_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_reviews_actor_id",
                table: "account_reviews",
                column: "actor_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_reviews");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_approval",
                table: "users");

            migrationBuilder.DropColumn(
                name: "approval_state",
                table: "users");

            migrationBuilder.DropColumn(
                name: "approval_version",
                table: "users");
        }
    }
}
