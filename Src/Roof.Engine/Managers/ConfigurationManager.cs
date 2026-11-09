using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Roof.Engine.Tools;
using Roof.Common.Tools;

//typedef std::string RoofClientID
using RoofClientID = string;

namespace Roof.Engine.Managers
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(RoofConfiguration))]
    public partial class RoofConfigurationJSONContext : JsonSerializerContext
    {
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(RoofClientConfig))]
    public partial class RoofClientConfigJSONContext : JsonSerializerContext
    {
    }

    public class RoofClientConfig
    {
        public int AccessCount { get; set; }
        public string OSSystemUsername { get; set; }
    }

    public partial class RoofConfiguration
    {
        ///////////RAW///////////
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Sha256FileHash { get; set; }
        ///////////RAW///////////

        ///////////ENCRYPTED///////////
        public int SchemaVersion { get; set; }
        public string RoofIdentifier { get; set; }
        public bool HasCompletedInitialSetup { get; set; }
        public Dictionary<RoofClientID, RoofClientConfig> RegisteredClients { get; set; }
        ///////////ENCRYPTED///////////
    }

    public class ConfigurationManager
    {
        internal const int CURRENT_CONFIG_FILE_SCHEMA_VERSION = 2;

        private static string _configurationEncryptionKeyRoot = "/etc/Roof";
        internal static string _configurationEncryptionKeyDirectory = $"/etc/Roof/keys/";
        private static string _configurationEncryptionKeyFilePath =  _configurationEncryptionKeyDirectory + "roof_config.key";
        private static string _configurationFileDirectory = $"{Directory.GetCurrentDirectory()}/roof_config/";
        private static string _configurationFilePath = _configurationFileDirectory + "config.json";

        internal static string ClientKeyPath(string clientID)
        {
            if (!SecurityManager.IsValidClientID(clientID))
                throw new ArgumentException("Invalid client ID.");

            return SafePath.CanonicalizeUnder(_configurationEncryptionKeyDirectory + $"{clientID}.key", _configurationEncryptionKeyDirectory);
        }

        private static RoofConfiguration? _loadedConfiguration;

        private static async Task WriteEncryptionKey(string path, string nek, string niv, bool overwrite)
        {
            byte[] keyBytes = Convert.FromBase64String(nek);
            byte[] ivBytes = Convert.FromBase64String(niv);
            if (keyBytes.Length != 32 || ivBytes.Length != 16)
                throw new ArgumentException("Encryption keys require a 32-byte key and a 16-byte IV.");

            var safeKeyDir = SafePath.CanonicalizeUnder(_configurationEncryptionKeyDirectory, _configurationEncryptionKeyRoot);
            var safeKeyFile = SafePath.CanonicalizeUnder(path, safeKeyDir);
            Directory.CreateDirectory(safeKeyDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            string directoryResult = await Shell.ExecuteAsync(
                $"sudo chown root:root -- {SafePath.ShellQuote(safeKeyDir)} && sudo chmod 700 -- {SafePath.ShellQuote(safeKeyDir)}");
            if (!string.IsNullOrEmpty(directoryResult))
                throw new IOException("Failed to secure encryption key directory: " + directoryResult);

            // Write privately, then replace atomically so failed rotation leaves the old key intact.
            string temporaryPath = safeKeyFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var stream = new FileStream(temporaryPath, new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                }))
                {
                    await stream.WriteAsync(keyBytes.Concat(ivBytes).ToArray());
                }

                string permissionResult = await Shell.ExecuteAsync(
                    $"sudo chown root:root -- {SafePath.ShellQuote(temporaryPath)} && sudo chmod 600 -- {SafePath.ShellQuote(temporaryPath)}");
                if (!string.IsNullOrEmpty(permissionResult))
                    throw new IOException("Failed to secure encryption key file: " + permissionResult);

                SafePath.CanonicalizeUnder(safeKeyFile, safeKeyDir);
                File.Move(temporaryPath, safeKeyFile, overwrite);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private static async Task<bool> CreateConfigEncryptionKey()
        {
            try
            {
                var encryptionKey = Security.AES256.GenerateAESIVAndKey();
                await WriteEncryptionKey(_configurationEncryptionKeyFilePath, encryptionKey.Item2, encryptionKey.Item1, false);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile("[ConfigurationManager]: Failed to create configuration encryption key.", ex);
                return false;
            }
        }

        private static async Task<bool> CreateRoofClientNetworkEncryptionKey(string clientID, string nek, string niv)
        {
            var path = ClientKeyPath(clientID);
            if (File.Exists(path))
            {
                // A retry must not silently accept an unrelated or damaged existing key.
                var expected = Convert.FromBase64String(nek).Concat(Convert.FromBase64String(niv)).ToArray();
                var existing = await File.ReadAllBytesAsync(path);
                if (Convert.FromBase64String(nek).Length != 32 || Convert.FromBase64String(niv).Length != 16 ||
                    !existing.SequenceEqual(expected))
                    return false;
            }

            return await SaveRoofClientNetworkEncryptionKey(clientID, nek, niv);
        }

        public static async Task<bool> SaveRoofClientNetworkEncryptionKey(string clientID, string nek, string niv)
        {
            try
            {
                await WriteEncryptionKey(ClientKeyPath(clientID), nek, niv, true);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile("[ConfigurationManager]: Failed to save roof client network encryption key.", ex);
                return false;
            }
        }

        public static async Task<bool> Initialize()
        {
            try
            {
                if (!Directory.Exists(_configurationFileDirectory))
                {
                    Logger.LogToFile("[ConfigurationManager]: Creating configuration directory...");
                    Directory.CreateDirectory(_configurationFileDirectory);
                }

                if (!File.Exists(_configurationEncryptionKeyFilePath))
                {
                    if (File.Exists(_configurationFilePath))
                    {
                        Logger.LogToFile("[ConfigurationManager]: Existing configuration is missing its encryption key.", LogSeverity.Danger);
                        return false;
                    }

                    Logger.LogToFile("[ConfigurationManager]: Creating configuration encryption key...");
                    bool cfgEncKeyCreateStatus = await CreateConfigEncryptionKey();

                    if (!cfgEncKeyCreateStatus)
                    {
                        Logger.LogToFile("[ConfigurationManager]: Failed to create configuration encryption key.", LogSeverity.Danger);
                        return false;
                    }
                }

                if (!File.Exists(_configurationFilePath))
                {
                    Logger.LogToFile("[ConfigurationManager]: Creating default configuration file...");
                    return await CreateDefaultConfigurationFile();
                }
                else
                {
                    Logger.LogToFile("[ConfigurationManager]: Loading configuration file...");
                    var loadConfResult = await LoadConfiguration();

                    if (loadConfResult != null)
                    {
                        Logger.LogToFile("[ConfigurationManager]: Loaded configuration file successfully.");
                        return true;
                    }
                    else
                    {
                        Logger.LogToFile("[ConfigurationManager]: Failed to load configuration file.", LogSeverity.Danger);
                        return false;
                    }
                }
            }
            catch(Exception ex)
            {
                Logger.LogToFile($"[ConfigurationManager]: Exception occured while trying to initialize. Exception: {ex.Message}", LogSeverity.Danger);
                return false;
            }
        }

        internal static Task<bool> SaveLoadedConfiguration()
        {
            return _loadedConfiguration == null ? Task.FromResult(true) : SaveConfiguration(_loadedConfiguration);
        }

        public static RoofConfiguration GetConfiguration()
        {
            if (_loadedConfiguration == null)
            {
                Logger.LogToFile("[ConfigurationManager] - GetConfiguration(): Manager is not initialized properly.", LogSeverity.Danger);
                return new RoofConfiguration();
            }

            return _loadedConfiguration;
        }

        public static async Task<bool> SaveConfiguration(RoofConfiguration? configuration)
        {
            try
            {
                if (configuration == null)
                {
                    Logger.LogToFile("[ConfigurationManager] - SaveConfiguration(): Manager is not initialized properly.", LogSeverity.Danger);
                    return false;
                }

                //Compute hash
                configuration.Sha256FileHash = ComputeConfigurationIntegrityHash(configuration);

                //Serialise
                string serialisedJsonConfig = JsonSerializer.Serialize(
                    configuration,
                    RoofConfigurationJSONContext.Default.RoofConfiguration
                );

                if (!File.Exists(_configurationEncryptionKeyFilePath))
                {
                    Logger.LogToFile("[ConfigurationManager] - SaveConfiguration(): Failed to find config encryption key.", LogSeverity.Danger);
                    return false;
                }
                
                var readConfigEncryptionKeyContent = await File.ReadAllBytesAsync(_configurationEncryptionKeyFilePath);
                
                if (readConfigEncryptionKeyContent.Length != 48)
                {
                    Logger.LogToFile("[ConfigurationManager] - SaveConfiguration(): Config encryption key file is invalid.", LogSeverity.Danger);
                    return false;
                }
                
                byte[] readCfgEncKey = readConfigEncryptionKeyContent.Take(32).ToArray();  // AES-256 = 32 bytes
                byte[] readCfgEncIV = readConfigEncryptionKeyContent.Skip(32).Take(16).ToArray(); // AES block size = 16 bytes

                string loadedCfgEncKey = Convert.ToBase64String(readCfgEncKey);
                string loadedCfgEncIV = Convert.ToBase64String(readCfgEncIV);
                
                byte[] encryptedConfig = Security.AES256.ManagedNetworkEncrypt(loadedCfgEncKey, loadedCfgEncIV, serialisedJsonConfig);
                
                string temporaryPath = _configurationFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllBytesAsync(temporaryPath, encryptedConfig);
                    File.Move(temporaryPath, _configurationFilePath, true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                _loadedConfiguration = configuration;
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogToFile($"[ConfigurationManager] - SaveConfiguration(): Exception occurred while trying to save configuration. Exception: {ex.Message}", LogSeverity.Danger);
                return false;
            }
        }

        private static async Task<RoofConfiguration?> LoadConfiguration()
        {
            try
            {
                var readEncryptedConfigFileContent = await File.ReadAllBytesAsync(_configurationFilePath);

                if (!File.Exists(_configurationEncryptionKeyFilePath))
                {
                    Logger.LogToFile("[ConfigurationManager] - LoadConfiguration(): Failed to find config encryption key.", LogSeverity.Danger);
                    return null;
                }
                
                var readConfigEncryptionKeyContent = await File.ReadAllBytesAsync(_configurationEncryptionKeyFilePath);
                
                if (readConfigEncryptionKeyContent.Length != 48)
                {
                    Logger.LogToFile("[ConfigurationManager] - LoadConfiguration(): Config encryption key file is invalid.", LogSeverity.Danger);
                    return null;
                }
                
                byte[] readCfgEncKey = readConfigEncryptionKeyContent.Take(32).ToArray();  // AES-256 = 32 bytes
                byte[] readCfgEncIV = readConfigEncryptionKeyContent.Skip(32).Take(16).ToArray(); // AES block size = 16 bytes

                string loadedCfgEncKey = Convert.ToBase64String(readCfgEncKey);
                string loadedCfgEncIV = Convert.ToBase64String(readCfgEncIV);
                
                string configFileContent = Security.AES256.ManagedNetworkDecrypt(loadedCfgEncKey, loadedCfgEncIV, readEncryptedConfigFileContent, readEncryptedConfigFileContent.Length);

                if (!configFileContent.IsStringJson())
                {
                    Logger.LogToFile("[ConfigurationManager] -  LoadConfiguration(): Failed to decrypt config file.", LogSeverity.Danger);
                    return null;
                }
                
                RoofConfiguration? deserialisedConfig = JsonSerializer.Deserialize<RoofConfiguration>(configFileContent, RoofConfigurationJSONContext.Default.RoofConfiguration);

                if(deserialisedConfig == null)
                {
                    Logger.LogToFile($"[ConfigurationManager] - LoadConfiguration(): deserialisedConfig returned null.");
                    return null;
                }
                
                //Compute runtime hash and check...
                string? originalHash = deserialisedConfig.Sha256FileHash;
                if (string.IsNullOrWhiteSpace(originalHash))
                {
                    Logger.LogToFile("[ConfigurationManager] - LoadConfiguration(): Missing stored config integrity hash.", LogSeverity.Danger);
                    return null;
                }
                
                string computedRuntimeHash = deserialisedConfig.SchemaVersion < CURRENT_CONFIG_FILE_SCHEMA_VERSION
                    ? ComputeLegacyConfigurationIntegrityHash(configFileContent)
                    : ComputeConfigurationIntegrityHash(deserialisedConfig);
                if (!computedRuntimeHash.Equals(originalHash))
                {
                    Logger.LogToFile($"[ConfigurationManager] - LoadConfiguration(): A mismatch has been detected between the runtime hash and the stored hash. Startup blocked. Manual recovery required. No replacement config was generated.", LogSeverity.Danger);
                    return null;
                }
                
                if (deserialisedConfig.SchemaVersion < CURRENT_CONFIG_FILE_SCHEMA_VERSION)
                {
                    Logger.LogToFile($"[ConfigurationManager] - LoadConfiguration(): Migrating/re-encrypting configuration to version: {CURRENT_CONFIG_FILE_SCHEMA_VERSION}");
                    
                    deserialisedConfig = await Migrate(configFileContent, deserialisedConfig);
                    deserialisedConfig.SchemaVersion = CURRENT_CONFIG_FILE_SCHEMA_VERSION;

                    // After migration, persist upgraded file
                    if (!await SaveConfiguration(deserialisedConfig))
                        return null;
                }
                
                _loadedConfiguration = deserialisedConfig;
                return _loadedConfiguration;
            }
            catch (Exception ex)
            {
                Logger.LogToFile($"[ConfigurationManager] - LoadConfiguration(): Exception occurred while trying to load configuration. Exception: {ex.Message}", LogSeverity.Danger);
                return null;
            }
        }

        private static async Task<bool> CreateDefaultConfigurationFile()
        {
            try
            {
                var defaultConfig = new RoofConfiguration()
                {
                    ///////////ENCRYPTED///////////
                    SchemaVersion = CURRENT_CONFIG_FILE_SCHEMA_VERSION,
                    RoofIdentifier = Guid.NewGuid().ToString().Replace("-", ""),
                    HasCompletedInitialSetup = false,
                    RegisteredClients = new Dictionary<RoofClientID, RoofClientConfig>()
                    ///////////ENCRYPTED///////////
                };

                return await SaveConfiguration(defaultConfig);
            }
            catch (Exception ex)
            {
                Logger.LogExceptionToFile($"[ConfigurationManager] - CreateDefaultConfigurationFile(): Exception occurred while trying to create default configuration.", ex);
                return false;
            }
        }
        
        private static async Task<RoofConfiguration> Migrate(string rawJson, RoofConfiguration current)
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            
            //ROOT LEVEL MIGRATIONS
            Logger.LogToFile("[ConfigurationManager] - Migrate(): Migrating root-level configurations...");
            //ROOT LEVEL MIGRATIONS END

            //CHILDREN LEVEL MIGRATIONS
            Logger.LogToFile("[ConfigurationManager] - Migrate(): Migrating children-level configurations...");
            if (root.TryGetProperty("RegisteredClients", out var clientsEl) &&
                clientsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var clientProp in clientsEl.EnumerateObject())
                {
                    var clientId = clientProp.Name;
                    var clientObj = clientProp.Value;

                    if (current.RegisteredClients == null)
                        current.RegisteredClients = new Dictionary<RoofClientID, RoofClientConfig>();

                    if (!current.RegisteredClients.TryGetValue(clientId, out var clientCfg) || clientCfg == null)
                    {
                        clientCfg = new RoofClientConfig();
                        current.RegisteredClients[clientId] = clientCfg;
                    }

                    // OLD -> NEW mappings
                    if (clientObj.TryGetProperty("NetworkEncryptionKey", out var oldNEK) &&
                        oldNEK.ValueKind == JsonValueKind.String &&
                        clientObj.TryGetProperty("NetworkEncryptionIV", out var oldNIV) &&
                        oldNIV.ValueKind == JsonValueKind.String)
                    {
                        bool createRfCliNEKStatus = await CreateRoofClientNetworkEncryptionKey(clientId, oldNEK.GetString()!, oldNIV.GetString()!);
                        
                        if(!createRfCliNEKStatus)
                            throw new IOException($"Failed to migrate roof client network encryption key for {clientId}.");
                    }
                    else
                    {
                        if (clientObj.TryGetProperty("NetworkEncryptionKey", out _) ||
                            clientObj.TryGetProperty("NetworkEncryptionIV", out _))
                            throw new IOException($"Invalid legacy encryption key for {clientId}.");

                        var keyContent = await File.ReadAllBytesAsync(ClientKeyPath(clientId));
                        if (keyContent.Length != 48 || !await SaveRoofClientNetworkEncryptionKey(clientId,
                                Convert.ToBase64String(keyContent.Take(32).ToArray()),
                                Convert.ToBase64String(keyContent.Skip(32).ToArray())))
                            throw new IOException($"Invalid encryption key for {clientId}.");
                    }
                }
            }

            Logger.LogToFile("[ConfigurationManager] - Migrate(): Migration complete.");
            return current;
        }
        
        private static string ComputeLegacyConfigurationIntegrityHash(string rawJson)
        {
            // Older schemas included client keys in their serialized integrity hash.
            var config = JsonNode.Parse(rawJson)!.AsObject();
            config.Remove("Sha256FileHash");
            return Security.SHA256.ComputeSha256Hash(config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        private static string ComputeConfigurationIntegrityHash(RoofConfiguration config)
        {
            //Store the hash value field context
            string? existingHash = config.Sha256FileHash;

            //Exclude the hash value field from serialisation and hash check
            config.Sha256FileHash = null;

            //Generate at-runtime serialised config 
            string canonicalJson = JsonSerializer.Serialize(
                config,
                RoofConfigurationJSONContext.Default.RoofConfiguration
            );

            //Restore original hash value context
            config.Sha256FileHash = existingHash;

            //Compute runtime hash
            return Security.SHA256.ComputeSha256Hash(canonicalJson);
        }
    }
}