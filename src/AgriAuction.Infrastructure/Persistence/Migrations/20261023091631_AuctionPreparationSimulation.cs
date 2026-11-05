using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriAuction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuctionPreparationSimulation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auctions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    farmer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    published_by = table.Column<Guid>(type: "uuid", nullable: true),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_simulation = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    registration_opens_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    registration_closes_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deposit_amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auctions", x => x.id);
                    table.CheckConstraint("ck_auction_amount", "deposit_amount > 0 AND currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_auction_publication", "(state = 'Draft' AND published_at IS NULL AND published_by IS NULL) OR (state = 'Scheduled' AND published_at IS NOT NULL AND published_by IS NOT NULL)");
                    table.CheckConstraint("ck_auction_schedule", "registration_opens_at < registration_closes_at AND registration_closes_at <= starts_at AND starts_at < ends_at");
                    table.CheckConstraint("ck_auction_simulation", "is_simulation AND state IN ('Draft','Scheduled') AND version > 0");
                    table.ForeignKey(
                        name: "FK_auctions_lot_versions_lot_id_revision_id",
                        columns: x => new { x.lot_id, x.revision_id },
                        principalTable: "lot_versions",
                        principalColumns: new[] { "lot_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_auctions_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_auctions_users_farmer_id",
                        column: x => x.farmer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_auctions_users_published_by",
                        column: x => x.published_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "auction_registrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    auction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enterprise_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    eligible_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auction_registrations", x => x.id);
                    table.CheckConstraint("ck_registration_state", "(state = 'PendingDeposit' AND eligible_at IS NULL AND version = 1) OR (state = 'SimulationEligible' AND eligible_at IS NOT NULL AND version = 2)");
                    table.ForeignKey(
                        name: "FK_auction_registrations_auctions_auction_id",
                        column: x => x.auction_id,
                        principalTable: "auctions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_auction_registrations_users_enterprise_id",
                        column: x => x.enterprise_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "registration_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registration_events", x => x.id);
                    table.CheckConstraint("ck_registration_event_state", "state IN ('PendingDeposit','SimulationEligible')");
                    table.ForeignKey(
                        name: "FK_registration_events_auction_registrations_registration_id",
                        column: x => x.registration_id,
                        principalTable: "auction_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_registration_events_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "simulation_deposit_commands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    response_json = table.Column<string>(type: "text", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_simulation_deposit_commands", x => x.id);
                    table.ForeignKey(
                        name: "FK_simulation_deposit_commands_auction_registrations_registrat~",
                        column: x => x.registration_id,
                        principalTable: "auction_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_simulation_deposit_commands_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "simulation_deposits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_simulation_deposits", x => x.id);
                    table.CheckConstraint("ck_simulation_deposit", "mode = 'Simulation' AND amount > 0 AND currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_simulation_deposit_state", "(state = 'AwaitingSimulation' AND confirmed_at IS NULL AND confirmed_by IS NULL) OR (state = 'SimulatedHeld' AND confirmed_at IS NOT NULL AND confirmed_by IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_simulation_deposits_auction_registrations_registration_id",
                        column: x => x.registration_id,
                        principalTable: "auction_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_simulation_deposits_users_confirmed_by",
                        column: x => x.confirmed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "simulation_ledger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    deposit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_simulation_ledger", x => x.id);
                    table.CheckConstraint("ck_simulation_ledger", "kind = 'SimulatedConfirmation' AND provider = 'Simulation' AND amount > 0 AND currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "FK_simulation_ledger_simulation_deposits_deposit_id",
                        column: x => x.deposit_id,
                        principalTable: "simulation_deposits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_simulation_ledger_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auction_registrations_enterprise_id",
                table: "auction_registrations",
                column: "enterprise_id");

            migrationBuilder.CreateIndex(
                name: "ux_registrations_auction_enterprise",
                table: "auction_registrations",
                columns: new[] { "auction_id", "enterprise_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_auctions_created_by",
                table: "auctions",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_auctions_farmer_id",
                table: "auctions",
                column: "farmer_id");

            migrationBuilder.CreateIndex(
                name: "IX_auctions_lot_id_revision_id",
                table: "auctions",
                columns: new[] { "lot_id", "revision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_auctions_published_by",
                table: "auctions",
                column: "published_by");

            migrationBuilder.CreateIndex(
                name: "IX_auctions_state_created_at",
                table: "auctions",
                columns: new[] { "state", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_auctions_lot",
                table: "auctions",
                column: "lot_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_registration_events_actor_id",
                table: "registration_events",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "IX_registration_events_registration_id_state",
                table: "registration_events",
                columns: new[] { "registration_id", "state" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_simulation_deposit_commands_registration_id",
                table: "simulation_deposit_commands",
                column: "registration_id");

            migrationBuilder.CreateIndex(
                name: "ux_simulation_commands_actor_request",
                table: "simulation_deposit_commands",
                columns: new[] { "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_simulation_deposits_confirmed_by",
                table: "simulation_deposits",
                column: "confirmed_by");

            migrationBuilder.CreateIndex(
                name: "IX_simulation_deposits_registration_id",
                table: "simulation_deposits",
                column: "registration_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_simulation_ledger_actor_id",
                table: "simulation_ledger",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "IX_simulation_ledger_deposit_id",
                table: "simulation_ledger",
                column: "deposit_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_simulation_ledger_reference",
                table: "simulation_ledger",
                column: "reference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "registration_events");

            migrationBuilder.DropTable(
                name: "simulation_deposit_commands");

            migrationBuilder.DropTable(
                name: "simulation_ledger");

            migrationBuilder.DropTable(
                name: "simulation_deposits");

            migrationBuilder.DropTable(
                name: "auction_registrations");

            migrationBuilder.DropTable(
                name: "auctions");
        }
    }
}
