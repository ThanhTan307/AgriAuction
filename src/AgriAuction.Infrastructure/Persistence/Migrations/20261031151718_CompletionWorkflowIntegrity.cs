using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriAuction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompletionWorkflowIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_completion_delivery",
                table: "post_auction_transactions",
                sql: "policy_profile IS NULL OR ((delivery_state NOT IN ('Delivered','Received') OR delivered_at IS NOT NULL) AND (delivery_state<>'Received' OR (received_at IS NOT NULL AND received_at>=delivered_at)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_completion_complaint_deadlines",
                table: "complaints",
                sql: "policy_profile IS NULL OR (policy_profile='D35-Simulation-v1' AND reason_id IS NOT NULL AND complaint_deadline>=created_at AND response_due_at=created_at+interval '3 days' AND (responded_at IS NULL OR responded_at<response_due_at))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_completion_delivery",
                table: "post_auction_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_completion_complaint_deadlines",
                table: "complaints");
        }
    }
}
