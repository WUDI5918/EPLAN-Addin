using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using TranslationEplAddIn.Tool.Models;

namespace TranslationEplAddIn.Tool
{
    public static class TranslationMemoryService
    {
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EPLAN_TranslationAddIn"
        );
        private static readonly string DbFilePath = Path.Combine(FolderPath, "TM.json");

        // SourceHash -> TranslationMemoryItem
        private static ConcurrentDictionary<string, TranslationMemoryItem> _memoryCache;
        private static readonly object _lockObj = new object();

        static TranslationMemoryService()
        {
            InitializeDatabase();
        }

        private static void InitializeDatabase()
        {
            try
            {
                if (!Directory.Exists(FolderPath))
                {
                    Directory.CreateDirectory(FolderPath);
                }

                if (!File.Exists(DbFilePath))
                {
                    File.WriteAllText(DbFilePath, "{}", Encoding.UTF8);
                    _memoryCache = new ConcurrentDictionary<string, TranslationMemoryItem>();
                }
                else
                {
                    string json = File.ReadAllText(DbFilePath, Encoding.UTF8);
                    var serializer = new JavaScriptSerializer();
                    serializer.MaxJsonLength = Int32.MaxValue;
                    var dict = serializer.Deserialize<Dictionary<string, TranslationMemoryItem>>(json);
                    _memoryCache = dict != null 
                        ? new ConcurrentDictionary<string, TranslationMemoryItem>(dict) 
                        : new ConcurrentDictionary<string, TranslationMemoryItem>();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to initialize Translation Memory JSON database.");
                _memoryCache = new ConcurrentDictionary<string, TranslationMemoryItem>();
            }
        }

        public static string ComputeHash(string text, string sourceLang, string targetLang)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            using (var sha256 = SHA256.Create())
            {
                string input = $"{sourceLang}_{targetLang}_{text}";
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        public static Dictionary<string, string> LookupTranslations(IEnumerable<string> sourceTexts, string sourceLang, string targetLang)
        {
            var results = new Dictionary<string, string>();
            if (sourceTexts == null) return results;

            try
            {
                foreach (var text in sourceTexts)
                {
                    if (string.IsNullOrEmpty(text)) continue;
                    string hash = ComputeHash(text, sourceLang, targetLang);

                    if (_memoryCache.TryGetValue(hash, out var item))
                    {
                        results[text] = item.TranslatedText;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to lookup translation memory in JSON.");
            }
            return results;
        }

        public static void SaveTranslations(IEnumerable<TranslationMemoryItem> items)
        {
            if (items == null) return;

            try
            {
                bool hasChanges = false;
                foreach (var item in items)
                {
                    if (string.IsNullOrEmpty(item.OriginalText) || string.IsNullOrEmpty(item.TranslatedText))
                        continue;

                    string hash = item.SourceHash;
                    _memoryCache[hash] = item;
                    hasChanges = true;
                }

                if (hasChanges)
                {
                    lock (_lockObj)
                    {
                        var snapshot = new Dictionary<string, TranslationMemoryItem>(_memoryCache);
                        var serializer = new JavaScriptSerializer();
                        serializer.MaxJsonLength = Int32.MaxValue;
                        string json = serializer.Serialize(snapshot);
                        File.WriteAllText(DbFilePath, json, Encoding.UTF8);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to save to translation memory JSON.");
            }
        }
    }
}
