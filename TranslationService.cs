using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using TranslationEplAddIn.Tool.Models;

namespace TranslationEplAddIn.Tool
{
    public static class TranslationService
    {
        private static readonly HttpClient Client = new HttpClient();

        static TranslationService()
        {
            // Set default timeout to 30 seconds
            Client.Timeout = TimeSpan.FromSeconds(30);
        }

        public static async Task TranslateAsync(
            List<TranslationItem> items,
            string sourceLang,
            string targetLang,
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            if (items == null || items.Count == 0) return;

            // Normalize language names to standard English names
            sourceLang = MapToEnglishName(sourceLang);
            targetLang = MapToEnglishName(targetLang);

            // If target language is already English, or all items are multilingual, translate all in one go
            if (targetLang.Equals("English", StringComparison.OrdinalIgnoreCase) || items.All(i => i.IsMultiLang))
            {
                await TranslateItemsInternalAsync(items, sourceLang, targetLang, progress, cancellationToken);
                return;
            }

            // Otherwise, targetLang is not English and we have some non-multilang items.
            // Split them:
            var multiLangItems = items.Where(i => i.IsMultiLang).ToList();
            var singleLangItems = items.Where(i => !i.IsMultiLang).ToList();

            int totalCount = items.Count;
            int processedCount = 0;

            if (multiLangItems.Count > 0)
            {
                await TranslateItemsInternalAsync(multiLangItems, sourceLang, targetLang, new Progress<int>(p => {
                    int currentProcessed = (int)((double)p / 100 * multiLangItems.Count);
                    int totalProgress = (int)((double)(processedCount + currentProcessed) / totalCount * 100);
                    progress?.Report(Math.Min(totalProgress, 100));
                }), cancellationToken);
                processedCount += multiLangItems.Count;
            }

            if (singleLangItems.Count > 0)
            {
                await TranslateItemsInternalAsync(singleLangItems, sourceLang, "English", new Progress<int>(p => {
                    int currentProcessed = (int)((double)p / 100 * singleLangItems.Count);
                    int totalProgress = (int)((double)(processedCount + currentProcessed) / totalCount * 100);
                    progress?.Report(Math.Min(totalProgress, 100));
                }), cancellationToken);
                processedCount += singleLangItems.Count;
            }

            progress?.Report(100);
        }

