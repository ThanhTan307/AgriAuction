using AgriAuction.Domain.Auctions;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Lots;
using AgriAuction.Domain.Notifications;
using AgriAuction.Domain.Realtime;
using AgriAuction.Domain.Transactions;
using AgriAuction.Domain.Disputes;
using Microsoft.EntityFrameworkCore;

namespace AgriAuction.Infrastructure.Persistence;

internal static class PostAuctionMapping
{
    public static void Configure(ModelBuilder model)
    {
        var events = model.Entity<PostAuctionEvent>();
        events.ToTable("post_auction_events", t => t.HasCheckConstraint("ck_post_event_attempts", "attempts >= 0"));
        events.HasKey(x => x.RoomId);
        events.HasOne<SimulationResult>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
        events.Property(x => x.LastErrorType).HasMaxLength(120);
        events.HasIndex(x => new { x.ProcessedAt, x.NextAttemptAt });
        var trades = model.Entity<PostAuctionTransaction>();
        trades.ToTable("post_auction_transactions", t =>
        {
            t.HasCheckConstraint("ck_post_transaction_simulation", "mode = 'Simulation' AND ((policy_profile IS NULL AND state = 'AwaitingPolicy' AND payment_state = 'PolicyLocked' AND delivery_state = 'PolicyLocked' AND version = 1 AND completed_at IS NULL AND payment_requested_at IS NULL AND payment_confirmed_at IS NULL AND delivered_at IS NULL AND received_at IS NULL) OR (policy_profile = 'D35-Simulation-v1' AND currency = 'VND' AND price_basis = 'whole lot simulation' AND version >= 1 AND state IN ('InProgress','Completed') AND payment_state IN ('AwaitingRequest','AwaitingConfirmation','SimulatedConfirmed') AND delivery_state IN ('AwaitingDelivery','Delivered','Received','IssueReported')))");
            t.HasCheckConstraint("ck_transaction_completion", "(state = 'Completed') = (completed_at IS NOT NULL) AND (state <> 'Completed' OR (payment_state = 'SimulatedConfirmed' AND delivery_state = 'Received' AND completed_at >= payment_confirmed_at AND completed_at >= received_at))");
            t.HasCheckConstraint("ck_transaction_payment", "(payment_state IN ('AwaitingConfirmation','SimulatedConfirmed')) = (payment_requested_at IS NOT NULL) AND (payment_state = 'SimulatedConfirmed') = (payment_confirmed_at IS NOT NULL) AND (payment_confirmed_at IS NULL OR payment_confirmed_at >= payment_requested_at)");
            t.HasCheckConstraint("ck_completion_delivery","policy_profile IS NULL OR ((delivery_state NOT IN ('Delivered','Received') OR delivered_at IS NOT NULL) AND (delivery_state<>'Received' OR (received_at IS NOT NULL AND received_at>=delivered_at)))");
            t.HasCheckConstraint("ck_post_transaction_result", "winning_price > 0 AND result_version > 3 AND farmer_id <> enterprise_id AND closed_at <= created_at");
            t.HasCheckConstraint("ck_post_transaction_currency", "currency ~ '^[A-Z]{3}$' AND length(btrim(price_basis)) BETWEEN 1 AND 80");
        });
        trades.HasKey(x => x.Id); trades.HasIndex(x => x.RoomId).IsUnique(); trades.HasIndex(x => x.AuctionId).IsUnique();
        trades.HasIndex(x => new { x.FarmerId, x.CreatedAt }); trades.HasIndex(x => new { x.EnterpriseId, x.CreatedAt });
        trades.Property(x => x.WinningPrice).HasPrecision(20, 4); trades.Property(x => x.Currency).HasMaxLength(3);
        trades.Property(x => x.PriceBasis).HasMaxLength(80); trades.Property(x => x.Title).HasMaxLength(200);
        trades.Property(x => x.SnapshotJson).HasColumnType("jsonb"); trades.Property(x => x.Version).IsConcurrencyToken();
        foreach (var name in new[] { "Mode", "State", "PaymentState", "DeliveryState" }) trades.Property<string>(name).HasMaxLength(32);
        trades.HasOne<SimulationResult>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
        trades.HasOne<SimulationBid>().WithMany().HasForeignKey(x => new { x.RoomId, x.WinningBidId }).HasPrincipalKey(x => new { x.RoomId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        trades.HasOne<Auction>().WithMany().HasForeignKey(x => x.AuctionId).OnDelete(DeleteBehavior.Restrict);
        trades.HasOne<Lot>().WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
        trades.HasOne<LotRevision>().WithMany().HasForeignKey(x => new { x.LotId, x.RevisionId }).HasPrincipalKey(x => new { x.LotId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        trades.HasOne<Account>().WithMany().HasForeignKey(x => x.FarmerId).OnDelete(DeleteBehavior.Restrict);
        trades.HasOne<Account>().WithMany().HasForeignKey(x => x.EnterpriseId).OnDelete(DeleteBehavior.Restrict);
        var history = model.Entity<TransactionHistory>();
        history.ToTable("post_auction_history", t => t.HasCheckConstraint("ck_post_history", "(version = 1 AND kind = 'CreatedFromSimulationResult' AND state IN ('AwaitingPolicy','InProgress')) OR (version > 1 AND actor_id IS NOT NULL AND kind IN ('RequestPayment','ConfirmPayment','Deliver','Receive','Complete','ReportDeliveryIssue','ResolveDeliveryIssue','DisputeOpened','DisputeDecisionRecorded') AND state IN ('InProgress','Completed'))"));
        history.HasKey(x => x.Id); history.HasIndex(x => new { x.TransactionId, x.Version }).IsUnique();
        history.Property(x => x.Kind).HasMaxLength(40); history.Property(x => x.State).HasMaxLength(32);
        history.HasOne<PostAuctionTransaction>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Restrict);
        history.Property(x => x.Note).HasMaxLength(1000);
        history.HasOne<Account>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        var commands = model.Entity<TransactionCommand>();
        commands.ToTable("transaction_commands"); commands.HasKey(x => x.Id);
        commands.HasAlternateKey(x => new { x.TransactionId, x.Id });
        commands.HasIndex(x => new { x.ActorId, x.RequestId }).IsUnique();
        commands.Property(x => x.PayloadHash).HasMaxLength(64);
        commands.Property(x => x.ReceiptJson).HasColumnType("jsonb");
        commands.HasOne<PostAuctionTransaction>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Restrict);
        commands.HasOne<Account>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        var notifications = model.Entity<Notification>();
        notifications.ToTable("notifications", t =>
        {
            t.HasCheckConstraint("ck_notification_simulation", "mode = 'Simulation' AND ((kind IN ('WinnerRecorded','LotSoldSimulation','TransactionCreated','AuctionClosedNoWinner','DepositSettlementRecorded') AND complaint_id IS NULL) OR (kind IN ('ComplaintCreated','ComplaintResponded','ComplaintStatusChanged','ComplaintCommented','ComplaintDecisionRecorded') AND complaint_id IS NOT NULL) OR (kind = 'TransactionActionRecorded' AND transaction_id IS NOT NULL AND complaint_id IS NULL))");
            t.HasCheckConstraint("ck_notification_read", "read_at IS NULL OR read_at >= created_at");
        });
        notifications.HasKey(x => x.Id); notifications.HasIndex(x => new { x.RecipientId, x.EventId, x.Kind }).IsUnique();
        notifications.HasIndex(x => new { x.RecipientId, x.CreatedAt, x.Id }); notifications.HasIndex(x => new { x.RecipientId, x.ReadAt });
        notifications.Property(x => x.Kind).HasMaxLength(40); notifications.Property(x => x.Mode).HasMaxLength(16);
        notifications.Property(x => x.Title).HasMaxLength(200); notifications.Property(x => x.Message).HasMaxLength(1000);
        notifications.HasOne<Account>().WithMany().HasForeignKey(x => x.RecipientId).OnDelete(DeleteBehavior.Restrict);
        // Keep both source FKs: generated columns let existing Phase 5 producers and
        // inserts retain their original contract while Complaint events use the same inbox.
        notifications.Property(x => x.PostAuctionSourceId).HasComputedColumnSql("CASE WHEN kind IN ('WinnerRecorded','LotSoldSimulation','TransactionCreated','AuctionClosedNoWinner') THEN event_id END", stored: true);
        notifications.Property(x => x.ComplaintSourceId).HasComputedColumnSql("CASE WHEN kind IN ('ComplaintCreated','ComplaintResponded','ComplaintStatusChanged','ComplaintCommented','ComplaintDecisionRecorded') THEN event_id END", stored: true);
        notifications.Property(x => x.TransactionSourceId).HasComputedColumnSql("CASE WHEN kind = 'TransactionActionRecorded' THEN event_id END", stored: true);
        notifications.Property(x => x.DepositSourceId).HasComputedColumnSql("CASE WHEN kind = 'DepositSettlementRecorded' THEN event_id END", stored: true);
        notifications.HasOne<DepositSettlementCommand>().WithMany().HasForeignKey(x => x.DepositSourceId).OnDelete(DeleteBehavior.Restrict);
        notifications.HasOne<TransactionCommand>().WithMany().HasForeignKey(x => new { x.TransactionId, x.TransactionSourceId }).HasPrincipalKey(x => new { x.TransactionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        notifications.HasOne<PostAuctionEvent>().WithMany().HasForeignKey(x => x.PostAuctionSourceId).OnDelete(DeleteBehavior.Restrict);
        notifications.HasOne<ComplaintEntry>().WithMany().HasForeignKey(x => new { x.ComplaintId, x.ComplaintSourceId }).HasPrincipalKey(x => new { x.ComplaintId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        notifications.HasOne<Auction>().WithMany().HasForeignKey(x => x.AuctionId).OnDelete(DeleteBehavior.Restrict);
        notifications.HasOne<PostAuctionTransaction>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Restrict);
    }
}
