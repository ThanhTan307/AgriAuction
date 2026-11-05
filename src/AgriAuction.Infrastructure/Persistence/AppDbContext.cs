using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Lots;
using AgriAuction.Domain.Auctions;
using AgriAuction.Domain.Realtime;
using AgriAuction.Domain.Transactions;
using AgriAuction.Domain.Notifications;
using AgriAuction.Domain.Disputes;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace AgriAuction.Infrastructure.Persistence;

public sealed class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ActorId { get; set; }
    public Guid EntityId { get; set; }
    public string Action { get; set; } = "";
    public string? Before { get; set; }
    public string After { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Account> Users => Set<Account>();
    public DbSet<AccountReview> AccountReviews => Set<AccountReview>();
    public DbSet<AccountSanction> AccountSanctions => Set<AccountSanction>();
    public DbSet<LoginSession> Sessions => Set<LoginSession>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<WorkflowCatalogItem> WorkflowCatalogItems => Set<WorkflowCatalogItem>();
    public DbSet<AuditEntry> AuditLogs => Set<AuditEntry>();
    public DbSet<Lot> Lots => Set<Lot>();
    public DbSet<LotRevision> LotRevisions => Set<LotRevision>();
    public DbSet<LotImage> LotImages => Set<LotImage>();
    public DbSet<RevisionImage> RevisionImages => Set<RevisionImage>();
    public DbSet<LotReview> LotReviews => Set<LotReview>();
    public DbSet<Auction> Auctions => Set<Auction>();
    public DbSet<AuctionRegistration> AuctionRegistrations => Set<AuctionRegistration>();
    public DbSet<SimulationDeposit> SimulationDeposits => Set<SimulationDeposit>();
    public DbSet<RegistrationEvent> RegistrationEvents => Set<RegistrationEvent>();
    public DbSet<SimulationLedgerEntry> SimulationLedger => Set<SimulationLedgerEntry>();
    public DbSet<DepositCommand> DepositCommands => Set<DepositCommand>();
    public DbSet<SimulationRoom> SimulationRooms => Set<SimulationRoom>();
    public DbSet<SimulationBid> SimulationBids => Set<SimulationBid>();
    public DbSet<SimulationResult> SimulationResults => Set<SimulationResult>();
    public DbSet<SimulationBidCommand> SimulationBidCommands => Set<SimulationBidCommand>();
    public DbSet<SimulationRoomEvent> SimulationRoomEvents => Set<SimulationRoomEvent>();
    public DbSet<PostAuctionTransaction> PostAuctionTransactions => Set<PostAuctionTransaction>();
    public DbSet<TransactionHistory> TransactionHistory => Set<TransactionHistory>();
    public DbSet<TransactionCommand> TransactionCommands => Set<TransactionCommand>();
    public DbSet<DepositViolationDecision> DepositViolationDecisions => Set<DepositViolationDecision>();
    public DbSet<DepositSettlement> DepositSettlements => Set<DepositSettlement>();
    public DbSet<DepositSettlementCommand> DepositSettlementCommands => Set<DepositSettlementCommand>();
    public DbSet<PostAuctionEvent> PostAuctionEvents => Set<PostAuctionEvent>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Complaint> Complaints => Set<Complaint>();
    public DbSet<ComplaintEntry> ComplaintEntries => Set<ComplaintEntry>();
    public DbSet<ComplaintEvidence> ComplaintEvidence => Set<ComplaintEvidence>();
    public DbSet<ComplaintCommand> ComplaintCommands => Set<ComplaintCommand>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var users = model.Entity<Account>();
        users.ToTable("users", t => t.HasCheckConstraint("ck_users_role", "role IN ('Administrator','Farmer','Enterprise')"));
        users.HasKey(x => x.Id);
        users.Property(x => x.Email).HasMaxLength(254);
        users.Property(x => x.NormalizedEmail).HasMaxLength(254);
        users.HasIndex(x => x.NormalizedEmail).IsUnique();
        users.Property(x => x.DisplayName).HasMaxLength(120);
        users.Property(x => x.Role).HasMaxLength(32);
        users.Property(x => x.ProfileVersion).HasDefaultValue(1L).IsConcurrencyToken();
        users.Property(x => x.ApprovalVersion).HasDefaultValue(1L).IsConcurrencyToken();
        users.Property(x=>x.RestrictionVersion).HasDefaultValue(1L).IsConcurrencyToken();
        var sanctions=model.Entity<AccountSanction>();sanctions.ToTable("account_sanctions",t=>t.HasCheckConstraint("ck_account_sanction","version>1 AND length(btrim(reason)) BETWEEN 1 AND 2000 AND ((kind='SuspendBidding' AND until>occurred_at) OR (kind='LiftBiddingSuspension' AND until IS NULL))"));sanctions.HasKey(x=>x.Id);sanctions.HasIndex(x=>new{x.ActorId,x.RequestId}).IsUnique();sanctions.HasIndex(x=>new{x.AccountId,x.Version}).IsUnique();
        sanctions.HasOne<Account>().WithMany().HasForeignKey(x=>x.AccountId).OnDelete(DeleteBehavior.Restrict);sanctions.HasOne<Account>().WithMany().HasForeignKey(x=>x.ActorId).OnDelete(DeleteBehavior.Restrict);sanctions.HasOne<ComplaintEntry>().WithMany().HasForeignKey(x=>x.DecisionEntryId).OnDelete(DeleteBehavior.Restrict);
        users.Property(x => x.ApprovalState).HasMaxLength(32);
        users.ToTable("users",t=>t.HasCheckConstraint("ck_account_approval","approval_state IS NULL OR approval_state IN ('PendingReview','Approved','Rejected')"));
        var accountReviews=model.Entity<AccountReview>();accountReviews.ToTable("account_reviews",t=>t.HasCheckConstraint("ck_account_review","outcome IN ('Approve','Reject') AND length(btrim(reason)) BETWEEN 1 AND 2000 AND version > 1"));accountReviews.HasKey(x=>x.Id);accountReviews.HasIndex(x=>new{x.AccountId,x.Version}).IsUnique();accountReviews.Property(x=>x.Reason).HasMaxLength(2000);
        accountReviews.HasOne<Account>().WithMany().HasForeignKey(x=>x.AccountId).OnDelete(DeleteBehavior.Restrict);
        accountReviews.HasOne<Account>().WithMany().HasForeignKey(x=>x.ActorId).OnDelete(DeleteBehavior.Restrict);
        var sessions = model.Entity<LoginSession>();
        sessions.ToTable("login_sessions");
        sessions.HasKey(x => x.Id);
        sessions.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        sessions.HasIndex(x => x.ExpiresAt);
        var categories = model.Entity<Category>();
        categories.ToTable("categories", t =>
        {
            t.HasCheckConstraint("ck_category_code", "code ~ '^[A-Z0-9][A-Z0-9_-]{0,31}$'");
            t.HasCheckConstraint("ck_category_name", "length(btrim(name)) BETWEEN 1 AND 120");
            t.HasCheckConstraint("ck_category_version", "version > 0");
        });
        categories.HasKey(x => x.Id);
        var workflowCatalog=model.Entity<WorkflowCatalogItem>();workflowCatalog.ToTable("workflow_catalog_items",t=>t.HasCheckConstraint("ck_workflow_catalog","kind IN ('ComplaintReason','Inspection') AND length(btrim(name)) BETWEEN 1 AND 200 AND version>0"));workflowCatalog.HasKey(x=>x.Id);workflowCatalog.Property(x=>x.Name).HasMaxLength(200);workflowCatalog.Property(x=>x.Version).IsConcurrencyToken();
        categories.Property(x => x.Code).HasMaxLength(32);
        categories.Property(x => x.Name).HasMaxLength(120);
        categories.Property(x => x.Version).IsConcurrencyToken();
        categories.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_categories_code");
        var audit = model.Entity<AuditEntry>();
        audit.ToTable("audit_logs");
        audit.HasKey(x => x.Id);
        audit.Property(x => x.Action).HasMaxLength(64);
        audit.HasIndex(x => new { x.EntityId, x.OccurredAt });
        LotMapping.Configure(model);
        AuctionMapping.Configure(model);
        RoomMapping.Configure(model);
        PostAuctionMapping.Configure(model);
        DepositSettlementMapping.Configure(model);
        ComplaintMapping.Configure(model);
        ReportingMapping.Configure(model);
        foreach (var entity in model.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(entity.IsOwned() && property.IsPrimaryKey() ? "id" : Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
    }
}
