namespace Miningcore.Persistence.Postgres.Entities
{
    public class BalanceSummary
    {
        public decimal TotalAmount { get; set; }
        public decimal TotalAmountOverThreshold { get; set; }
    }
}
