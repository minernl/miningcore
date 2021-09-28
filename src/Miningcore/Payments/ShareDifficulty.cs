using System.Collections.Generic;

namespace Miningcore.Payments
{
    public class ShareDifficulty
    {
        public double Difficulty { get; set; }

        public List<Persistence.Model.Share> Shares { get; set; }
    }
}
