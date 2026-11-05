using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriAuction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompletionDepositSettlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "deposit_settlement_commands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deposit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    receipt_json = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deposit_settlement_commands", x => x.id);
                    table.ForeignKey(
                        name: "FK_deposit_settlement_commands_simulation_deposits_deposit_id",
                        column: x => x.deposit_id,
                        principalTable: "simulation_deposits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deposit_settlement_commands_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "deposit_violation_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    deposit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deposit_violation_decisions", x => x.id);
                    table.UniqueConstraint("AK_deposit_violation_decisions_deposit_id_id", x => new { x.deposit_id, x.id });
                    table.CheckConstraint("ck_deposit_violation_basis", "length(btrim(reason)) BETWEEN 1 AND 2000 AND length(btrim(evidence_reference)) BETWEEN 1 AND 1000");
                    table.ForeignKey(
                        name: "FK_deposit_violation_decisions_simulation_deposits_deposit_id",
                        column: x => x.deposit_id,
                        principalTable: "simulation_deposits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deposit_violation_decisions_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "deposit_settlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    deposit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deposit_settlements", x => x.id);
                    table.CheckConstraint("ck_deposit_settlement", "amount > 0 AND currency='VND' AND ((kind='SimulatedRefunded' AND decision_id IS NULL) OR (kind='SimulatedForfeited' AND decision_id IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_deposit_settlements_deposit_violation_decisions_deposit_id_~",
                        columns: x => new { x.deposit_id, x.decision_id },
                        principalTable: "deposit_violation_decisions",
                        principalColumns: new[] { "deposit_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deposit_settlements_simulation_deposits_deposit_id",
                        column: x => x.deposit_id,
                        principalTable: "simulation_deposits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deposit_settlements_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_deposit_settlement_commands_actor_id_request_id",
                table: "deposit_settlement_commands",
                columns: new[] { "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_deposit_settlement_commands_deposit_id",
                table: "deposit_settlement_commands",
                column: "deposit_id");

            migrationBuilder.CreateIndex(
                name: "IX_deposit_settlements_actor_id",
                table: "deposit_settlements",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "IX_deposit_settlements_deposit_id",
                table: "deposit_settlements",
                column: "deposit_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_deposit_settlements_deposit_id_decision_id",
                table: "deposit_settlements",
                columns: new[] { "deposit_id", "decision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_deposit_violation_decisions_actor_id",
                table: "deposit_violation_decisions",
                column: "actor_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deposit_settlement_commands");

            migrationBuilder.DropTable(
                name: "deposit_settlements");

            migrationBuilder.DropTable(
                name: "deposit_violation_decisions");
        }
    }
}
