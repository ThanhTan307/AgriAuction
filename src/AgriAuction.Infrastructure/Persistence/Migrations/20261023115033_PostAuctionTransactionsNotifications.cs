using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriAuction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PostAuctionTransactionsNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "post_auction_events",
                columns: table => new
                {
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_post_auction_events", x => x.room_id);
                    table.CheckConstraint("ck_post_event_attempts", "attempts >= 0");
                    table.ForeignKey(
                        name: "FK_post_auction_events_simulation_results_room_id",
                        column: x => x.room_id,
                        principalTable: "simulation_results",
                        principalColumn: "room_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "post_auction_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    auction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    farmer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enterprise_id = table.Column<Guid>(type: "uuid", nullable: false),
                    winning_bid_id = table.Column<Guid>(type: "uuid", nullable: false),
                    result_version = table.Column<long>(type: "bigint", nullable: false),
                    winning_price = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    price_basis = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    payment_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    delivery_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_post_auction_transactions", x => x.id);
                    table.CheckConstraint("ck_post_transaction_currency", "currency ~ '^[A-Z]{3}$' AND length(btrim(price_basis)) BETWEEN 1 AND 80");
                    table.CheckConstraint("ck_post_transaction_result", "winning_price > 0 AND result_version > 3 AND farmer_id <> enterprise_id AND closed_at <= created_at");
                    table.CheckConstraint("ck_post_transaction_simulation", "mode = 'Simulation' AND state = 'AwaitingPolicy' AND payment_state = 'PolicyLocked' AND delivery_state = 'PolicyLocked' AND version = 1");
                    table.ForeignKey(
                        name: "FK_post_auction_transactions_auctions_auction_id",
                        column: x => x.auction_id,
                        principalTable: "auctions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_post_auction_transactions_lot_versions_lot_id_revision_id",
                        columns: x => new { x.lot_id, x.revision_id },
                        principalTable: "lot_versions",
                        principalColumns: new[] { "lot_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_post_auction_transactions_lots_lot_id",
                        column: x => x.lot_id,
                        principalTable: "lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_post_auction_transactions_simulation_bids_room_id_winning_b~",
                        columns: x => new { x.room_id, x.winning_bid_id },
                        principalTable: "simulation_bids",
                        principalColumns: new[] { "room_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_post_auction_transactions_simulation_results_room_id",
                        column: x => x.room_id,
                        principalTable: "simulation_results",
                        principalColumn: "room_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_post_auction_transactions_users_enterprise_id",
                        column: x => x.enterprise_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_post_auction_transactions_users_farmer_id",
                        column: x => x.farmer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    auction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.id);
                    table.CheckConstraint("ck_notification_read", "read_at IS NULL OR read_at >= created_at");
                    table.CheckConstraint("ck_notification_simulation", "mode = 'Simulation' AND kind IN ('WinnerRecorded','LotSoldSimulation','TransactionCreated','AuctionClosedNoWinner')");
                    table.ForeignKey(
                        name: "FK_notifications_auctions_auction_id",
                        column: x => x.auction_id,
                        principalTable: "auctions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_notifications_post_auction_events_event_id",
                        column: x => x.event_id,
                        principalTable: "post_auction_events",
                        principalColumn: "room_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_notifications_post_auction_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalTable: "post_auction_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_notifications_users_recipient_id",
                        column: x => x.recipient_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "post_auction_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_post_auction_history", x => x.id);
                    table.CheckConstraint("ck_post_history", "version = 1 AND kind = 'CreatedFromSimulationResult' AND state = 'AwaitingPolicy'");
                    table.ForeignKey(
                        name: "FK_post_auction_history_post_auction_transactions_transaction_~",
                        column: x => x.transaction_id,
                        principalTable: "post_auction_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_auction_id",
                table: "notifications",
                column: "auction_id");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_event_id",
                table: "notifications",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_recipient_id_created_at_id",
                table: "notifications",
                columns: new[] { "recipient_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_recipient_id_event_id_kind",
                table: "notifications",
                columns: new[] { "recipient_id", "event_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_recipient_id_read_at",
                table: "notifications",
                columns: new[] { "recipient_id", "read_at" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_transaction_id",
                table: "notifications",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "IX_post_auction_events_processed_at_next_attempt_at",
                table: "post_auction_events",
                columns: new[] { "processed_at", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_post_auction_history_transaction_id_version",
                table: "post_auction_history",
                columns: new[] { "transaction_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_post_auction_transactions_auction_id",
                table: "post_auction_transactions",
                column: "auction_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_post_auction_transactions_enterprise_id_created_at",
                table: "post_auction_transactions",
                columns: new[] { "enterprise_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_post_auction_transactions_farmer_id_created_at",
                table: "post_auction_transactions",
                columns: new[] { "farmer_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_post_auction_transactions_lot_id_revision_id",
                table: "post_auction_transactions",
                columns: new[] { "lot_id", "revision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_post_auction_transactions_room_id",
                table: "post_auction_transactions",
                column: "room_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_post_auction_transactions_room_id_winning_bid_id",
                table: "post_auction_transactions",
                columns: new[] { "room_id", "winning_bid_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "post_auction_history");

            migrationBuilder.DropTable(
                name: "post_auction_events");

            migrationBuilder.DropTable(
                name: "post_auction_transactions");
        }
    }
}
