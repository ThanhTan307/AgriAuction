namespace AgriAuction.Application.Transactions;

public sealed record CompletionProfile(bool Enabled)
{
    public const string Version = "D35-Simulation-v1";
    public const string PriceBasis = "whole lot simulation";
}
