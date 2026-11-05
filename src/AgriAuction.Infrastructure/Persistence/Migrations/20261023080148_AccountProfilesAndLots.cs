using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriAuction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountProfilesAndLots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "profile_version",
                table: "users",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateTable(
                name: "lot_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    is_removed = table.Column<bool>(type: "boolean", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lot_images", x => x.id);
                    table.CheckConstraint("ck_lot_image_dimensions", "width > 0 AND height > 0 AND width <= 6000 AND height <= 6000 AND width::bigint * height <= 16000000");
                    table.CheckConstraint("ck_lot_image_size", "octet_length(data) BETWEEN 1 AND 67108864");
                    table.ForeignKey(
                        name: "FK_lot_images_users_uploaded_by",
                        column: x => x.uploaded_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lot_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lot_reviews", x => x.id);
                    table.CheckConstraint("ck_lot_review_outcome", "outcome IN ('Approve','Reject')");
                    table.CheckConstraint("ck_lot_review_reason", "length(btrim(reason)) BETWEEN 1 AND 2000");
                    table.ForeignKey(
                        name: "FK_lot_reviews_users_admin_id",
                        column: x => x.admin_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lot_revision_images",
                columns: table => new
                {
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    image_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lot_revision_images", x => new { x.revision_id, x.image_id });
                    table.ForeignKey(
                        name: "FK_lot_revision_images_lot_images_image_id",
                        column: x => x.image_id,
                        principalTable: "lot_images",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lot_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    location = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    quantity_unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    weight = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    weight_unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    proposed_starting_price = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    proposed_increment = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    price_basis = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    proposed_starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    proposed_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    quality_notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lot_versions", x => x.id);
                    table.UniqueConstraint("AK_lot_versions_lot_id_id", x => new { x.lot_id, x.id });
                    table.CheckConstraint("ck_lot_revision_number", "number > 0");
                    table.CheckConstraint("ck_revision_amounts", "quantity > 0 AND weight > 0 AND proposed_starting_price > 0 AND proposed_increment > 0");
                    table.CheckConstraint("ck_revision_schedule", "proposed_starts_at IS NOT NULL AND proposed_ends_at IS NOT NULL AND proposed_starts_at < proposed_ends_at");
                    table.CheckConstraint("ck_revision_title", "length(btrim(title)) BETWEEN 1 AND 160");
                    table.CheckConstraint("ck_revision_units", "currency ~ '^[A-Z]{3}$' AND length(btrim(price_basis)) > 0 AND length(btrim(quantity_unit)) > 0 AND length(btrim(weight_unit)) > 0");
                    table.ForeignKey(
                        name: "FK_lot_versions_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    farmer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    location = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    quantity_unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    weight = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    weight_unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    proposed_starting_price = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    proposed_increment = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    price_basis = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    proposed_starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    proposed_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    quality_notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    submission_number = table.Column<int>(type: "integer", nullable: false),
                    current_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lots", x => x.id);
                    table.CheckConstraint("ck_lot_amounts", "quantity > 0 AND weight > 0 AND proposed_starting_price > 0 AND proposed_increment > 0");
                    table.CheckConstraint("ck_lot_schedule", "proposed_starts_at IS NOT NULL AND proposed_ends_at IS NOT NULL AND proposed_starts_at < proposed_ends_at");
                    table.CheckConstraint("ck_lot_title", "length(btrim(title)) BETWEEN 1 AND 160");
                    table.CheckConstraint("ck_lot_units", "currency ~ '^[A-Z]{3}$' AND length(btrim(price_basis)) > 0 AND length(btrim(quantity_unit)) > 0 AND length(btrim(weight_unit)) > 0");
                    table.CheckConstraint("ck_lots_state", "state IN ('Draft','PendingReview','Approved','Rejected')");
                    table.CheckConstraint("ck_lots_submission", "state = 'Draft' OR (current_revision_id IS NOT NULL AND submitted_at IS NOT NULL)");
                    table.CheckConstraint("ck_lots_version", "version > 0 AND submission_number >= 0");
                    table.ForeignKey(
                        name: "FK_lots_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lots_lot_versions_id_current_revision_id",
                        columns: x => new { x.id, x.current_revision_id },
                        principalTable: "lot_versions",
                        principalColumns: new[] { "lot_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lots_users_farmer_id",
                        column: x => x.farmer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lot_images_lot_id_is_removed",
                table: "lot_images",
                columns: new[] { "lot_id", "is_removed" });

            migrationBuilder.CreateIndex(
                name: "IX_lot_images_uploaded_by",
                table: "lot_images",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "IX_lot_reviews_admin_id",
                table: "lot_reviews",
                column: "admin_id");

            migrationBuilder.CreateIndex(
                name: "IX_lot_reviews_lot_id_revision_id",
                table: "lot_reviews",
                columns: new[] { "lot_id", "revision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_lot_reviews_revision_id",
                table: "lot_reviews",
                column: "revision_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lot_revision_images_image_id",
                table: "lot_revision_images",
                column: "image_id");

            migrationBuilder.CreateIndex(
                name: "IX_lot_versions_category_id",
                table: "lot_versions",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_lot_versions_lot_id_number",
                table: "lot_versions",
                columns: new[] { "lot_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lots_category_id",
                table: "lots",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_lots_farmer_id_updated_at",
                table: "lots",
                columns: new[] { "farmer_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_lots_id_current_revision_id",
                table: "lots",
                columns: new[] { "id", "current_revision_id" });

            migrationBuilder.CreateIndex(
                name: "IX_lots_state_submitted_at",
                table: "lots",
                columns: new[] { "state", "submitted_at" });

            migrationBuilder.AddForeignKey(
                name: "FK_lot_images_lots_lot_id",
                table: "lot_images",
                column: "lot_id",
                principalTable: "lots",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_lot_reviews_lot_versions_lot_id_revision_id",
                table: "lot_reviews",
                columns: new[] { "lot_id", "revision_id" },
                principalTable: "lot_versions",
                principalColumns: new[] { "lot_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_lot_revision_images_lot_versions_revision_id",
                table: "lot_revision_images",
                column: "revision_id",
                principalTable: "lot_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_lot_versions_lots_lot_id",
                table: "lot_versions",
                column: "lot_id",
                principalTable: "lots",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_lot_versions_lots_lot_id",
                table: "lot_versions");

            migrationBuilder.DropTable(
                name: "lot_reviews");

            migrationBuilder.DropTable(
                name: "lot_revision_images");

            migrationBuilder.DropTable(
                name: "lot_images");

            migrationBuilder.DropTable(
                name: "lots");

            migrationBuilder.DropTable(
                name: "lot_versions");

            migrationBuilder.DropColumn(
                name: "profile_version",
                table: "users");
        }
    }
}
