using AgriAuction.Domain.Disputes;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace AgriAuction.Infrastructure.Persistence;

internal static class ComplaintMapping
{
    public static void Configure(ModelBuilder model)
    {
        var cases = model.Entity<Complaint>();
        cases.ToTable("complaints", t =>
        {
            t.HasCheckConstraint("ck_complaint_simulation", "mode = 'Simulation' AND scope IN ('AuctionRecord','TransactionRecord') AND complainant_id <> respondent_id AND version > 0 AND updated_at >= created_at");
            t.HasCheckConstraint("ck_complaint_state", "(state IN ('Open','UnderReview') AND outcome IS NULL) OR (state = 'MediationPending' AND outcome IS NOT NULL AND outcome = 'Mediate') OR (state = 'DecisionRecorded' AND outcome IS NOT NULL AND outcome IN ('Accept','Reject'))");
            t.HasCheckConstraint("ck_complaint_text", "length(btrim(reason)) BETWEEN 1 AND 200 AND length(btrim(description)) BETWEEN 1 AND 4000");
            t.HasCheckConstraint("ck_completion_complaint_deadlines","policy_profile IS NULL OR (policy_profile='D35-Simulation-v1' AND reason_id IS NOT NULL AND complaint_deadline>=created_at AND response_due_at=created_at+interval '3 days' AND (responded_at IS NULL OR responded_at<response_due_at))");
        });
        cases.HasKey(x => x.Id);
        cases.HasIndex(x => x.TransactionId).IsUnique().HasFilter("state <> 'DecisionRecorded'").HasDatabaseName("ux_complaints_active_transaction");
        cases.HasIndex(x => new { x.ComplainantId, x.CreatedAt }); cases.HasIndex(x => new { x.RespondentId, x.CreatedAt });
        cases.HasIndex(x => new { x.State, x.CreatedAt, x.Id });
        cases.Property(x => x.Mode).HasMaxLength(16); cases.Property(x => x.Scope).HasMaxLength(32);
        cases.Property(x => x.State).HasMaxLength(32); cases.Property(x => x.Outcome).HasMaxLength(16);
        cases.Property(x => x.Title).HasMaxLength(200); cases.Property(x => x.Reason).HasMaxLength(200); cases.Property(x => x.Description).HasMaxLength(4000);
        cases.Property(x => x.Version).IsConcurrencyToken();
        cases.HasOne<AgriAuction.Domain.Catalog.WorkflowCatalogItem>().WithMany().HasForeignKey(x=>x.ReasonId).OnDelete(DeleteBehavior.Restrict);
        cases.HasIndex(x=>new{x.State,x.ResponseDueAt});
        cases.HasOne<PostAuctionTransaction>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Restrict);
        cases.HasOne<Account>().WithMany().HasForeignKey(x => x.ComplainantId).OnDelete(DeleteBehavior.Restrict);
        cases.HasOne<Account>().WithMany().HasForeignKey(x => x.RespondentId).OnDelete(DeleteBehavior.Restrict);
        var entries = model.Entity<ComplaintEntry>();
        entries.ToTable("complaint_entries", t =>
        {
            t.HasCheckConstraint("ck_complaint_entry", "version > 0 AND attempts >= 0 AND ((kind IN ('Created','ResponseAdded','ReviewStarted','AdminComment','DecisionRecorded') AND actor_role IN ('Farmer','Enterprise','Administrator') AND actor_id IS NOT NULL) OR (kind='ResponseDeadlineExpired' AND actor_role='System' AND actor_id IS NULL)) AND length(btrim(text)) BETWEEN 1 AND 4000");
            t.HasCheckConstraint("ck_complaint_entry_decision", "(kind = 'DecisionRecorded' AND outcome IS NOT NULL AND outcome IN ('Accept','Reject','Mediate')) OR (kind <> 'DecisionRecorded' AND outcome IS NULL)");
        });
        entries.HasKey(x => x.Id); entries.HasAlternateKey(x => new { x.ComplaintId, x.Id });
        entries.HasIndex(x => new { x.ComplaintId, x.Version }).IsUnique();
        entries.HasIndex(x => x.ComplaintId).IsUnique().HasFilter("kind = 'DecisionRecorded'");
        entries.HasIndex(x => new { x.NotifiedAt, x.NextAttemptAt });
        entries.Property(x => x.ActorRole).HasMaxLength(32); entries.Property(x => x.Kind).HasMaxLength(32);
        entries.Property(x => x.State).HasMaxLength(32); entries.Property(x => x.Outcome).HasMaxLength(16);
        entries.Property(x => x.Text).HasMaxLength(4000); entries.Property(x => x.LastErrorType).HasMaxLength(120);
        entries.HasOne<Complaint>().WithMany().HasForeignKey(x => x.ComplaintId).OnDelete(DeleteBehavior.Restrict);
        entries.HasOne<Account>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        var evidence = model.Entity<ComplaintEvidence>();
        evidence.ToTable("complaint_evidence", t => t.HasCheckConstraint("ck_complaint_evidence", "width > 0 AND height > 0 AND width <= 6000 AND height <= 6000 AND width::bigint * height <= 16000000 AND octet_length(data) BETWEEN 1 AND 5242880 AND sha256 ~ '^[A-F0-9]{64}$' AND server_file_name ~ '^[a-f0-9]{32}[.]png$'"));
        evidence.HasKey(x => x.Id); evidence.Property(x => x.Sha256).HasMaxLength(64); evidence.Property(x => x.ServerFileName).HasMaxLength(36);
        evidence.HasOne<ComplaintEntry>().WithMany().HasForeignKey(x => new { x.ComplaintId, x.EntryId }).HasPrincipalKey(x => new { x.ComplaintId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        evidence.HasOne<Account>().WithMany().HasForeignKey(x => x.UploadedBy).OnDelete(DeleteBehavior.Restrict);
        var commands = model.Entity<ComplaintCommand>();
        commands.ToTable("complaint_commands"); commands.HasKey(x => x.Id);
        commands.HasIndex(x => new { x.ActorId, x.RequestId }).IsUnique();
        commands.Property(x => x.PayloadHash).HasMaxLength(64); commands.Property(x => x.ReceiptJson).HasColumnType("jsonb");
        commands.HasOne<Complaint>().WithMany().HasForeignKey(x => x.ComplaintId).OnDelete(DeleteBehavior.Restrict);
        commands.HasOne<Account>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
    }
}
