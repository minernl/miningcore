/*
MiningCore 2.0
Copyright 2021 MinerNL (Miningcore.com)
*/

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Miningcore.Configuration;
using Miningcore.Mining;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Miningcore.PoolCore
{
    public class PoolConfig
    {
        internal const string EnvironmentConfig = "cfg";
        internal const string VaultName = "akv";
        private const string BaseConfigFile = "config.json";
        private static ClusterConfig clusterConfig;
        private static readonly Regex RegexJsonTypeConversionError = new Regex("\"([^\"]+)\"[^\']+\'([^\']+)\'.+\\s(\\d+),.+\\s(\\d+)", RegexOptions.Compiled);

        public static ClusterConfig GetConfigFromFile(string configFile)
        {
            // Read config.json file
            clusterConfig = ReadConfig(configFile);
            ValidateConfig();

            return clusterConfig;
        }

        public static ClusterConfig GetConfigFromJson(string config)
        {
            try
            {
                var baseConfig = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(BaseConfigFile));
                var toBeMerged = JObject.Parse(config);
                baseConfig.Merge(toBeMerged, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Merge });
                clusterConfig = baseConfig.ToObject<ClusterConfig>();
            }
            catch(JsonSerializationException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                throw;
            }
            catch(JsonException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                throw;
            }
            catch(IOException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                throw;
            }

            ValidateConfig();

            return clusterConfig;
        }

        public static ClusterConfig GetConfigFromAppConfig(string prefix)
        {
            Console.WriteLine("Loading config from app config");

            if(prefix.Trim().Equals("/")) prefix = string.Empty;

            try
            {
                var config = AzureAppConfiguration.GetAppConfig();
                var serializer = JsonSerializer.Create(new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                });
                var reader = new JsonTextReader(new StringReader(config[prefix + AzureAppConfiguration.ConfigJson]));
                clusterConfig = serializer.Deserialize<ClusterConfig>(reader);
                // Update dynamic pass and others config here
                clusterConfig.Persistence.Postgres.User = config[AzureAppConfiguration.PersistencePostgresUser];
                clusterConfig.Persistence.Postgres.Password = config[AzureAppConfiguration.PersistencePostgresPassword];
                foreach(var poolConfig in clusterConfig.Pools)
                {
                    poolConfig.PaymentProcessing.Extra["coinbasePassword"] = config["pools." + poolConfig.Id + "." + AzureAppConfiguration.CoinbasePassword];
                    poolConfig.PaymentProcessing.Extra["privateKey"] = config["pools." + poolConfig.Id + "." + AzureAppConfiguration.PrivateKey];
                    poolConfig.EtherScan.apiKey = config["pools." + poolConfig.Id + "." + AzureAppConfiguration.EtherscanApiKey];
                }
            }
            catch(JsonSerializationException ex)
            {
                HumanizeJsonParseException(ex);
                throw;
            }
            catch(JsonException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                throw;
            }

            ValidateConfig();

            return clusterConfig;
        }

        public static ClusterConfig GetConfigFromKeyVault(string vaultName, string prefix)
        {
            Console.WriteLine($"Loading config from key vault '{vaultName}'");
            var config = ReadKeyVault(vaultName);
            var secretName = (prefix + BaseConfigFile).Replace(".", "-");
            return GetConfigFromJson(config[secretName]);
        }

        public static void DumpParsedConfig(ClusterConfig config)
        {
            Console.WriteLine("\nCurrent configuration as parsed from config file:");

            Console.WriteLine(JsonConvert.SerializeObject(config, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Formatting = Formatting.Indented
            }));
        }

        private static ClusterConfig ReadConfig(string configFile)
        {
            try
            {
                var serializer = JsonSerializer.Create(new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                });

                using(var reader = new StreamReader(configFile, Encoding.UTF8))
                {
                    using(var jsonReader = new JsonTextReader(reader))
                    {
                        return serializer.Deserialize<ClusterConfig>(jsonReader);
                    }
                }
            }
            catch(JsonSerializationException ex)
            {
                HumanizeJsonParseException(ex);
                throw;
            }
            catch(JsonException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                throw;
            }
            catch(IOException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                throw;
            }
        }

        private static void ValidateConfig()
        {
            // set some defaults
            foreach(var config in clusterConfig.Pools)
            {
                if(!config.EnableInternalStratum.HasValue)
                    config.EnableInternalStratum = clusterConfig.ShareRelays == null || clusterConfig.ShareRelays.Length == 0;
            }
            try
            {
                clusterConfig.Validate();
            }
            catch(ValidationException ex)
            {
                Console.WriteLine($"Configuration is not valid:\n\n{string.Join("\n", ex.Errors.Select(x => "=> " + x.ErrorMessage))}");
                throw new PoolStartupAbortException(string.Empty);
            }
            finally
            {
                Console.WriteLine("Pool Configuration file is valid");
            }
        }

        private static void HumanizeJsonParseException(JsonSerializationException ex)
        {
            var m = RegexJsonTypeConversionError.Match(ex.Message);

            if(m.Success)
            {
                var value = m.Groups[1].Value;
                var type = Type.GetType(m.Groups[2].Value);
                var line = m.Groups[3].Value;
                var col = m.Groups[4].Value;

                if(type == typeof(PayoutScheme))
                    Console.WriteLine($"Error: Payout scheme '{value}' is not (yet) supported (line {line}, column {col})");
            }

            else
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        private static IConfigurationRoot ReadKeyVault(string vaultName)
        {
            var builder = new ConfigurationBuilder();
            builder.AddAzureKeyVault(new SecretClient(new Uri($"https://{vaultName}.vault.azure.net/"), new DefaultAzureCredential()), new KeyVaultSecretManager());

            return builder.Build();
        }
    }
}
