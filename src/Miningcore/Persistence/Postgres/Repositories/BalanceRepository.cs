/*
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Dapper;
using Miningcore.Extensions;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using NLog;

namespace Miningcore.Persistence.Postgres.Repositories
{
    public class BalanceRepository : IBalanceRepository
    {
        public BalanceRepository(IMapper mapper)
        {
            this.mapper = mapper;
        }

        private readonly IMapper mapper;
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        public async Task<int> AddAmountAsync(IDbConnection con, IDbTransaction tx, string poolId, string address, decimal amount, string usage)
        {
            logger.LogInvoke();

            const string query = @"INSERT INTO balances(poolid, address, amount, created, updated)
                    VALUES(@poolId, @address, @amount, @created, @updated)
                    ON CONFLICT (poolid, address)
                    DO UPDATE SET amount = balances.amount + EXCLUDED.amount, updated = EXCLUDED.updated";

            var now = DateTime.UtcNow;

            var balance = new Entities.Balance
            {
                PoolId = poolId,
                Created = now,
                Address = address,
                Amount = amount,
                Updated = now
            };

            return await con.ExecuteAsync(query, balance, tx);
        }

        public async Task<Balance> GetBalanceAsync(IDbConnection con, string poolId, string address)
        {
            logger.LogInvoke();

            const string query = "SELECT * FROM balances WHERE poolid = @poolId AND address = @address";

            return (await con.QueryAsync<Entities.Balance>(query, new { poolId, address }))
                .Select(mapper.Map<Balance>)
                .FirstOrDefault();
        }

        public async Task<Balance> GetBalanceWithPaidDateAsync(IDbConnection con, string poolId, string address)
        {
            logger.LogInvoke();

            const string query = "SELECT b.poolid, b.address, b.amount, b.created, b.updated, p.created AS paiddate FROM balances AS b " +
                                 "LEFT JOIN payments AS p ON  p.address = b.address AND p.poolid = b.poolid " +
                                 "WHERE b.poolid = @poolId AND b.address = @address ORDER BY p.created DESC LIMIT 1";

            return (await con.QueryAsync<Entities.Balance>(query, new { poolId, address }))
                .Select(mapper.Map<Balance>)
                .FirstOrDefault();
        }

        public async Task<Balance[]> GetPoolBalancesOverThresholdAsync(IDbConnection con, string poolId, decimal minimum)
        {
            logger.LogInvoke();

            const string query = "SELECT b.poolid, b.address, b.amount, b.created, b.updated, MAX(p.created) AS paiddate FROM balances AS b " +
                                 "LEFT JOIN payments AS p ON  p.address = b.address AND p.poolid = b.poolid " +
                                 "WHERE b.poolid = @poolId AND b.amount >= @minimum " +
                                 "GROUP BY b.poolid, b.address, b.amount, b.created, b.updated ORDER BY b.amount DESC";

            return (await con.QueryAsync<Entities.Balance>(query, new { poolId, minimum }))
                .Select(mapper.Map<Balance>)
                .ToArray();
        }

        public async Task<Balance[]> GetPoolBalancesOverThresholdAsync(IDbConnection con, string poolId, decimal minimum, int recordLimit)
        {
            logger.LogInvoke();

            const string query = "SELECT b.poolid, b.address, b.amount, b.created, b.updated, MAX(p.created) AS paiddate FROM balances AS b " +
                                 "LEFT JOIN payments AS p ON  p.address = b.address AND p.poolid = b.poolid " +
                                 "WHERE b.poolid = @poolId AND b.amount >= @minimum " +
                                 "GROUP BY b.poolid, b.address, b.amount, b.created, b.updated ORDER BY b.amount DESC LIMIT @recordLimit;";

            return (await con.QueryAsync<Entities.Balance>(query, new { poolId, minimum, recordLimit }))
                .Select(mapper.Map<Balance>)
                .ToArray();
        }

        public async Task<BalanceSummary> GetTotalBalanceSum(IDbConnection con, string poolId, decimal minimum)
        {
            logger.LogInvoke();

            const string query = "SELECT SUM(amount) as TotalAmount, SUM(CASE WHEN amount >= @minimum THEN amount ELSE 0 END) as TotalAmountOverThreshold " +
                                 "FROM balances WHERE poolid = @poolId";

            return (await con.QueryAsync<Entities.BalanceSummary>(query, new { poolId, minimum }))
                .Select(mapper.Map<BalanceSummary>)
                .FirstOrDefault();
        }
    }
}
