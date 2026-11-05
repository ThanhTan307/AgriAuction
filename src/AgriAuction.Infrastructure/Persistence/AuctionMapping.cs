using AgriAuction.Domain.Auctions;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Lots;
using Microsoft.EntityFrameworkCore;

namespace AgriAuction.Infrastructure.Persistence;

internal static class AuctionMapping
{
    public static void Configure(ModelBuilder model)
    {
        var auctions = model.Entity<Auction>();
        auctions.ToTable("auctions", t =>
        {
            t.HasCheckConstraint("ck_auction_simulation", "is_simulation AND state IN ('Draft','Scheduled') AND version > 0");
            t.HasCheckConstraint("ck_auction_publication", "(state = 'Draft' AND published_at IS NULL AND published_by IS NULL) OR (state = 'Scheduled' AND published_at IS NOT NULL AND published_by IS NOT NULL)");
            t.HasCheckConstraint("ck_auction_schedule", "registration_opens_at < registration_closes_at AND registration_closes_at <= starts_at AND starts_at < ends_at");
            t.HasCheckConstraint("ck_auction_amount", "deposit_amount > 0 AND currency ~ '^[A-Z]{3}$'");
        });
        auctions.HasKey(x => x.Id);
        auctions.Property(x => x.State).HasMaxLength(32);
        auctions.Property(x => x.Version).IsConcurrencyToken();
        auctions.HasIndex(x => x.LotId).IsUnique().HasDatabaseName("ux_auctions_lot");
        auctions.HasIndex(x => new { x.State, x.CreatedAt });
        auctions.HasOne<LotRevision>().WithMany().HasForeignKey(x => new { x.LotId, x.RevisionId }).HasPrincipalKey(x => new { x.LotId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        auctions.HasOne<Account>().WithMany().HasForeignKey(x => x.FarmerId).OnDelete(DeleteBehavior.Restrict);
        auctions.HasOne<Account>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        auctions.HasOne<Account>().WithMany().HasForeignKey(x => x.PublishedBy).OnDelete(DeleteBehavior.Restrict);
        auctions.OwnsOne(x => x.Terms, terms => { terms.Property(x => x.DepositAmount).HasPrecision(20, 4); terms.Property(x => x.Currency).HasMaxLength(3); });
        auctions.Navigation(x => x.Terms).IsRequired();

        var registrations = model.Entity<AuctionRegistration>();
        registrations.ToTable("auction_registrations", t =>
        {
            t.HasCheckConstraint("ck_registration_state", "(state = 'PendingDeposit' AND eligible_at IS NULL AND version = 1) OR (state = 'SimulationEligible' AND eligible_at IS NOT NULL AND version = 2)");
        });
        registrations.HasKey(x => x.Id);
        registrations.Property(x => x.State).HasMaxLength(32);
        registrations.Property(x => x.Version).IsConcurrencyToken();
        registrations.HasIndex(x => new { x.AuctionId, x.EnterpriseId }).IsUnique().HasDatabaseName("ux_registrations_auction_enterprise");
        registrations.HasOne<Auction>().WithMany().HasForeignKey(x => x.AuctionId).OnDelete(DeleteBehavior.Restrict);
        registrations.HasOne<Account>().WithMany().HasForeignKey(x => x.EnterpriseId).OnDelete(DeleteBehavior.Restrict);

        var deposits = model.Entity<SimulationDeposit>();
        deposits.ToTable("simulation_deposits", t =>
        {
            t.HasCheckConstraint("ck_simulation_deposit", "mode = 'Simulation' AND amount > 0 AND currency ~ '^[A-Z]{3}$'");
            t.HasCheckConstraint("ck_simulation_deposit_state", "(state = 'AwaitingSimulation' AND confirmed_at IS NULL AND confirmed_by IS NULL) OR (state = 'SimulatedHeld' AND confirmed_at IS NOT NULL AND confirmed_by IS NOT NULL)");
        });
        deposits.HasKey(x => x.Id);
        deposits.HasIndex(x => x.RegistrationId).IsUnique();
        deposits.Property(x => x.Mode).HasMaxLength(16); deposits.Property(x => x.State).HasMaxLength(32);
        deposits.Property(x => x.Amount).HasPrecision(20, 4); deposits.Property(x => x.Currency).HasMaxLength(3);
        deposits.HasOne<AuctionRegistration>().WithMany().HasForeignKey(x => x.RegistrationId).OnDelete(DeleteBehavior.Restrict);
        deposits.HasOne<Account>().WithMany().HasForeignKey(x => x.ConfirmedBy).OnDelete(DeleteBehavior.Restrict);

        var events = model.Entity<RegistrationEvent>();
        events.ToTable("registration_events", t => t.HasCheckConstraint("ck_registration_event_state", "state IN ('PendingDeposit','SimulationEligible')"));
        events.HasKey(x => x.Id); events.Property(x => x.State).HasMaxLength(32);
        events.HasIndex(x => new { x.RegistrationId, x.State }).IsUnique();
        events.HasOne<AuctionRegistration>().WithMany().HasForeignKey(x => x.RegistrationId).OnDelete(DeleteBehavior.Restrict);
        events.HasOne<Account>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);

        var ledger = model.Entity<SimulationLedgerEntry>();
        ledger.ToTable("simulation_ledger", t => t.HasCheckConstraint("ck_simulation_ledger", "kind = 'SimulatedConfirmation' AND provider = 'Simulation' AND amount > 0 AND currency ~ '^[A-Z]{3}$'"));
        ledger.HasKey(x => x.Id); ledger.HasIndex(x => x.DepositId).IsUnique();
        ledger.HasIndex(x => x.Reference).IsUnique();
        ledger.Property(x => x.Amount).HasPrecision(20, 4); ledger.Property(x => x.Currency).HasMaxLength(3);
        ledger.Property(x => x.Kind).HasMaxLength(32); ledger.Property(x => x.Provider).HasMaxLength(32); ledger.Property(x => x.Reference).HasMaxLength(80);
        ledger.HasOne<SimulationDeposit>().WithMany().HasForeignKey(x => x.DepositId).OnDelete(DeleteBehavior.Restrict);
        ledger.HasOne<Account>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);

        var commands = model.Entity<DepositCommand>();
        commands.ToTable("simulation_deposit_commands"); commands.HasKey(x => x.Id);
        commands.HasIndex(x => new { x.ActorId, x.RequestId }).IsUnique().HasDatabaseName("ux_simulation_commands_actor_request");
        commands.Property(x => x.PayloadHash).HasMaxLength(64);
        commands.HasOne<Account>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        commands.HasOne<AuctionRegistration>().WithMany().HasForeignKey(x => x.RegistrationId).OnDelete(DeleteBehavior.Restrict);
    }
}
