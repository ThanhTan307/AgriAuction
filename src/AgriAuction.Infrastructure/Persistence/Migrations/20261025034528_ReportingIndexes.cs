using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriAuction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReportingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_report_accounts_created",
                table: "users",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_report_rooms_configured",
                table: "simulation_rooms",
                columns: new[] { "configured_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_report_results_closed",
                table: "simulation_results",
                column: "closed_at");

            migrationBuilder.CreateIndex(
                name: "ix_report_bids_accepted",
                table: "simulation_bids",
                column: "accepted_at");

            migrationBuilder.CreateIndex(
                name: "ix_report_transactions_created",
                table: "post_auction_transactions",
                columns: new[] { "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_report_lots_created",
                table: "lots",
                columns: new[] { "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_report_complaints_created",
                table: "complaints",
                columns: new[] { "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_report_auctions_created",
                table: "auctions",
                columns: new[] { "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_report_pending_registrations",
                table: "auction_registrations",
                columns: new[] { "state", "registered_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_report_accounts_created",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_report_rooms_configured",
                table: "simulation_rooms");

            migrationBuilder.DropIndex(
                name: "ix_report_results_closed",
                table: "simulation_results");

            migrationBuilder.DropIndex(
                name: "ix_report_bids_accepted",
                table: "simulation_bids");

            migrationBuilder.DropIndex(
                name: "ix_report_transactions_created",
                table: "post_auction_transactions");

            migrationBuilder.DropIndex(
                name: "ix_report_lots_created",
                table: "lots");

            migrationBuilder.DropIndex(
                name: "ix_report_complaints_created",
                table: "complaints");

            migrationBuilder.DropIndex(
                name: "ix_report_auctions_created",
                table: "auctions");

            migrationBuilder.DropIndex(
                name: "ix_report_pending_registrations",
                table: "auction_registrations");
        }
    }
}