        private static async Task TranslateItemsInternalAsync(
            List<TranslationItem> items,
            string sourceLang,
            string targetLang,
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            if (items == null || items.Count == 0) return;

            string engine = SettingsManager.Current.SelectedEngine;
            int batchSize = 20; // LLM batch size
            
            if (engine == "Google" || engine == "DeepL" || engine == "Baidu")
            {
                batchSize = 10;
            }

            int processedCount = 0;
            
            // 1. Translation Memory Pre-Check
            var textsToLookup = items.Where(i => i.Status != "Glossary" && i.Status != "Translated" && !string.IsNullOrEmpty(i.SourceText)).Select(i => i.SourceText).Distinct().ToList();
            var tmResults = TranslationMemoryService.LookupTranslations(textsToLookup, sourceLang, targetLang);
            
            foreach (var item in items)
            {
                if (item.Status != "Glossary" && item.Status != "Translated" && !string.IsNullOrEmpty(item.SourceText))
                {
                    if (tmResults.TryGetValue(item.SourceText, out string tmTranslation))
                    {
                        item.TranslatedText = tmTranslation;
                        item.Status = "Translated";
                        item.EngineUsed = "Translation Memory";
                        Interlocked.Increment(ref processedCount);
                    }
                }
            }

            progress?.Report((int)((double)processedCount / items.Count * 100));

            // 2. Filter remaining items
            var remainingItems = items.Where(i => i.Status != "Glossary" && i.Status != "Translated" && !string.IsNullOrEmpty(i.SourceText)).ToList();
            
            if (remainingItems.Count == 0) return; // All translated from TM or Glossary

            var batches = SplitIntoBatches(remainingItems, batchSize);

            // 3. Concurrent translation
            using (var semaphore = new SemaphoreSlim(5)) // Max 5 concurrent requests
            {
                var tasks = batches.Select(async batch =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var protectionSessions = new List<FilterService.ProtectionSession>();
                        var textsToTranslate = new List<string>();

                        foreach (var item in batch)
                        {
                            var session = FilterService.Protect(item.SourceText, SettingsManager.Current.RegexProtectionRules);
                            protectionSessions.Add(session);
                            textsToTranslate.Add(session.TokenizedText);
                        }

                        List<string> translations = null;

                        switch (engine)
                        {
                            case "Deepseek":
                                translations = await TranslateWithLLMAsync(textsToTranslate, sourceLang, targetLang, SettingsManager.Current.DeepseekApiKey, SettingsManager.Current.DeepseekEndpoint, SettingsManager.Current.DeepseekModel, cancellationToken);
                                break;
                            case "Zhipu":
                                translations = await TranslateWithLLMAsync(textsToTranslate, sourceLang, targetLang, SettingsManager.Current.ZhipuApiKey, SettingsManager.Current.ZhipuEndpoint, SettingsManager.Current.ZhipuModel, cancellationToken);
                                break;
                            case "DeepL":
                                translations = await TranslateWithDeepLAsync(textsToTranslate, sourceLang, targetLang, SettingsManager.Current.DeeplApiKey, cancellationToken);
                                break;
                            case "Google":
                            default:
                                translations = await TranslateWithGoogleAsync(textsToTranslate, sourceLang, targetLang, SettingsManager.Current.GoogleApiKey, cancellationToken);
                                break;
                        }

                        if (translations != null && translations.Count == batch.Count)
                        {
                            var newTmItems = new List<TranslationMemoryItem>();
                            for (int i = 0; i < batch.Count; i++)
                            {
                                string restoredText = FilterService.Restore(translations[i], protectionSessions[i].Tokens);
                                batch[i].TranslatedText = restoredText;
                                batch[i].Status = "Translated";
                                batch[i].EngineUsed = engine;

                                newTmItems.Add(new TranslationMemoryItem
                                {
                                    SourceHash = TranslationMemoryService.ComputeHash(batch[i].SourceText, sourceLang, targetLang),
                                    SourceLang = sourceLang,
                                    TargetLang = targetLang,
                                    OriginalText = batch[i].SourceText,
                                    TranslatedText = restoredText,
                                    EngineUsed = engine,
                                    Timestamp = DateTime.Now
                                });
                            }
                            
                            // Save to TM
                            if (newTmItems.Count > 0)
                            {
                                TranslationMemoryService.SaveTranslations(newTmItems);
                            }
                        }
                        else
                        {
                            foreach (var item in batch)
                            {
                                item.Status = "Error";
                                item.EngineUsed = engine;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Batch translation failed");
                        foreach (var item in batch)
                        {
                            item.Status = "Error";
                            item.TranslatedText = $"[Error: {ex.Message}]";
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                        int currentProcessed = Interlocked.Add(ref processedCount, batch.Count);
                        int percentComplete = (int)((double)currentProcessed / items.Count * 100);
                        progress?.Report(percentComplete);
                    }
                });

                await Task.WhenAll(tasks);
            }
        }

        private static IEnumerable<List<T>> SplitIntoBatches<T>(List<T> list, int size)
        {
            for (int i = 0; i < list.Count; i += size)
            {
                yield return list.GetRange(i, Math.Min(size, list.Count - i));
            }
        }

        private static async Task<List<string>> TranslateWithLLMAsync(
            List<string> texts,
            string sourceLang,
            string targetLang,
            string apiKey,
            string endpoint,
            string modelName,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException($"API Key for model {modelName} is not configured.");
            }

            var requestUrl = $"{endpoint.TrimEnd('/')}/chat/completions";
            
            // Build the electrical translation prompt
            string systemPrompt = $"You are a professional industrial electrical engineering translator. " +
                                  $"Translate the following list of electrical engineering labels/texts from {sourceLang} to {targetLang}. " +
                                  $"Preserve EPLAN placeholders, device tags (e.g., =F1+A1-K1), variables, and format strings (like %Value% or \\n). " +
                                  $"Strictly return ONLY a JSON array of strings in the exact same order as the input. " +
                                  $"Do not return any markdown formatting, markdown code blocks, or conversational wrapper. " +
                                  $"Example input: [\"熔断器\", \"电缆\"] " +
                                  $"Example output: [\"Fuse\", \"Cable\"]";

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            string userContent = serializer.Serialize(texts);

            var payload = new
            {
                model = modelName,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userContent }
                },
                temperature = 0.1
            };

