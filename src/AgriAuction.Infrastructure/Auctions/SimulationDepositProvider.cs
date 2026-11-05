using AgriAuction.Application.Auctions;

namespace AgriAuction.Infrastructure.Auctions;

public sealed class SimulationDepositProvider : IDepositSimulationProvider
{
    public SimulationReceipt Confirm(SimulationInstruction instruction)
        => new("Simulation", "SIM-" + instruction.DepositId.ToString("N"));
}
