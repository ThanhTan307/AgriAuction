using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriAuction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompletionGroundedSanctions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "bidding_suspended_until",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "restriction_version",
                table: "users",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateTable(
                name: "account_sanctions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_hash = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_sanctions", x => x.id);
                    table.CheckConstraint("ck_account_sanction", "version>1 AND length(btrim(reason)) BETWEEN 1 AND 2000 AND ((kind='SuspendBidding' AND until>occurred_at) OR (kind='LiftBiddingSuspension' AND until IS NULL))");
                    table.ForeignKey(
                        name: "FK_account_sanctions_complaint_entries_decision_entry_id",
                        column: x => x.decision_entry_id,
                        principalTable: "complaint_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_sanctions_users_account_id",
                        column: x => x.account_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_sanctions_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_sanctions_account_id_version",
                table: "account_sanctions",
                columns: new[] { "account_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_sanctions_actor_id_request_id",
                table: "account_sanctions",
                columns: new[] { "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_sanctions_decision_entry_id",
                table: "account_sanctions",
                column: "decision_entry_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_sanctions");

            migrationBuilder.DropColumn(
                name: "bidding_suspended_until",
                table: "users");

            migrationBuilder.DropColumn(
                name: "restriction_version",
                table: "users");
        }
    }
}
