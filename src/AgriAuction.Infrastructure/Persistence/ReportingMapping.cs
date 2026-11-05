using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Lots;
using AgriAuction.Domain.Auctions;
using AgriAuction.Domain.Realtime;
using AgriAuction.Domain.Transactions;
using AgriAuction.Domain.Disputes;
using Microsoft.EntityFrameworkCore;

namespace AgriAuction.Infrastructure.Persistence;

internal static class ReportingMapping
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<Account>().HasIndex(x => x.CreatedAt).HasDatabaseName("ix_report_accounts_created");
        model.Entity<Lot>().HasIndex(x => new { x.CreatedAt, x.Id }).HasDatabaseName("ix_report_lots_created");
        model.Entity<Auction>().HasIndex(x => new { x.CreatedAt, x.Id }).HasDatabaseName("ix_report_auctions_created");
        model.Entity<SimulationRoom>().HasIndex(x => new { x.ConfiguredAt, x.Id }).HasDatabaseName("ix_report_rooms_configured");
        model.Entity<SimulationBid>().HasIndex(x => x.AcceptedAt).HasDatabaseName("ix_report_bids_accepted");
        model.Entity<SimulationResult>().HasIndex(x => x.ClosedAt).HasDatabaseName("ix_report_results_closed");
        model.Entity<PostAuctionTransaction>().HasIndex(x => new { x.CreatedAt, x.Id }).HasDatabaseName("ix_report_transactions_created");
        model.Entity<Complaint>().HasIndex(x => new { x.CreatedAt, x.Id }).HasDatabaseName("ix_report_complaints_created");
        model.Entity<AuctionRegistration>().HasIndex(x => new { x.State, x.RegisteredAt }).HasDatabaseName("ix_report_pending_registrations");
    }
}
