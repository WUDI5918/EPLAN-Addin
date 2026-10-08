using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TranslationEplAddIn.Tool
{
    public static class FilterService
    {
        public class ProtectionSession
        {
            public string TokenizedText { get; set; } = string.Empty;
            public Dictionary<string, string> Tokens { get; set; } = new Dictionary<string, string>();
        }

        private class MatchCandidate
        {
            public int StartIndex { get; set; }
            public int Length { get; set; }
            public int EndIndex => StartIndex + Length;
            public int RuleOrder { get; set; }
            public string Value { get; set; }
            public string Token { get; set; }
        }

        /// <summary>
        /// Parses a regex rule string from right to left, supporting patterns that contain pipe '|' characters.
        /// Format: Pattern|Description|IsActive or Pattern
        /// </summary>
        public static bool ParseRule(string rule, out string pattern, out string description, out bool isActive)
        {
            pattern = string.Empty;
            description = string.Empty;
            isActive = true;

            if (string.IsNullOrWhiteSpace(rule))
            {
                return false;
            }

            int lastPipe = rule.LastIndexOf('|');
            if (lastPipe >= 0)
            {
                string lastPart = rule.Substring(lastPipe + 1).Trim();
                if (bool.TryParse(lastPart, out bool parsedActive))
                {
                    isActive = parsedActive;
                    int secondLastPipe = rule.LastIndexOf('|', lastPipe - 1);
                    if (secondLastPipe >= 0)
                    {
                        string candidatePattern = rule.Substring(0, secondLastPipe);
                        string candidateDesc = rule.Substring(secondLastPipe + 1, lastPipe - secondLastPipe - 1).Trim();

                        // If candidatePattern is non-empty but syntactically invalid regex (e.g. unclosed parenthesis like "(A" from "(A|B)|true"),
                        // whereas the full prefix rule.Substring(0, lastPipe) is valid, then secondLastPipe was inside the regex.
                        if (!string.IsNullOrEmpty(candidatePattern) && !IsValidRegex(candidatePattern) && IsValidRegex(rule.Substring(0, lastPipe)))
                        {
                            description = string.Empty;
                            pattern = rule.Substring(0, lastPipe);
                        }
                        else
                        {
                            description = candidateDesc;
                            pattern = candidatePattern;
                        }
                    }
                    else
                    {
                        description = string.Empty;
                        pattern = rule.Substring(0, lastPipe);
                    }
                    return !string.IsNullOrEmpty(pattern);
                }
            }

            // If not ending with |true or |false, the entire rule string is the regex pattern
            pattern = rule;
            description = string.Empty;
            isActive = true;
            return true;
        }

        private static bool IsValidRegex(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            try
            {
                _ = new Regex(pattern);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Single-pass tokenizer: Scans the original clean text with all active rules simultaneously,
        /// resolves overlapping intervals (preferring longer match, then earlier rule order),
        /// and replaces matches from right to left with tokens [[TKN_n]].
        /// </summary>
        public static ProtectionSession Protect(string text, List<string> rules)
        {
            var session = new ProtectionSession { TokenizedText = text ?? string.Empty };
            if (string.IsNullOrEmpty(text) || rules == null || rules.Count == 0)
            {
                return session;
            }

            // 1. Parse rules and collect active ones
            var activeRules = new List<(string Pattern, int RuleOrder)>();
            for (int i = 0; i < rules.Count; i++)
            {
                string rule = rules[i];
                if (string.IsNullOrWhiteSpace(rule)) continue;

                if (ParseRule(rule, out string pattern, out _, out bool isActive))
                {
                    if (isActive && !string.IsNullOrEmpty(pattern))
                    {
                        activeRules.Add((pattern, i));
                    }
                }
            }

            if (activeRules.Count == 0)
            {
                return session;
            }

            // 2. Scan original clean text with all active rules
            var candidates = new List<MatchCandidate>();
            foreach (var ruleInfo in activeRules)
            {
                try
                {
                    Regex regex = new Regex(ruleInfo.Pattern, RegexOptions.IgnoreCase);
                    var matches = regex.Matches(text);
                    foreach (Match m in matches)
                    {
                        if (m.Success && m.Length > 0)
                        {
                            candidates.Add(new MatchCandidate
                            {
                                StartIndex = m.Index,
                                Length = m.Length,
                                RuleOrder = ruleInfo.RuleOrder,
                                Value = m.Value
                            });
                        }
                    }
                }
                catch
                {
                    // Ignore malformed regex pattern
                }
            }

            if (candidates.Count == 0)
            {
                return session;
            }

            // 3. Resolve conflicts / eliminate overlapping intervals:
            // Priority: longest match first, then earlier rule order, then earlier start index
            candidates.Sort((a, b) =>
            {
                int lenCmp = b.Length.CompareTo(a.Length);
                if (lenCmp != 0) return lenCmp;

                int ruleCmp = a.RuleOrder.CompareTo(b.RuleOrder);
                if (ruleCmp != 0) return ruleCmp;

                return a.StartIndex.CompareTo(b.StartIndex);
            });

            var chosen = new List<MatchCandidate>();
            foreach (var cand in candidates)
            {
                bool overlaps = false;
                foreach (var selected in chosen)
                {
                    if (Math.Max(cand.StartIndex, selected.StartIndex) < Math.Min(cand.EndIndex, selected.EndIndex))
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (!overlaps)
                {
                    chosen.Add(cand);
                }
            }

            // 4. Sort chosen intervals from left to right to assign sequential token IDs [[TKN_0]], [[TKN_1]], ...
            chosen.Sort((a, b) => a.StartIndex.CompareTo(b.StartIndex));
            for (int i = 0; i < chosen.Count; i++)
            {
                string token = $"[[TKN_{i}]]";
                chosen[i].Token = token;
                session.Tokens[token] = chosen[i].Value;
            }

            // 5. Replace intervals from right to left to keep preceding character indices valid
            string currentText = text;
            for (int i = chosen.Count - 1; i >= 0; i--)
            {
                var item = chosen[i];
                currentText = currentText.Remove(item.StartIndex, item.Length)
                                         .Insert(item.StartIndex, item.Token);
            }

            session.TokenizedText = currentText;
            return session;
        }

        /// <summary>
        /// Accurately restores tokens back to their original values in a single pass.
        /// Handles case variations and optional spaces introduced by translation engines.
        /// </summary>
        public static string Restore(string tokenizedText, Dictionary<string, string> tokens)
        {
            if (string.IsNullOrEmpty(tokenizedText) || tokens == null || tokens.Count == 0)
            {
                return tokenizedText;
            }

            var tokenMap = new Dictionary<string, string>(tokens, StringComparer.OrdinalIgnoreCase);
            var validKeys = tokenMap.Keys.Where(k => !string.IsNullOrEmpty(k)).OrderByDescending(k => k.Length).ToList();
            if (validKeys.Count == 0)
            {
                return tokenizedText;
            }

            // Match standard [[TKN_n]], spaced [ [ TKN_n ] ], full-width brackets 【【TKN_n】】, ［［TKN_n］］, 【TKN_n】,
            // as well as any explicit custom keys in the token map.
            var pattern = @"(?:\[\s*\[|［\s*［|【\s*【|【|［|\[\[)\s*TKN_(\d+)\s*(?:\]\s*\]|］\s*］|】\s*】|】|］|\]\])|" + string.Join("|", validKeys.Select(Regex.Escape));
            return Regex.Replace(tokenizedText, pattern, m =>
            {
                // If it matched a TKN_\d+ pattern, lookup the canonical [[TKN_n]] key
                var tknMatch = Regex.Match(m.Value, @"TKN_(\d+)", RegexOptions.IgnoreCase);
                if (tknMatch.Success)
                {
                    string canonicalKey = $"[[TKN_{tknMatch.Groups[1].Value}]]";
                    if (tokenMap.TryGetValue(canonicalKey, out string canonicalVal))
                    {
                        return canonicalVal;
                    }
                }

                string normalizedKey = Regex.Replace(m.Value, @"\s+", "");
                if (tokenMap.TryGetValue(normalizedKey, out string originalVal) ||
                    tokenMap.TryGetValue(m.Value, out originalVal))
                {
                    return originalVal;
                }
                return m.Value;
            }, RegexOptions.IgnoreCase);
        }
    }
}
