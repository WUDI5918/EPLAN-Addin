using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using TranslationEplAddIn.Tool.Models;

namespace TranslationEplAddIn.Tool
{
    public static class SettingsManager
    {
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EPLAN_TranslationAddIn"
        );
        private static readonly string FilePath = Path.Combine(FolderPath, "settings.json");

        public static Configuration Current { get; set; } = new Configuration();

        static SettingsManager()
        {
            Load();
        }

        public static void Load()
        {
            try
            {
                if (!Directory.Exists(FolderPath))
                {
                    Directory.CreateDirectory(FolderPath);
                }

                if (!File.Exists(FilePath))
                {
                    Current = new Configuration();
                    Save();
                    return;
                }

                string json = File.ReadAllText(FilePath, Encoding.UTF8);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Configuration config = serializer.Deserialize<Configuration>(json);

                if (config != null)
                {
                    // Decrypt keys
                    config.DeepseekApiKey = Decrypt(config.DeepseekApiKey);
                    config.ZhipuApiKey = Decrypt(config.ZhipuApiKey);
                    config.WenxinApiKey = Decrypt(config.WenxinApiKey);
                    config.DeeplApiKey = Decrypt(config.DeeplApiKey);
                    config.GoogleApiKey = Decrypt(config.GoogleApiKey);
                    config.BaiduApiKey = Decrypt(config.BaiduApiKey);
                    
                    // Migrate from old default to new default
                    if (string.IsNullOrEmpty(config.DeepseekModel) || config.DeepseekModel == "deepseek-chat")
                    {
                        config.DeepseekModel = "deepseek-v4-flash";
                    }

                    Current = config;
                }
                else
                {
                    Current = new Configuration();
                }
            }
            catch (Exception)
            {
                Current = new Configuration();
            }
        }

        public static void Save()
        {
            try
            {
                if (!Directory.Exists(FolderPath))
                {
                    Directory.CreateDirectory(FolderPath);
                }

                // Create a clone to encrypt keys before saving to disk
                Configuration clone = new Configuration
                {
                    SelectedEngine = Current.SelectedEngine,
                    DeepseekApiKey = Encrypt(Current.DeepseekApiKey),
                    ZhipuApiKey = Encrypt(Current.ZhipuApiKey),
                    WenxinApiKey = Encrypt(Current.WenxinApiKey),
                    DeeplApiKey = Encrypt(Current.DeeplApiKey),
                    GoogleApiKey = Encrypt(Current.GoogleApiKey),
                    BaiduApiKey = Encrypt(Current.BaiduApiKey),
                    DeepseekEndpoint = Current.DeepseekEndpoint,
                    ZhipuEndpoint = Current.ZhipuEndpoint,
                    DeepseekModel = Current.DeepseekModel,
                    ZhipuModel = Current.ZhipuModel,
                    RegexProtectionRules = Current.RegexProtectionRules,
                    BlacklistedPages = Current.BlacklistedPages,
                    AppendMode = Current.AppendMode,
                    AppendSeparator = Current.AppendSeparator,
                    GlossaryPaths = Current.GlossaryPaths,
                    UseNativeEplanDictionary = Current.UseNativeEplanDictionary
                };

                JavaScriptSerializer serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(clone);
                File.WriteAllText(FilePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // In production, log to EPLAN logs
                Logger.Error(ex, "Failed to save settings");
            }
        }

        private static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(plainText);
                byte[] encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string Decrypt(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText)) return string.Empty;
            try
            {
                byte[] data = Convert.FromBase64String(encryptedText);
                byte[] decrypted = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