            string jsonPayload = serializer.Serialize(payload);
            using (var request = new HttpRequestMessage(HttpMethod.Post, requestUrl))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                using (var response = await Client.SendAsync(request, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    byte[] responseBytes = await response.Content.ReadAsByteArrayAsync();
                    string responseBody = Encoding.UTF8.GetString(responseBytes);
                    
                    var responseData = serializer.Deserialize<Dictionary<string, object>>(responseBody);
                    if (responseData.TryGetValue("choices", out object choicesObj) && choicesObj is System.Collections.IList choices && choices.Count > 0)
                    {
                        var firstChoice = choices[0] as Dictionary<string, object>;
                        if (firstChoice != null && firstChoice.TryGetValue("message", out object messageObj) && messageObj is Dictionary<string, object> message)
                        {
                            if (message.TryGetValue("content", out object contentObj) && contentObj is string content)
                            {
                                // Clean up markdown formatting if LLM returned it
                                string cleanJson = content.Trim();
                                if (cleanJson.StartsWith("```json"))
                                {
                                    cleanJson = cleanJson.Substring(7);
                                }
                                if (cleanJson.StartsWith("```"))
                                {
                                    cleanJson = cleanJson.Substring(3);
                                }
                                if (cleanJson.EndsWith("```"))
                                {
                                    cleanJson = cleanJson.Substring(0, cleanJson.Length - 3);
                                }
                                cleanJson = cleanJson.Trim();

                                try
                                {
                                    List<string> resultList = serializer.Deserialize<List<string>>(cleanJson);
                                    if (resultList != null)
                                    {
                                        return resultList;
                                    }
                                }
                                catch
                                {
                                    // Fallback if parsing JSON fails, try splitting by lines or return source
                                    throw new FormatException("LLM did not return a valid JSON array.");
                                }
                            }
                        }
                    }
                }
            }

