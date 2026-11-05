using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriAuction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompletionSettlementAndDeliveryGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_post_history",
                table: "post_auction_history");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_simulation",
                table: "notifications");

            migrationBuilder.AddColumn<Guid>(
                name: "deposit_source_id",
                table: "notifications",
                type: "uuid",
                nullable: true,
                computedColumnSql: "CASE WHEN kind = 'DepositSettlementRecorded' THEN event_id END",
                stored: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_post_history",
                table: "post_auction_history",
                sql: "(version = 1 AND kind = 'CreatedFromSimulationResult' AND state IN ('AwaitingPolicy','InProgress')) OR (version > 1 AND actor_id IS NOT NULL AND kind IN ('RequestPayment','ConfirmPayment','Deliver','Receive','Complete','ReportDeliveryIssue','ResolveDeliveryIssue') AND state IN ('InProgress','Completed'))");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_deposit_source_id",
                table: "notifications",
                column: "deposit_source_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_simulation",
                table: "notifications",
                sql: "mode = 'Simulation' AND ((kind IN ('WinnerRecorded','LotSoldSimulation','TransactionCreated','AuctionClosedNoWinner','DepositSettlementRecorded') AND complaint_id IS NULL) OR (kind IN ('ComplaintCreated','ComplaintResponded','ComplaintStatusChanged','ComplaintCommented','ComplaintDecisionRecorded') AND complaint_id IS NOT NULL) OR (kind = 'TransactionActionRecorded' AND transaction_id IS NOT NULL AND complaint_id IS NULL))");

            migrationBuilder.AddForeignKey(
                name: "FK_notifications_deposit_settlement_commands_deposit_source_id",
                table: "notifications",
                column: "deposit_source_id",
                principalTable: "deposit_settlement_commands",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_notifications_deposit_settlement_commands_deposit_source_id",
                table: "notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_post_history",
                table: "post_auction_history");

            migrationBuilder.DropIndex(
                name: "IX_notifications_deposit_source_id",
                table: "notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_simulation",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "deposit_source_id",
                table: "notifications");

            migrationBuilder.AddCheckConstraint(
                name: "ck_post_history",
                table: "post_auction_history",
                sql: "(version = 1 AND kind = 'CreatedFromSimulationResult' AND state IN ('AwaitingPolicy','InProgress')) OR (version > 1 AND actor_id IS NOT NULL AND kind IN ('RequestPayment','ConfirmPayment','Deliver','Receive','Complete','ReportDeliveryIssue') AND state IN ('InProgress','Completed'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_simulation",
                table: "notifications",
                sql: "mode = 'Simulation' AND ((kind IN ('WinnerRecorded','LotSoldSimulation','TransactionCreated','AuctionClosedNoWinner') AND complaint_id IS NULL) OR (kind IN ('ComplaintCreated','ComplaintResponded','ComplaintStatusChanged','ComplaintCommented','ComplaintDecisionRecorded') AND complaint_id IS NOT NULL) OR (kind = 'TransactionActionRecorded' AND transaction_id IS NOT NULL AND complaint_id IS NULL))");
        }
    }
}
