using AgriAuction.Domain.Auctions;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Realtime;
using Microsoft.EntityFrameworkCore;

namespace AgriAuction.Infrastructure.Persistence;

internal static class RoomMapping
{
    public static void Configure(ModelBuilder model)
    {
        var rooms = model.Entity<SimulationRoom>();
        rooms.ToTable("simulation_rooms", t =>
        {
            t.HasCheckConstraint("ck_room_simulation", "mode = 'Simulation' AND state IN ('Prepared','Open','ClosedWithWinner','ClosedNoWinner') AND version > 0");
            t.HasCheckConstraint("ck_room_schedule", "starts_at < initial_ends_at AND initial_ends_at <= ends_at");
            t.HasCheckConstraint("ck_room_lifecycle", "(state = 'Prepared' AND opened_at IS NULL AND closed_at IS NULL AND started_by IS NULL) OR (state = 'Open' AND opened_at IS NOT NULL AND started_by IS NOT NULL AND closed_at IS NULL) OR (state IN ('ClosedWithWinner','ClosedNoWinner') AND opened_at IS NOT NULL AND started_by IS NOT NULL AND closed_at IS NOT NULL AND closed_at >= ends_at)");
            t.HasCheckConstraint("ck_room_leader", "(bid_count = 0 AND current_price IS NULL AND leading_bid_id IS NULL AND leading_enterprise_id IS NULL AND state <> 'ClosedWithWinner') OR (bid_count > 0 AND current_price IS NOT NULL AND current_price > 0 AND leading_bid_id IS NOT NULL AND leading_enterprise_id IS NOT NULL AND state <> 'ClosedNoWinner')");
            t.HasCheckConstraint("ck_room_rules", "starting_price > 0 AND minimum_increment > 0 AND currency ~ '^[A-Z]{3}$' AND length(btrim(price_basis)) BETWEEN 1 AND 80 AND first_bid_rule = 'AtLeastStartingPrice' AND increment_rule IN ('MinimumIncrease','StartingPriceGrid') AND tie_rule = 'FirstCommittedWins' AND close_rule = 'AtOrAfterDeadline'");
            t.HasCheckConstraint("ck_room_extension", "extension_count BETWEEN 0 AND max_extensions AND max_extensions BETWEEN 0 AND 1000 AND ((extension_rule = 'Disabled' AND extension_window_seconds = 0 AND extension_seconds = 0 AND max_extensions = 0) OR (extension_rule IN ('FromBidTime','AddToDeadline') AND extension_window_seconds BETWEEN 1 AND 86400 AND extension_seconds BETWEEN 1 AND 86400 AND max_extensions > 0))");
        });
        rooms.HasKey(x => x.Id); rooms.HasIndex(x => x.AuctionId).IsUnique(); rooms.HasIndex(x => new { x.State, x.EndsAt });
        rooms.Property(x => x.State).HasMaxLength(32); rooms.Property(x => x.Mode).HasMaxLength(16); rooms.Property(x => x.Version).IsConcurrencyToken(); rooms.Property(x => x.CurrentPrice).HasPrecision(20, 4);
        rooms.HasOne<Auction>().WithMany().HasForeignKey(x => x.AuctionId).OnDelete(DeleteBehavior.Restrict);
        rooms.HasOne<Account>().WithMany().HasForeignKey(x => x.ConfiguredBy).OnDelete(DeleteBehavior.Restrict);
        rooms.HasOne<Account>().WithMany().HasForeignKey(x => x.StartedBy).OnDelete(DeleteBehavior.Restrict);
        rooms.HasOne<Account>().WithMany().HasForeignKey(x => x.LeadingEnterpriseId).OnDelete(DeleteBehavior.Restrict);
        rooms.OwnsOne(x => x.Rules, r =>
        {
            r.Property(x => x.StartingPrice).HasPrecision(20, 4); r.Property(x => x.MinimumIncrement).HasPrecision(20, 4);
            r.Property(x => x.Currency).HasMaxLength(3); r.Property(x => x.PriceBasis).HasMaxLength(80);
            foreach (var name in new[] { "FirstBidRule", "IncrementRule", "TieRule", "ExtensionRule", "CloseRule" }) r.Property<string>(name).HasMaxLength(32);
            r.Property(x => x.ExtensionWindowSeconds).IsRequired(); r.Property(x => x.ExtensionSeconds).IsRequired(); r.Property(x => x.MaxExtensions).IsRequired();
        });
        rooms.Navigation(x => x.Rules).IsRequired(); rooms.Ignore(x => x.MinimumBid);
        var bids = model.Entity<SimulationBid>(); bids.ToTable("simulation_bids", t => t.HasCheckConstraint("ck_simulation_bid", "amount > 0 AND room_version > 2 AND accepted_at < previous_ends_at AND previous_ends_at <= ends_at AND extended = (previous_ends_at < ends_at)"));
        bids.HasKey(x => x.Id); bids.HasAlternateKey(x => new { x.RoomId, x.Id }); bids.HasIndex(x => new { x.RoomId, x.RoomVersion }).IsUnique(); bids.Property(x => x.Amount).HasPrecision(20, 4);
        bids.HasOne<SimulationRoom>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict); bids.HasOne<Account>().WithMany().HasForeignKey(x => x.EnterpriseId).OnDelete(DeleteBehavior.Restrict);
        rooms.HasOne<SimulationBid>().WithMany().HasForeignKey(x => new { x.Id, x.LeadingBidId }).HasPrincipalKey(x => new { x.RoomId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        var results = model.Entity<SimulationResult>(); results.ToTable("simulation_results", t => t.HasCheckConstraint("ck_simulation_result", "mode = 'Simulation' AND room_version > 2 AND ((winning_bid_id IS NULL AND winner_id IS NULL AND amount IS NULL) OR (winning_bid_id IS NOT NULL AND winner_id IS NOT NULL AND amount IS NOT NULL AND amount > 0))"));
        results.HasKey(x => x.RoomId); results.Property(x => x.Amount).HasPrecision(20, 4); results.Property(x => x.Mode).HasMaxLength(16);
        results.HasOne<SimulationRoom>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
        results.HasOne<SimulationBid>().WithMany().HasForeignKey(x => new { x.RoomId, x.WinningBidId }).HasPrincipalKey(x => new { x.RoomId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        results.HasOne<Account>().WithMany().HasForeignKey(x => x.WinnerId).OnDelete(DeleteBehavior.Restrict);
        var commands = model.Entity<SimulationBidCommand>(); commands.ToTable("simulation_bid_commands"); commands.HasKey(x => x.Id);
        commands.HasIndex(x => new { x.ActorId, x.RequestId }).IsUnique(); commands.Property(x => x.PayloadHash).HasMaxLength(64);
        commands.HasOne<Account>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict); commands.HasOne<SimulationRoom>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
        var events = model.Entity<SimulationRoomEvent>(); events.ToTable("simulation_room_events", t => t.HasCheckConstraint("ck_room_event", "kind IN ('RoomConfigured','RoomOpened','BidAccepted','RoomClosed') AND version > 0 AND attempts >= 0"));
        events.HasKey(x => x.Id); events.HasIndex(x => new { x.RoomId, x.Version }).IsUnique(); events.HasIndex(x => new { x.PublishedAt, x.NextAttemptAt }); events.Property(x => x.Kind).HasMaxLength(32);
        events.HasOne<SimulationRoom>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict); events.HasOne<Account>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
    }
}