            throw new Exception("Invalid response structure from LLM API.");
        }

        private static async Task<List<string>> TranslateWithDeepLAsync(
            List<string> texts,
            string sourceLang,
            string targetLang,
            string apiKey,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("DeepL API Key is not configured.");
            }

            string targetCode = MapLanguageCodeToDeepL(targetLang);
            var requestUrl = "https://api-free.deepl.com/v2/translate";
            if (!apiKey.EndsWith(":fx"))
            {
                requestUrl = "https://api.deepl.com/v2/translate";
            }

            var requestData = new List<KeyValuePair<string, string>>();
            foreach (var text in texts)
            {
                requestData.Add(new KeyValuePair<string, string>("text", text));
            }
            requestData.Add(new KeyValuePair<string, string>("target_lang", targetCode));

            using (var request = new HttpRequestMessage(HttpMethod.Post, requestUrl))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", apiKey);
                request.Content = new FormUrlEncodedContent(requestData);

                using (var response = await Client.SendAsync(request, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    byte[] responseBytes = await response.Content.ReadAsByteArrayAsync();
                    string json = Encoding.UTF8.GetString(responseBytes);
                    
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    var data = serializer.Deserialize<Dictionary<string, object>>(json);
                    
                    if (data.TryGetValue("translations", out object transObj) && transObj is System.Collections.IList transList)
                    {
                        var results = new List<string>();
                        foreach (var item in transList)
                        {
                            if (item is Dictionary<string, object> dict && dict.TryGetValue("text", out object textVal))
                            {
                                results.Add(textVal.ToString());
                            }
                        }
                        return results;
                    }
                }
            }

            throw new Exception("Invalid response structure from DeepL API.");
        }

        private static async Task<List<string>> TranslateWithGoogleAsync(
            List<string> texts,
            string sourceLang,
            string targetLang,
            string apiKey,
            CancellationToken cancellationToken)
        {
            string srcCode = MapLanguageCodeToGoogle(sourceLang);
            string tgtCode = MapLanguageCodeToGoogle(targetLang);

            // If API key is provided, use standard Cloud Translation API v2
            if (!string.IsNullOrEmpty(apiKey))
            {
                var requestUrl = $"https://translation.googleapis.com/language/translate/v2?key={apiKey}";
                var payload = new
                {
                    q = texts.ToArray(),
                    target = tgtCode,
                    source = srcCode,
                    format = "text"
                };

                JavaScriptSerializer serializer = new JavaScriptSerializer();
                string jsonPayload = serializer.Serialize(payload);

                using (var request = new HttpRequestMessage(HttpMethod.Post, requestUrl))
                {
                    request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    using (var response = await Client.SendAsync(request, cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();
                        byte[] responseBytes = await response.Content.ReadAsByteArrayAsync();
                        string json = Encoding.UTF8.GetString(responseBytes);
                        var data = serializer.Deserialize<Dictionary<string, object>>(json);
                        
                        if (data.TryGetValue("data", out object dataObj) && dataObj is Dictionary<string, object> dataDict)
                        {
                            if (dataDict.TryGetValue("translations", out object transObj) && transObj is System.Collections.IList transList)
                            {
                                var results = new List<string>();
                                foreach (var item in transList)
                                {
                                    if (item is Dictionary<string, object> dict && dict.TryGetValue("translatedText", out object textVal))
                                    {
                                        results.Add(textVal.ToString());
                                    }
                                }
                                return results;
                            }
                        }
                    }
                }
                throw new Exception("Invalid response structure from Google Translate API.");
            }
            else
            {
                // Fallback to free web API single-text translation loop
                var results = new List<string>();
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                foreach (var text in texts)
                {
                    if (string.IsNullOrEmpty(text))
                    {
                        results.Add(string.Empty);
                        continue;
                    }

                    var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={srcCode}&tl={tgtCode}&dt=t&q={Uri.EscapeDataString(text)}";
                    using (var response = await Client.GetAsync(url, cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();
                        byte[] responseBytes = await response.Content.ReadAsByteArrayAsync();
                        string json = Encoding.UTF8.GetString(responseBytes);
                        
                        // Response is: [[["translated","source",null,null,1]],null,"zh-CN"]
                        System.Collections.ArrayList root = serializer.Deserialize<System.Collections.ArrayList>(json);
                        if (root != null && root.Count > 0 && root[0] is System.Collections.IList translationRows)
                        {
                            StringBuilder sb = new StringBuilder();
                            foreach (var row in translationRows)
                            {
                                if (row is System.Collections.IList rowFields && rowFields.Count > 0 && rowFields[0] != null)
                                {
                                    sb.Append(rowFields[0].ToString());
                                }
                            }
                            results.Add(sb.ToString());
                        }
                        else
                        {
                            results.Add(text); // Fallback to source
                        }
                    }
                }
                return results;
            }
        }

        private static string GetCleanLanguageKey(string langName)
        {
            if (string.IsNullOrEmpty(langName)) return string.Empty;
            string clean = langName.Trim();
            int parenIdx = clean.IndexOf('(');
            if (parenIdx >= 0)
            {
                clean = clean.Substring(0, parenIdx).Trim();
            }
            return clean.ToLower();
        }

        private static string MapToEnglishName(string langName)
        {
            switch (GetCleanLanguageKey(langName))
            {
                case "zh_cn":
                case "chinese":
                case "zh":
                    return "Chinese";
                case "cs_cz":
                case "czech":
                case "cs":
                    return "Czech";
                case "da_dk":
                case "danish":
                case "da":
                    return "Danish";
                case "de_de":
                case "german":
                case "de":
                    return "German";
                case "en_us":
                case "english":
                case "en":
                    return "English";
                case "es_es":
                case "spanish":
                case "es":
                    return "Spanish";
                case "fr_fr":
                case "french":
                case "fr":
                    return "French";
                case "hu_hu":
                case "hungarian":
                case "hu":
                    return "Hungarian";
                case "it_it":
                case "italian":
                case "it":
                    return "Italian";
                case "ja_jp":
                case "japanese":
                case "ja":
                    return "Japanese";
                case "ko_kr":
                case "korean":
                case "ko":
                    return "Korean";
                case "nl_nl":
                case "dutch":
                case "nl":
                    return "Dutch";
                case "pl_pl":
                case "polish":
                case "pl":
                    return "Polish";
                case "pt_br":
                    return "Portuguese (Brazil)";
                case "pt_pt":
                    return "Portuguese (Portugal)";
                case "portuguese":
                case "pt":
                    return "Portuguese";
                case "ro_ro":
                case "romanian":
                case "ro":
                    return "Romanian";
                case "ru_ru":
                case "russian":
                case "ru":
                    return "Russian";
                case "sv_se":
                case "swedish":
                case "sv":
                    return "Swedish";
                case "tr_tr":
                case "turkish":
                case "tr":
                    return "Turkish";
                default:
                    return langName;
            }
        }

        private static string MapLanguageCodeToDeepL(string code)
        {
            switch (GetCleanLanguageKey(code))
            {
                case "zh_cn":
                case "chinese":
                case "zh":
                    return "ZH";
                case "cs_cz":
                case "czech":
                case "cs":
                    return "CS";
                case "da_dk":
                case "danish":
                case "da":
                    return "DA";
                case "de_de":
                case "german":
                case "de":
                    return "DE";
                case "en_us":
                case "english":
                case "en":
                    return "EN";
                case "es_es":
                case "spanish":
                case "es":
                    return "ES";
                case "fr_fr":
                case "french":
                case "fr":
                    return "FR";
                case "hu_hu":
                case "hungarian":
                case "hu":
                    return "HU";
                case "it_it":
                case "italian":
                case "it":
                    return "IT";
                case "ja_jp":
                case "japanese":
                case "ja":
                    return "JA";
                case "ko_kr":
                case "korean":
                case "ko":
                    return "KO";
                case "nl_nl":
                case "dutch":
                case "nl":
                    return "NL";
                case "pl_pl":
                case "polish":
                case "pl":
                    return "PL";
                case "pt_br":
                    return "PT-BR";
                case "pt_pt":
                    return "PT-PT";
                case "portuguese":
                case "pt":
                    return "PT";
                case "ro_ro":
                case "romanian":
                case "ro":
                    return "RO";
                case "ru_ru":
                case "russian":
                case "ru":
                    return "RU";
                case "sv_se":
                case "swedish":
                case "sv":
                    return "SV";
                case "tr_tr":
                case "turkish":
                case "tr":
                    return "TR";
                default:
                    return code.ToUpper();
            }
        }

        private static string MapLanguageCodeToGoogle(string code)
        {
            switch (GetCleanLanguageKey(code))
            {
                case "zh_cn":
                case "chinese":
                case "zh":
                    return "zh-CN";
                case "cs_cz":
                case "czech":
                case "cs":
                    return "cs";
                case "da_dk":
                case "danish":
                case "da":
                    return "da";
                case "de_de":
                case "german":
                case "de":
                    return "de";
                case "en_us":
                case "english":
                case "en":
                    return "en";
                case "es_es":
                case "spanish":
                case "es":
                    return "es";
                case "fr_fr":
                case "french":
                case "fr":
                    return "fr";
                case "hu_hu":
                case "hungarian":
                case "hu":
                    return "hu";
                case "it_it":
                case "italian":
                case "it":
                    return "it";
                case "ja_jp":
                case "japanese":
                case "ja":
                    return "ja";
                case "ko_kr":
                case "korean":
                case "ko":
                    return "ko";
                case "nl_nl":
                case "dutch":
                case "nl":
                    return "nl";
                case "pl_pl":
                case "polish":
                case "pl":
                    return "pl";
                case "pt_br":
                case "pt_pt":
                case "portuguese":
                case "pt":
                    return "pt";
                case "ro_ro":
                case "romanian":
                case "ro":
                    return "ro";
                case "ru_ru":
                case "russian":
                case "ru":
                    return "ru";
                case "sv_se":
                case "swedish":
                case "sv":
                    return "sv";
                case "tr_tr":
                case "turkish":
                case "tr":
                    return "tr";
                default:
                    return code;
            }
        }

        public static async Task<bool> TestConnectionAsync(string engine, string apiKey, string endpoint, string modelName)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new ArgumentException("API Key 不能为空。");
            }

            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    if (engine == "Deepseek" || engine == "Zhipu")
                    {
                        if (string.IsNullOrEmpty(endpoint))
                        {
                            throw new ArgumentException("Base URL 不能为空。");
                        }
                        if (string.IsNullOrEmpty(modelName))
                        {
                            throw new ArgumentException("模型名称 不能为空。");
                        }

                        var requestUrl = $"{endpoint.TrimEnd('/')}/chat/completions";
                        var serializer = new JavaScriptSerializer();
                        var payload = new
                        {
                            model = modelName,
                            messages = new[]
                            {
                                new { role = "user", content = "Ping" }
                            },
                            max_tokens = 5
                        };

                        string jsonPayload = serializer.Serialize(payload);
                        using (var request = new HttpRequestMessage(HttpMethod.Post, requestUrl))
                        {
                            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                            using (var response = await Client.SendAsync(request, cts.Token))
                            {
                                if (response.IsSuccessStatusCode)
                                {
                                    return true;
                                }
                                else
                                {
                                    byte[] errBytes = await response.Content.ReadAsByteArrayAsync();
                                    string errBody = Encoding.UTF8.GetString(errBytes);
                                    throw new Exception($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}. Response: {errBody}");
                                }
                            }
                        }
                    }
                    else if (engine == "DeepL")
                    {
                        var requestUrl = "https://api-free.deepl.com/v2/usage";
                        if (!apiKey.EndsWith(":fx"))
                        {
                            requestUrl = "https://api.deepl.com/v2/usage";
                        }

                        using (var request = new HttpRequestMessage(HttpMethod.Get, requestUrl))
                        {
                            request.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", apiKey);

                            using (var response = await Client.SendAsync(request, cts.Token))
                            {
                                if (response.IsSuccessStatusCode)
                                {
                                    return true;
                                }
                                else
                                {
                                    byte[] errBytes = await response.Content.ReadAsByteArrayAsync();
                                    string errBody = Encoding.UTF8.GetString(errBytes);
                                    throw new Exception($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}. Response: {errBody}");
                                }
                            }
                        }
                    }
                    else if (engine == "Google")
                    {
                        var requestUrl = $"https://translation.googleapis.com/language/translate/v2?key={apiKey}";
                        var serializer = new JavaScriptSerializer();
                        var payload = new
                        {
                            q = "hello",
                            target = "zh"
                        };
                        string jsonPayload = serializer.Serialize(payload);

                        using (var request = new HttpRequestMessage(HttpMethod.Post, requestUrl))
                        {
                            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                            using (var response = await Client.SendAsync(request, cts.Token))
                            {
                                if (response.IsSuccessStatusCode)
                                {
                                    return true;
                                }
                                else
                                {
                                    byte[] errBytes = await response.Content.ReadAsByteArrayAsync();
                                    string errBody = Encoding.UTF8.GetString(errBytes);
                                    throw new Exception($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}. Response: {errBody}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"连接测试失败: {ex.Message}");
            }

            return false;
        }
    }
}
