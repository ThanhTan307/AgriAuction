using AgriAuction.Domain.Auctions;
using AgriAuction.Domain.Identity;
using AgriAuction.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
namespace AgriAuction.Infrastructure.Persistence;

internal static class DepositSettlementMapping
{
    public static void Configure(ModelBuilder model)
    {
        var decisions=model.Entity<DepositViolationDecision>();decisions.ToTable("deposit_violation_decisions",t=>t.HasCheckConstraint("ck_deposit_violation_basis","length(btrim(reason)) BETWEEN 1 AND 2000 AND length(btrim(evidence_reference)) BETWEEN 1 AND 1000"));decisions.HasKey(x=>x.Id);decisions.HasAlternateKey(x=>new{x.DepositId,x.Id});
        decisions.Property(x=>x.Reason).HasMaxLength(2000);decisions.Property(x=>x.EvidenceReference).HasMaxLength(1000);
        decisions.HasOne<SimulationDeposit>().WithMany().HasForeignKey(x=>x.DepositId).OnDelete(DeleteBehavior.Restrict);
        decisions.HasOne<Account>().WithMany().HasForeignKey(x=>x.ActorId).OnDelete(DeleteBehavior.Restrict);
        var entries=model.Entity<DepositSettlement>();entries.ToTable("deposit_settlements",t=>t.HasCheckConstraint("ck_deposit_settlement","amount > 0 AND currency='VND' AND ((kind='SimulatedRefunded' AND decision_id IS NULL) OR (kind='SimulatedForfeited' AND decision_id IS NOT NULL))"));entries.HasKey(x=>x.Id);entries.HasIndex(x=>x.DepositId).IsUnique();entries.Property(x=>x.Amount).HasPrecision(20,4);
        entries.HasOne<SimulationDeposit>().WithMany().HasForeignKey(x=>x.DepositId).OnDelete(DeleteBehavior.Restrict);
        entries.HasOne<Account>().WithMany().HasForeignKey(x=>x.ActorId).OnDelete(DeleteBehavior.Restrict);
        entries.HasOne<DepositViolationDecision>().WithMany().HasForeignKey(x=>new{x.DepositId,x.DecisionId}).HasPrincipalKey(x=>new{x.DepositId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        var commands=model.Entity<DepositSettlementCommand>();commands.ToTable("deposit_settlement_commands");commands.HasKey(x=>x.Id);commands.HasIndex(x=>new{x.ActorId,x.RequestId}).IsUnique();commands.Property(x=>x.ReceiptJson).HasColumnType("jsonb");commands.Property(x=>x.PayloadHash).HasMaxLength(64);
        commands.HasOne<SimulationDeposit>().WithMany().HasForeignKey(x=>x.DepositId).OnDelete(DeleteBehavior.Restrict);
        commands.HasOne<Account>().WithMany().HasForeignKey(x=>x.ActorId).OnDelete(DeleteBehavior.Restrict);
    }
}
