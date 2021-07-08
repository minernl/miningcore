using System;
using Microsoft.Extensions.Configuration;
using Azure.Identity;

namespace Miningcore.PoolCore
{
    public class AzureAppConfiguration
    {
        public static readonly string  ConfigJson = "config.json";
        public static readonly string PersistencePostgresUser = "persistence.postgres.user";
        public static readonly string PersistencePostgresPassword = "persistence.postgres.password";
        public static readonly string CoinbasePassword = "paymentProcessing.coinbasePassword";
        public static readonly string ConnectionString = "ConnectionString";

        public static IConfigurationRoot  GetAppConfig(string prefix) {

                var builder = new ConfigurationBuilder();
                builder.AddAzureAppConfiguration(options => {
                        options.Connect(Environment.GetEnvironmentVariable(ConnectionString))
                        .ConfigureKeyVault(kv =>
                        {
                            kv.SetCredential(new DefaultAzureCredential());
                        })
                        .TrimKeyPrefix(prefix);
                    });

                return builder.Build();
        }

    }
}