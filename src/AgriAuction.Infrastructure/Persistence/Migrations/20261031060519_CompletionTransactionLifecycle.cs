using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriAuction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompletionTransactionLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_post_transaction_simulation",
                table: "post_auction_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_post_history",
                table: "post_auction_history");

            migrationBuilder.DropIndex(
                name: "IX_notifications_transaction_id",
                table: "notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_simulation",
                table: "notifications");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "completed_at",
                table: "post_auction_transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "delivered_at",
                table: "post_auction_transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "payment_confirmed_at",
                table: "post_auction_transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "payment_requested_at",
                table: "post_auction_transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "policy_profile",
                table: "post_auction_transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "received_at",
                table: "post_auction_transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "actor_id",
                table: "post_auction_history",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "note",
                table: "post_auction_history",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "policy_profile",
                table: "auctions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "transaction_source_id",
                table: "notifications",
                type: "uuid",
                nullable: true,
                computedColumnSql: "CASE WHEN kind = 'TransactionActionRecorded' THEN event_id END",
                stored: true);

            migrationBuilder.CreateTable(
                name: "transaction_commands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    receipt_json = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transaction_commands", x => x.id);
                    table.UniqueConstraint("AK_transaction_commands_transaction_id_id", x => new { x.transaction_id, x.id });
                    table.ForeignKey(
                        name: "FK_transaction_commands_post_auction_transactions_transaction_~",
                        column: x => x.transaction_id,
                        principalTable: "post_auction_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transaction_commands_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_post_transaction_simulation",
                table: "post_auction_transactions",
                sql: "mode = 'Simulation' AND ((policy_profile IS NULL AND state = 'AwaitingPolicy' AND payment_state = 'PolicyLocked' AND delivery_state = 'PolicyLocked' AND version = 1 AND completed_at IS NULL AND payment_requested_at IS NULL AND payment_confirmed_at IS NULL AND delivered_at IS NULL AND received_at IS NULL) OR (policy_profile = 'D35-Simulation-v1' AND currency = 'VND' AND price_basis = 'whole lot simulation' AND version >= 1 AND state IN ('InProgress','Completed') AND payment_state IN ('AwaitingRequest','AwaitingConfirmation','SimulatedConfirmed') AND delivery_state IN ('AwaitingDelivery','Delivered','Received','IssueReported')))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transaction_completion",
                table: "post_auction_transactions",
                sql: "(state = 'Completed') = (completed_at IS NOT NULL) AND (state <> 'Completed' OR (payment_state = 'SimulatedConfirmed' AND delivery_state = 'Received' AND completed_at >= payment_confirmed_at AND completed_at >= received_at))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transaction_payment",
                table: "post_auction_transactions",
                sql: "(payment_state IN ('AwaitingConfirmation','SimulatedConfirmed')) = (payment_requested_at IS NOT NULL) AND (payment_state = 'SimulatedConfirmed') = (payment_confirmed_at IS NOT NULL) AND (payment_confirmed_at IS NULL OR payment_confirmed_at >= payment_requested_at)");

            migrationBuilder.CreateIndex(
                name: "IX_post_auction_history_actor_id",
                table: "post_auction_history",
                column: "actor_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_post_history",
                table: "post_auction_history",
                sql: "(version = 1 AND kind = 'CreatedFromSimulationResult' AND state IN ('AwaitingPolicy','InProgress')) OR (version > 1 AND actor_id IS NOT NULL AND kind IN ('RequestPayment','ConfirmPayment','Deliver','Receive','Complete','ReportDeliveryIssue') AND state IN ('InProgress','Completed'))");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_transaction_id_transaction_source_id",
                table: "notifications",
                columns: new[] { "transaction_id", "transaction_source_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_simulation",
                table: "notifications",
                sql: "mode = 'Simulation' AND ((kind IN ('WinnerRecorded','LotSoldSimulation','TransactionCreated','AuctionClosedNoWinner') AND complaint_id IS NULL) OR (kind IN ('ComplaintCreated','ComplaintResponded','ComplaintStatusChanged','ComplaintCommented','ComplaintDecisionRecorded') AND complaint_id IS NOT NULL) OR (kind = 'TransactionActionRecorded' AND transaction_id IS NOT NULL AND complaint_id IS NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_transaction_commands_actor_id_request_id",
                table: "transaction_commands",
                columns: new[] { "actor_id", "request_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_notifications_transaction_commands_transaction_id_transacti~",
                table: "notifications",
                columns: new[] { "transaction_id", "transaction_source_id" },
                principalTable: "transaction_commands",
                principalColumns: new[] { "transaction_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_post_auction_history_users_actor_id",
                table: "post_auction_history",
                column: "actor_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_notifications_transaction_commands_transaction_id_transacti~",
                table: "notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_post_auction_history_users_actor_id",
                table: "post_auction_history");

            migrationBuilder.DropTable(
                name: "transaction_commands");

            migrationBuilder.DropCheckConstraint(
                name: "ck_post_transaction_simulation",
                table: "post_auction_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transaction_completion",
                table: "post_auction_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transaction_payment",
                table: "post_auction_transactions");

            migrationBuilder.DropIndex(
                name: "IX_post_auction_history_actor_id",
                table: "post_auction_history");

            migrationBuilder.DropCheckConstraint(
                name: "ck_post_history",
                table: "post_auction_history");

            migrationBuilder.DropIndex(
                name: "IX_notifications_transaction_id_transaction_source_id",
                table: "notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_simulation",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "transaction_source_id",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "completed_at",
                table: "post_auction_transactions");

            migrationBuilder.DropColumn(
                name: "delivered_at",
                table: "post_auction_transactions");

            migrationBuilder.DropColumn(
                name: "payment_confirmed_at",
                table: "post_auction_transactions");

            migrationBuilder.DropColumn(
                name: "payment_requested_at",
                table: "post_auction_transactions");

            migrationBuilder.DropColumn(
                name: "policy_profile",
                table: "post_auction_transactions");

            migrationBuilder.DropColumn(
                name: "received_at",
                table: "post_auction_transactions");

            migrationBuilder.DropColumn(
                name: "actor_id",
                table: "post_auction_history");

            migrationBuilder.DropColumn(
                name: "note",
                table: "post_auction_history");

            migrationBuilder.DropColumn(
                name: "policy_profile",
                table: "auctions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_post_transaction_simulation",
                table: "post_auction_transactions",
                sql: "mode = 'Simulation' AND state = 'AwaitingPolicy' AND payment_state = 'PolicyLocked' AND delivery_state = 'PolicyLocked' AND version = 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_post_history",
                table: "post_auction_history",
                sql: "version = 1 AND kind = 'CreatedFromSimulationResult' AND state = 'AwaitingPolicy'");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_transaction_id",
                table: "notifications",
                column: "transaction_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_simulation",
                table: "notifications",
                sql: "mode = 'Simulation' AND ((kind IN ('WinnerRecorded','LotSoldSimulation','TransactionCreated','AuctionClosedNoWinner') AND complaint_id IS NULL) OR (kind IN ('ComplaintCreated','ComplaintResponded','ComplaintStatusChanged','ComplaintCommented','ComplaintDecisionRecorded') AND complaint_id IS NOT NULL))");
        }
    }
}
