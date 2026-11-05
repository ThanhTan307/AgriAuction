using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriAuction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RealtimeAuctionSimulation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "simulation_bid_commands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    response_json = table.Column<string>(type: "text", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_simulation_bid_commands", x => x.id);
                    table.ForeignKey(
                        name: "FK_simulation_bid_commands_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "simulation_bids",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enterprise_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    room_version = table.Column<long>(type: "bigint", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    previous_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    extended = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_simulation_bids", x => x.id);
                    table.UniqueConstraint("AK_simulation_bids_room_id_id", x => new { x.room_id, x.id });
                    table.CheckConstraint("ck_simulation_bid", "amount > 0 AND room_version > 2 AND accepted_at < previous_ends_at AND previous_ends_at <= ends_at AND extended = (previous_ends_at < ends_at)");
                    table.ForeignKey(
                        name: "FK_simulation_bids_users_enterprise_id",
                        column: x => x.enterprise_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "simulation_rooms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    auction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    configured_by = table.Column<Guid>(type: "uuid", nullable: false),
                    started_by = table.Column<Guid>(type: "uuid", nullable: true),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    starting_price = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    minimum_increment = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    price_basis = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    first_bid_rule = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    increment_rule = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    tie_rule = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    extension_rule = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    extension_window_seconds = table.Column<int>(type: "integer", nullable: false),
                    extension_seconds = table.Column<int>(type: "integer", nullable: false),
                    max_extensions = table.Column<int>(type: "integer", nullable: false),
                    close_rule = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    initial_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    configured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    leading_bid_id = table.Column<Guid>(type: "uuid", nullable: true),
                    leading_enterprise_id = table.Column<Guid>(type: "uuid", nullable: true),
                    current_price = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    bid_count = table.Column<int>(type: "integer", nullable: false),
                    extension_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_simulation_rooms", x => x.id);
                    table.CheckConstraint("ck_room_extension", "extension_count BETWEEN 0 AND max_extensions AND max_extensions BETWEEN 0 AND 1000 AND ((extension_rule = 'Disabled' AND extension_window_seconds = 0 AND extension_seconds = 0 AND max_extensions = 0) OR (extension_rule IN ('FromBidTime','AddToDeadline') AND extension_window_seconds BETWEEN 1 AND 86400 AND extension_seconds BETWEEN 1 AND 86400 AND max_extensions > 0))");
                    table.CheckConstraint("ck_room_leader", "(bid_count = 0 AND current_price IS NULL AND leading_bid_id IS NULL AND leading_enterprise_id IS NULL AND state <> 'ClosedWithWinner') OR (bid_count > 0 AND current_price IS NOT NULL AND current_price > 0 AND leading_bid_id IS NOT NULL AND leading_enterprise_id IS NOT NULL AND state <> 'ClosedNoWinner')");
                    table.CheckConstraint("ck_room_lifecycle", "(state = 'Prepared' AND opened_at IS NULL AND closed_at IS NULL AND started_by IS NULL) OR (state = 'Open' AND opened_at IS NOT NULL AND started_by IS NOT NULL AND closed_at IS NULL) OR (state IN ('ClosedWithWinner','ClosedNoWinner') AND opened_at IS NOT NULL AND started_by IS NOT NULL AND closed_at IS NOT NULL AND closed_at >= ends_at)");
                    table.CheckConstraint("ck_room_rules", "starting_price > 0 AND minimum_increment > 0 AND currency ~ '^[A-Z]{3}$' AND length(btrim(price_basis)) BETWEEN 1 AND 80 AND first_bid_rule = 'AtLeastStartingPrice' AND increment_rule IN ('MinimumIncrease','StartingPriceGrid') AND tie_rule = 'FirstCommittedWins' AND close_rule = 'AtOrAfterDeadline'");
                    table.CheckConstraint("ck_room_schedule", "starts_at < initial_ends_at AND initial_ends_at <= ends_at");
                    table.CheckConstraint("ck_room_simulation", "mode = 'Simulation' AND state IN ('Prepared','Open','ClosedWithWinner','ClosedNoWinner') AND version > 0");
                    table.ForeignKey(
                        name: "FK_simulation_rooms_auctions_auction_id",
                        column: x => x.auction_id,
                        principalTable: "auctions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_simulation_rooms_simulation_bids_id_leading_bid_id",
                        columns: x => new { x.id, x.leading_bid_id },
                        principalTable: "simulation_bids",
                        principalColumns: new[] { "room_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_simulation_rooms_users_configured_by",
                        column: x => x.configured_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_simulation_rooms_users_leading_enterprise_id",
                        column: x => x.leading_enterprise_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_simulation_rooms_users_started_by",
                        column: x => x.started_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "simulation_results",
                columns: table => new
                {
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    winning_bid_id = table.Column<Guid>(type: "uuid", nullable: true),
                    winner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    room_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_simulation_results", x => x.room_id);
                    table.CheckConstraint("ck_simulation_result", "mode = 'Simulation' AND room_version > 2 AND ((winning_bid_id IS NULL AND winner_id IS NULL AND amount IS NULL) OR (winning_bid_id IS NOT NULL AND winner_id IS NOT NULL AND amount IS NOT NULL AND amount > 0))");
                    table.ForeignKey(
                        name: "FK_simulation_results_simulation_bids_room_id_winning_bid_id",
                        columns: x => new { x.room_id, x.winning_bid_id },
                        principalTable: "simulation_bids",
                        principalColumns: new[] { "room_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_simulation_results_simulation_rooms_room_id",
                        column: x => x.room_id,
                        principalTable: "simulation_rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_simulation_results_users_winner_id",
                        column: x => x.winner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "simulation_room_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    auction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    details = table.Column<string>(type: "text", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_simulation_room_events", x => x.id);
                    table.CheckConstraint("ck_room_event", "kind IN ('RoomConfigured','RoomOpened','BidAccepted','RoomClosed') AND version > 0 AND attempts >= 0");
                    table.ForeignKey(
                        name: "FK_simulation_room_events_simulation_rooms_room_id",
                        column: x => x.room_id,
                        principalTable: "simulation_rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_simulation_room_events_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_simulation_bid_commands_actor_id_request_id",
                table: "simulation_bid_commands",
                columns: new[] { "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_simulation_bid_commands_room_id",
                table: "simulation_bid_commands",
                column: "room_id");

            migrationBuilder.CreateIndex(
                name: "IX_simulation_bids_enterprise_id",
                table: "simulation_bids",
                column: "enterprise_id");

            migrationBuilder.CreateIndex(
                name: "IX_simulation_bids_room_id_room_version",
                table: "simulation_bids",
                columns: new[] { "room_id", "room_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_simulation_results_room_id_winning_bid_id",
                table: "simulation_results",
                columns: new[] { "room_id", "winning_bid_id" });

            migrationBuilder.CreateIndex(
                name: "IX_simulation_results_winner_id",
                table: "simulation_results",
                column: "winner_id");

            migrationBuilder.CreateIndex(
                name: "IX_simulation_room_events_actor_id",
                table: "simulation_room_events",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "IX_simulation_room_events_published_at_next_attempt_at",
                table: "simulation_room_events",
                columns: new[] { "published_at", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_simulation_room_events_room_id_version",
                table: "simulation_room_events",
                columns: new[] { "room_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_simulation_rooms_auction_id",
                table: "simulation_rooms",
                column: "auction_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_simulation_rooms_configured_by",
                table: "simulation_rooms",
                column: "configured_by");

            migrationBuilder.CreateIndex(
                name: "IX_simulation_rooms_id_leading_bid_id",
                table: "simulation_rooms",
                columns: new[] { "id", "leading_bid_id" });

            migrationBuilder.CreateIndex(
                name: "IX_simulation_rooms_leading_enterprise_id",
                table: "simulation_rooms",
                column: "leading_enterprise_id");

            migrationBuilder.CreateIndex(
                name: "IX_simulation_rooms_started_by",
                table: "simulation_rooms",
                column: "started_by");

            migrationBuilder.CreateIndex(
                name: "IX_simulation_rooms_state_ends_at",
                table: "simulation_rooms",
                columns: new[] { "state", "ends_at" });

            migrationBuilder.AddForeignKey(
                name: "FK_simulation_bid_commands_simulation_rooms_room_id",
                table: "simulation_bid_commands",
                column: "room_id",
                principalTable: "simulation_rooms",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_simulation_bids_simulation_rooms_room_id",
                table: "simulation_bids",
                column: "room_id",
                principalTable: "simulation_rooms",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_simulation_bids_simulation_rooms_room_id",
                table: "simulation_bids");

            migrationBuilder.DropTable(
                name: "simulation_bid_commands");

            migrationBuilder.DropTable(
                name: "simulation_results");

            migrationBuilder.DropTable(
                name: "simulation_room_events");

            migrationBuilder.DropTable(
                name: "simulation_rooms");

            migrationBuilder.DropTable(
                name: "simulation_bids");
        }
    }
}
