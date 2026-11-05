using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Lots;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriAuction.Infrastructure.Persistence;

internal static class LotMapping
{
    public static void Configure(ModelBuilder model)
    {
        var lots = model.Entity<Lot>();
        lots.ToTable("lots", t =>
        {
            t.HasCheckConstraint("ck_lots_state", "state IN ('Draft','PendingReview','Approved','Rejected')");
            t.HasCheckConstraint("ck_lots_version", "version > 0 AND submission_number >= 0");
            t.HasCheckConstraint("ck_lots_submission", "state = 'Draft' OR (current_revision_id IS NOT NULL AND submitted_at IS NOT NULL)");
            ContentChecks(t, "lot");
        });
        lots.HasKey(x => x.Id);
        lots.Property(x => x.State).HasMaxLength(32);
        lots.Property(x => x.Version).IsConcurrencyToken();
        lots.HasOne<Account>().WithMany().HasForeignKey(x => x.FarmerId).OnDelete(DeleteBehavior.Restrict);
        lots.HasIndex(x => new { x.FarmerId, x.UpdatedAt });
        lots.HasIndex(x => new { x.State, x.SubmittedAt });
        lots.OwnsOne(x => x.Content, Content);
        lots.Navigation(x => x.Content).IsRequired();
        var versions = model.Entity<LotRevision>();
        versions.ToTable("lot_versions", t => { t.HasCheckConstraint("ck_lot_revision_number", "number > 0"); ContentChecks(t, "revision"); });
        versions.HasKey(x => x.Id);
        versions.HasAlternateKey(x => new { x.LotId, x.Id });
        versions.HasIndex(x => new { x.LotId, x.Number }).IsUnique();
        versions.HasOne<Lot>().WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
        versions.OwnsOne(x => x.Content, Content);
        versions.Navigation(x => x.Content).IsRequired();
        lots.HasOne<LotRevision>().WithMany().HasForeignKey(x => new { x.Id, x.CurrentRevisionId })
            .HasPrincipalKey(x => new { x.LotId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        var images = model.Entity<LotImage>();
        images.ToTable("lot_images", t =>
        {
            t.HasCheckConstraint("ck_lot_image_dimensions", "width > 0 AND height > 0 AND width <= 6000 AND height <= 6000 AND width::bigint * height <= 16000000");
            t.HasCheckConstraint("ck_lot_image_size", "octet_length(data) BETWEEN 1 AND 67108864");
        });
        images.HasKey(x => x.Id);
        images.Property(x => x.Sha256).HasMaxLength(64);
        images.HasOne<Lot>().WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
        images.HasOne<Account>().WithMany().HasForeignKey(x => x.UploadedBy).OnDelete(DeleteBehavior.Restrict);
        images.HasIndex(x => new { x.LotId, x.IsRemoved });
        var links = model.Entity<RevisionImage>();
        links.ToTable("lot_revision_images");
        links.HasKey(x => new { x.RevisionId, x.ImageId });
        links.HasOne<LotRevision>().WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);
        links.HasOne<LotImage>().WithMany().HasForeignKey(x => x.ImageId).OnDelete(DeleteBehavior.Restrict);
        var reviews = model.Entity<LotReview>();
        reviews.ToTable("lot_reviews", t =>
        {
            t.HasCheckConstraint("ck_lot_review_outcome", "outcome IN ('Approve','Reject')");
            t.HasCheckConstraint("ck_lot_review_reason", "length(btrim(reason)) BETWEEN 1 AND 2000");
        });
        reviews.HasKey(x => x.Id);
        reviews.HasIndex(x => x.RevisionId).IsUnique();
        reviews.Property(x => x.Outcome).HasMaxLength(16);
        reviews.Property(x => x.Reason).HasMaxLength(2000);
        reviews.HasOne<LotRevision>().WithMany().HasForeignKey(x => new { x.LotId, x.RevisionId })
            .HasPrincipalKey(x => new { x.LotId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        reviews.HasOne<Account>().WithMany().HasForeignKey(x => x.AdminId).OnDelete(DeleteBehavior.Restrict);
    }
    private static void Content<TOwner>(OwnedNavigationBuilder<TOwner, LotContent> content) where TOwner : class
    {
        content.Property(x => x.Title).HasMaxLength(160);
        content.Property(x => x.Description).HasMaxLength(4000);
        content.Property(x => x.Location).HasMaxLength(250);
        content.Property(x => x.QuantityUnit).HasMaxLength(40);
        content.Property(x => x.WeightUnit).HasMaxLength(40);
        content.Property(x => x.Currency).HasMaxLength(3);
        content.Property(x => x.PriceBasis).HasMaxLength(80);
        content.Property(x => x.QualityNotes).HasMaxLength(4000);
        content.Property(x => x.Quantity).HasPrecision(18, 3);
        content.Property(x => x.Weight).HasPrecision(18, 3);
        content.Property(x => x.ProposedStartingPrice).HasPrecision(20, 4);
        content.Property(x => x.ProposedIncrement).HasPrecision(20, 4);
        content.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
    private static void ContentChecks<TOwner>(TableBuilder<TOwner> table, string prefix) where TOwner : class
    {
        table.HasCheckConstraint("ck_" + prefix + "_title", "length(btrim(title)) BETWEEN 1 AND 160");
        table.HasCheckConstraint("ck_" + prefix + "_amounts", "quantity > 0 AND weight > 0 AND proposed_starting_price > 0 AND proposed_increment > 0");
        table.HasCheckConstraint("ck_" + prefix + "_schedule", "proposed_starts_at IS NOT NULL AND proposed_ends_at IS NOT NULL AND proposed_starts_at < proposed_ends_at");
        table.HasCheckConstraint("ck_" + prefix + "_units", "currency ~ '^[A-Z]{3}$' AND length(btrim(price_basis)) > 0 AND length(btrim(quantity_unit)) > 0 AND length(btrim(weight_unit)) > 0");
    }
}
