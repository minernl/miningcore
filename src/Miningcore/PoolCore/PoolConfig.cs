/*
MiningCore 2.0
Copyright 2021 MinerNL (Miningcore.com)
*/

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Miningcore.Configuration;
using Miningcore.Mining;
using MoreLinq.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Miningcore.PoolCore
{
    public class PoolConfig
    {
        internal const string EnvironmentConfig = "cfg";
        internal const string VaultName = "akv";
        internal const string ConnectionString = "ConnectionString";
        private const string CoinPassword = "coinbasePassword";
        private const string PrivateKey = "privateKey";
        private const string BaseConfigFile = "config.json";
        private static IConfigurationRoot remoteConfig;
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
            GetConfigFromJsonInternal(config);
            ValidateConfig();

            return clusterConfig;
        }

        public static ClusterConfig GetConfigFromAppConfig(string prefix)
        {
            Console.WriteLine("Loading config from app config");
            if(prefix.Trim().Equals("/")) prefix = string.Empty;
            try
            {
                remoteConfig = ReadAppConfig();
                var serializer = JsonSerializer.Create(new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                });
                var reader = new JsonTextReader(new StringReader(remoteConfig[prefix + BaseConfigFile]));
                clusterConfig = serializer.Deserialize<ClusterConfig>(reader);
                // Update dynamic pass and others config here
                clusterConfig.Persistence.Postgres.User = remoteConfig[AppConfigConstants.PersistencePostgresUser];
                clusterConfig.Persistence.Postgres.Password = remoteConfig[AppConfigConstants.PersistencePostgresPassword];
                foreach(var poolConfig in clusterConfig.Pools)
                {
                    poolConfig.PaymentProcessing.Extra[CoinPassword] = remoteConfig[string.Format(AppConfigConstants.CoinBasePassword, poolConfig.Id)];
                    poolConfig.PaymentProcessing.Extra[PrivateKey] = remoteConfig[string.Format(AppConfigConstants.PrivateKey, poolConfig.Id)];
                    poolConfig.EtherScan.apiKey = remoteConfig[string.Format(AppConfigConstants.EtherscanApiKey, poolConfig.Id)];
                    poolConfig.Ports.ForEach(p =>
                    {
                        try
                        {
                            if(!p.Value.Tls) return;
                            var cert = remoteConfig[string.Format(AppConfigConstants.TlsPfxFile, poolConfig.Id, p.Key)];
                            if(cert == null) return;
                            p.Value.TlsPfx = new X509Certificate2(Convert.FromBase64String(cert), (string) null, X509KeyStorageFlags.MachineKeySet);
                            Console.WriteLine("Loaded TLS certificate successfully from App Config...");
                        }
                        catch(Exception ex)
                        {
                            Console.WriteLine($"Failed to load TLS certificate from App Config, Error={ex.Message}");
                        }
                    });
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
            if(prefix.Trim().Equals("/")) prefix = string.Empty;
            remoteConfig = ReadKeyVault(vaultName);
            var secretName = (prefix + BaseConfigFile).Replace(".", "-");
            var config = GetConfigFromJsonInternal(remoteConfig[secretName]);
            foreach(var poolConfig in clusterConfig.Pools)
            {
                poolConfig.Ports.ForEach(p =>
                {
                    try
                    {
                        if(!p.Value.Tls) return;
                        var cert = remoteConfig[p.Value.TlsPfxFile];
                        if(cert == null) return;
                        p.Value.TlsPfx = new X509Certificate2(Convert.FromBase64String(cert), (string) null, X509KeyStorageFlags.MachineKeySet);
                        Console.WriteLine("Loaded TLS certificate successfully from Key Vault...");
                    }
                    catch(Exception ex)
                    {
                        Console.WriteLine($"Failed to load TLS certificate from App Config, Error={ex.Message}");
                    }
                });
            }
            ValidateConfig();

            return config;
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

        private static ClusterConfig GetConfigFromJsonInternal(string config)
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

            return clusterConfig;
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

        private static IConfigurationRoot ReadAppConfig()
        {
            var builder = new ConfigurationBuilder();
            builder.AddAzureAppConfiguration(options => options.Connect(Environment.GetEnvironmentVariable(ConnectionString))
                .ConfigureKeyVault(kv => kv.SetCredential(new DefaultAzureCredential())));

            return builder.Build();
        }

        public class AppConfigConstants
        {
            public const string PersistencePostgresUser = "persistence.postgres.user";
            public static readonly string PersistencePostgresPassword = "persistence.postgres.password";
            public static readonly string CoinBasePassword = "pools.{0}.paymentProcessing.coinbasePassword";
            public static readonly string PrivateKey = "pools.{0}.paymentProcessing.PrivateKey";
            public static readonly string TlsPfxFile = "pools.{0}.{1}.tlsPfxFile";
            public static readonly string EtherscanApiKey = "pools.{0}.etherscan.apiKey";
        }
    }
}
