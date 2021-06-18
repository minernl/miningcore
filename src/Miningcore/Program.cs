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

            var miningCore = new CommandLineApplication(throwOnUnexpectedArg: false)
            {
                Name = "dotnet Miningcore.dll",
                FullName = "MiningCore 2.0 - Stratum Mining Pool Engine",
                Description = "Stratum mining pool engine for Bitcoin and Altcoins",
                ShortVersionGetter = () => $"- MinerNL build v{Assembly.GetEntryAssembly()?.GetName().Version.ToString(2)}",
                LongVersionGetter = () => $"- MinerNL build v{Assembly.GetEntryAssembly()?.GetName().Version}",
                ExtendedHelpText = "--------------------------------------------------------------------------------------------------------------"
            };

            var versionOption = miningCore.Option("-v|--version", "Version Information", CommandOptionType.NoValue);
            var configFileOption = miningCore.Option("-c|--config <configfile>", "Configuration File", CommandOptionType.SingleValue);
            dumpConfigOption = miningCore.Option("-dc|--dumpconfig", "Dump the configuration (useful for trouble-shooting typos in the config file)", CommandOptionType.NoValue);
            shareRecoveryOption = miningCore.Option("-rs", "Import lost shares using existing recovery file", CommandOptionType.SingleValue);
            miningCore.HelpOption("-? | -h | --help");
            miningCore.OnExecute(() =>
           {
               Console.WriteLine("-----------------------------------------------------------------------------------------------------------------------");

               var configFile = "config_template.json";

               if(versionOption.HasValue())
               {
                   miningCore.ShowVersion();
               }

               // overwrite default config_template.json with -c | --config <configfile> file
               if(configFileOption.HasValue())
               {
                   configFile = configFileOption.Value();
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

               if(configFileOption.HasValue())
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
                       miningCore.ShowHelp();
                   }
               }
           });
            miningCore.Execute(args);
        }
    }
}
