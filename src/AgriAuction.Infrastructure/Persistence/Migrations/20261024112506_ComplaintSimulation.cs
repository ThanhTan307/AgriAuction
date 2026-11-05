using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriAuction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComplaintSimulation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_notifications_post_auction_events_event_id",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_event_id",
                table: "notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_simulation",
                table: "notifications");

            migrationBuilder.AddColumn<Guid>(
                name: "complaint_id",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "complaint_source_id",
                table: "notifications",
                type: "uuid",
                nullable: true,
                computedColumnSql: "CASE WHEN kind IN ('ComplaintCreated','ComplaintResponded','ComplaintStatusChanged','ComplaintCommented','ComplaintDecisionRecorded') THEN event_id END",
                stored: true);

            migrationBuilder.AddColumn<Guid>(
                name: "post_auction_source_id",
                table: "notifications",
                type: "uuid",
                nullable: true,
                computedColumnSql: "CASE WHEN kind IN ('WinnerRecorded','LotSoldSimulation','TransactionCreated','AuctionClosedNoWinner') THEN event_id END",
                stored: true);

            migrationBuilder.CreateTable(
                name: "complaints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    complainant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    respondent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_complaints", x => x.id);
                    table.CheckConstraint("ck_complaint_simulation", "mode = 'Simulation' AND scope IN ('AuctionRecord','TransactionRecord') AND complainant_id <> respondent_id AND version > 0 AND updated_at >= created_at");
                    table.CheckConstraint("ck_complaint_state", "(state IN ('Open','UnderReview') AND outcome IS NULL) OR (state = 'MediationPending' AND outcome IS NOT NULL AND outcome = 'Mediate') OR (state = 'DecisionRecorded' AND outcome IS NOT NULL AND outcome IN ('Accept','Reject'))");
                    table.CheckConstraint("ck_complaint_text", "length(btrim(reason)) BETWEEN 1 AND 200 AND length(btrim(description)) BETWEEN 1 AND 4000");
                    table.ForeignKey(
                        name: "FK_complaints_post_auction_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalTable: "post_auction_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_complaints_users_complainant_id",
                        column: x => x.complainant_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_complaints_users_respondent_id",
                        column: x => x.respondent_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "complaint_commands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    complaint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    receipt_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_complaint_commands", x => x.id);
                    table.ForeignKey(
                        name: "FK_complaint_commands_complaints_complaint_id",
                        column: x => x.complaint_id,
                        principalTable: "complaints",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_complaint_commands_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "complaint_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    complaint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recipient_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_error_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_complaint_entries", x => x.id);
                    table.UniqueConstraint("AK_complaint_entries_complaint_id_id", x => new { x.complaint_id, x.id });
                    table.CheckConstraint("ck_complaint_entry", "version > 0 AND attempts >= 0 AND kind IN ('Created','ResponseAdded','ReviewStarted','AdminComment','DecisionRecorded') AND actor_role IN ('Farmer','Enterprise','Administrator') AND length(btrim(text)) BETWEEN 1 AND 4000");
                    table.CheckConstraint("ck_complaint_entry_decision", "(kind = 'DecisionRecorded' AND outcome IS NOT NULL AND outcome IN ('Accept','Reject','Mediate')) OR (kind <> 'DecisionRecorded' AND outcome IS NULL)");
                    table.ForeignKey(
                        name: "FK_complaint_entries_complaints_complaint_id",
                        column: x => x.complaint_id,
                        principalTable: "complaints",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_complaint_entries_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "complaint_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    complaint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    server_file_name = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_complaint_evidence", x => x.id);
                    table.CheckConstraint("ck_complaint_evidence", "width > 0 AND height > 0 AND width <= 6000 AND height <= 6000 AND width::bigint * height <= 16000000 AND octet_length(data) BETWEEN 1 AND 5242880 AND sha256 ~ '^[A-F0-9]{64}$' AND server_file_name ~ '^[a-f0-9]{32}[.]png$'");
                    table.ForeignKey(
                        name: "FK_complaint_evidence_complaint_entries_complaint_id_entry_id",
                        columns: x => new { x.complaint_id, x.entry_id },
                        principalTable: "complaint_entries",
                        principalColumns: new[] { "complaint_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_complaint_evidence_users_uploaded_by",
                        column: x => x.uploaded_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_complaint_id_complaint_source_id",
                table: "notifications",
                columns: new[] { "complaint_id", "complaint_source_id" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_post_auction_source_id",
                table: "notifications",
                column: "post_auction_source_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_simulation",
                table: "notifications",
                sql: "mode = 'Simulation' AND ((kind IN ('WinnerRecorded','LotSoldSimulation','TransactionCreated','AuctionClosedNoWinner') AND complaint_id IS NULL) OR (kind IN ('ComplaintCreated','ComplaintResponded','ComplaintStatusChanged','ComplaintCommented','ComplaintDecisionRecorded') AND complaint_id IS NOT NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_complaint_commands_actor_id_request_id",
                table: "complaint_commands",
                columns: new[] { "actor_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_complaint_commands_complaint_id",
                table: "complaint_commands",
                column: "complaint_id");

            migrationBuilder.CreateIndex(
                name: "IX_complaint_entries_actor_id",
                table: "complaint_entries",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "IX_complaint_entries_complaint_id",
                table: "complaint_entries",
                column: "complaint_id",
                unique: true,
                filter: "kind = 'DecisionRecorded'");

            migrationBuilder.CreateIndex(
                name: "IX_complaint_entries_complaint_id_version",
                table: "complaint_entries",
                columns: new[] { "complaint_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_complaint_entries_notified_at_next_attempt_at",
                table: "complaint_entries",
                columns: new[] { "notified_at", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_complaint_evidence_complaint_id_entry_id",
                table: "complaint_evidence",
                columns: new[] { "complaint_id", "entry_id" });

            migrationBuilder.CreateIndex(
                name: "IX_complaint_evidence_uploaded_by",
                table: "complaint_evidence",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "IX_complaints_complainant_id_created_at",
                table: "complaints",
                columns: new[] { "complainant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_complaints_respondent_id_created_at",
                table: "complaints",
                columns: new[] { "respondent_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_complaints_state_created_at_id",
                table: "complaints",
                columns: new[] { "state", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_complaints_active_transaction",
                table: "complaints",
                column: "transaction_id",
                unique: true,
                filter: "state <> 'DecisionRecorded'");

            migrationBuilder.AddForeignKey(
                name: "FK_notifications_complaint_entries_complaint_id_complaint_sour~",
                table: "notifications",
                columns: new[] { "complaint_id", "complaint_source_id" },
                principalTable: "complaint_entries",
                principalColumns: new[] { "complaint_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_notifications_post_auction_events_post_auction_source_id",
                table: "notifications",
                column: "post_auction_source_id",
                principalTable: "post_auction_events",
                principalColumn: "room_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_notifications_complaint_entries_complaint_id_complaint_sour~",
                table: "notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_notifications_post_auction_events_post_auction_source_id",
                table: "notifications");

            migrationBuilder.DropTable(
                name: "complaint_commands");

            migrationBuilder.DropTable(
                name: "complaint_evidence");

            migrationBuilder.DropTable(
                name: "complaint_entries");

            migrationBuilder.DropTable(
                name: "complaints");

            migrationBuilder.DropIndex(
                name: "IX_notifications_complaint_id_complaint_source_id",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_post_auction_source_id",
                table: "notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_simulation",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "complaint_source_id",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "post_auction_source_id",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "complaint_id",
                table: "notifications");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_event_id",
                table: "notifications",
                column: "event_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_simulation",
                table: "notifications",
                sql: "mode = 'Simulation' AND kind IN ('WinnerRecorded','LotSoldSimulation','TransactionCreated','AuctionClosedNoWinner')");

            migrationBuilder.AddForeignKey(
                name: "FK_notifications_post_auction_events_event_id",
                table: "notifications",
                column: "event_id",
                principalTable: "post_auction_events",
                principalColumn: "room_id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
