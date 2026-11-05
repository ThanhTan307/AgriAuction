using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriAuction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompletionCatalogDisputeAndInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_post_history",
                table: "post_auction_history");

            migrationBuilder.DropCheckConstraint(
                name: "ck_complaint_entry",
                table: "complaint_entries");

            migrationBuilder.AddColumn<string>(
                name: "dispute_state",
                table: "post_auction_transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "signaled_at",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "checklist_json",
                table: "lot_reviews",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "complaint_deadline",
                table: "complaints",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "policy_profile",
                table: "complaints",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reason_id",
                table: "complaints",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "responded_at",
                table: "complaints",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "response_due_at",
                table: "complaints",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_id",
                table: "complaint_entries",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateTable(
                name: "workflow_catalog_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_catalog_items", x => x.id);
                    table.CheckConstraint("ck_workflow_catalog", "kind IN ('ComplaintReason','Inspection') AND length(btrim(name)) BETWEEN 1 AND 200 AND version>0");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_post_history",
                table: "post_auction_history",
                sql: "(version = 1 AND kind = 'CreatedFromSimulationResult' AND state IN ('AwaitingPolicy','InProgress')) OR (version > 1 AND actor_id IS NOT NULL AND kind IN ('RequestPayment','ConfirmPayment','Deliver','Receive','Complete','ReportDeliveryIssue','ResolveDeliveryIssue','DisputeOpened','DisputeDecisionRecorded') AND state IN ('InProgress','Completed'))");

            migrationBuilder.CreateIndex(
                name: "IX_complaints_reason_id",
                table: "complaints",
                column: "reason_id");

            migrationBuilder.CreateIndex(
                name: "IX_complaints_state_response_due_at",
                table: "complaints",
                columns: new[] { "state", "response_due_at" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_complaint_entry",
                table: "complaint_entries",
                sql: "version > 0 AND attempts >= 0 AND ((kind IN ('Created','ResponseAdded','ReviewStarted','AdminComment','DecisionRecorded') AND actor_role IN ('Farmer','Enterprise','Administrator') AND actor_id IS NOT NULL) OR (kind='ResponseDeadlineExpired' AND actor_role='System' AND actor_id IS NULL)) AND length(btrim(text)) BETWEEN 1 AND 4000");

            migrationBuilder.AddForeignKey(
                name: "FK_complaints_workflow_catalog_items_reason_id",
                table: "complaints",
                column: "reason_id",
                principalTable: "workflow_catalog_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_complaints_workflow_catalog_items_reason_id",
                table: "complaints");

            migrationBuilder.DropTable(
                name: "workflow_catalog_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_post_history",
                table: "post_auction_history");

            migrationBuilder.DropIndex(
                name: "IX_complaints_reason_id",
                table: "complaints");

            migrationBuilder.DropIndex(
                name: "IX_complaints_state_response_due_at",
                table: "complaints");

            migrationBuilder.DropCheckConstraint(
                name: "ck_complaint_entry",
                table: "complaint_entries");

            migrationBuilder.DropColumn(
                name: "dispute_state",
                table: "post_auction_transactions");

            migrationBuilder.DropColumn(
                name: "signaled_at",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "checklist_json",
                table: "lot_reviews");

            migrationBuilder.DropColumn(
                name: "complaint_deadline",
                table: "complaints");

            migrationBuilder.DropColumn(
                name: "policy_profile",
                table: "complaints");

            migrationBuilder.DropColumn(
                name: "reason_id",
                table: "complaints");

            migrationBuilder.DropColumn(
                name: "responded_at",
                table: "complaints");

            migrationBuilder.DropColumn(
                name: "response_due_at",
                table: "complaints");

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_id",
                table: "complaint_entries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_post_history",
                table: "post_auction_history",
                sql: "(version = 1 AND kind = 'CreatedFromSimulationResult' AND state IN ('AwaitingPolicy','InProgress')) OR (version > 1 AND actor_id IS NOT NULL AND kind IN ('RequestPayment','ConfirmPayment','Deliver','Receive','Complete','ReportDeliveryIssue','ResolveDeliveryIssue') AND state IN ('InProgress','Completed'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_complaint_entry",
                table: "complaint_entries",
                sql: "version > 0 AND attempts >= 0 AND kind IN ('Created','ResponseAdded','ReviewStarted','AdminComment','DecisionRecorded') AND actor_role IN ('Farmer','Enterprise','Administrator') AND length(btrim(text)) BETWEEN 1 AND 4000");
        }
    }
}
