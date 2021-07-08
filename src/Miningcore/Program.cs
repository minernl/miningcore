using McMaster.Extensions.CommandLineUtils;
using Miningcore.Configuration;
using Miningcore.PoolCore;
using System;
using System.Reflection;

namespace Miningcore
{
    public class Program
    {
        private const string EnvironmentConfig = "cfg";
        private static CommandOption dumpConfigOption;
        private static CommandOption shareRecoveryOption;
        private static ClusterConfig clusterConfig;

        public static void Main(string[] args)
        {
            PoolLogo.Logo();

            var MiningCore = new CommandLineApplication(throwOnUnexpectedArg: false)
            {
                Name = "dotnet Miningcore.dll",
                FullName = "MiningCore 2.0 - Stratum Mining Pool Engine",
                Description = "Stratum mining pool engine for Bitcoin and Altcoins",
                ShortVersionGetter = () => $"- MinerNL build v{Assembly.GetEntryAssembly()?.GetName().Version.ToString(2)}",
                LongVersionGetter = () => $"- MinerNL build v{Assembly.GetEntryAssembly()?.GetName().Version}",
                ExtendedHelpText = "--------------------------------------------------------------------------------------------------------------"
            };

            var versionOption = MiningCore.Option("-v|--version", "Version Information", CommandOptionType.NoValue);
            var configFileOption = MiningCore.Option("-c|--config <configfile>", "Configuration File", CommandOptionType.SingleValue);
            var appConfigPrefixOption = MiningCore.Option("-ac|--appconfig <prefix>", "Azure App Configuration Prefix", CommandOptionType.SingleValue);
            dumpConfigOption = MiningCore.Option("-dc|--dumpconfig", "Dump the configuration (useful for trouble-shooting typos in the config file)", CommandOptionType.NoValue);
            shareRecoveryOption = MiningCore.Option("-rs", "Import lost shares using existing recovery file", CommandOptionType.SingleValue);
            MiningCore.HelpOption("-? | -h | --help");
            MiningCore.OnExecute(() =>
            {
                Console.WriteLine("-----------------------------------------------------------------------------------------------------------------------");

                var configFile = "config_template.json";
                var appConfigPrefix = "/";

                if(versionOption.HasValue())
                {
                    MiningCore.ShowVersion();
                }
                // overwrite default config_template.json with -c | --config <configfile> file
                if(configFileOption.HasValue())
                {
                    configFile = configFileOption.Value();
                }
                if(appConfigPrefixOption.HasValue())
                {
                    appConfigPrefix = appConfigPrefixOption.Value();
                }
                // Dump Config to JSON output
                if(dumpConfigOption.HasValue())
                {
                    clusterConfig = PoolCore.PoolConfig.GetConfigContent(configFile);
                    PoolCore.PoolConfig.DumpParsedConfig(clusterConfig);
                }
                // Shares recovery from file to database
                if(shareRecoveryOption.HasValue())
                {
                    Pool.RecoverSharesAsync(shareRecoveryOption.Value()).Wait();
                }

                if(appConfigPrefixOption.HasValue())
                {
                    Pool.StartMiningCorePoolWithAppConfig(appConfigPrefix);
                }
                else if(configFileOption.HasValue())
                {
                    // Start Miningcore PoolCore
                    Pool.StartMiningCorePool(configFile);
                }
                else
                {
                    var envConfig = Environment.GetEnvironmentVariable(EnvironmentConfig);
                    if(!string.IsNullOrEmpty(envConfig))
                    {
                        // Start Miningcore PoolCore
                        Pool.StartMiningCorePoolWithJson(envConfig);
                    }
                    else
                    {
                        MiningCore.ShowHelp();
                    }
                }
            });
            MiningCore.Execute(args);
        }
    }
}
