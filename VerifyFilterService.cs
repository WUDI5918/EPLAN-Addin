using System;
using System.Collections.Generic;
using TranslationEplAddIn.Tool;

namespace Verification
{
    class Program
    {
        static int passed = 0;
        static int failed = 0;

        static void Assert(bool condition, string testName, string details = "")
        {
            if (condition)
            {
                passed++;
                Console.WriteLine($"[PASS] {testName}");
            }
            else
            {
                failed++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FAIL] {testName} - {details}");
                Console.ResetColor();
            }
        }

        static void Main(string[] args)
        {
            Console.WriteLine("=== Starting FilterService Tests ===");

            TestRuleParsing();
            TestOverlappingLongerMatchWins();
            TestOverlappingEarlierRuleWins();
            TestAdjacentNonOverlapping();
            TestTokenCorruptionImmunity();
            TestPipesInPatternAndInactiveRules();
            TestRestoreResilience();
            TestFullMatchVsPartialMatchFilter();
            TestBoundaryAndEmptyCases();

            Console.WriteLine("\n=== Test Results ===");
            Console.WriteLine($"Passed: {passed}, Failed: {failed}");

            if (failed > 0)
            {
                Environment.Exit(1);
            }
        }

        static void TestRuleParsing()
        {
            // Case 1: Raw pattern without metadata
            bool ok1 = FilterService.ParseRule(@"PLC_.*", out string p1, out string d1, out bool a1);
            Assert(ok1 && p1 == @"PLC_.*" && a1 == true, "ParseRule - Raw pattern");

            // Case 2: Standard 3-part rule
            bool ok2 = FilterService.ParseRule(@"<.*?>|HTML Tags|true", out string p2, out string d2, out bool a2);
            Assert(ok2 && p2 == @"<.*?>" && d2 == "HTML Tags" && a2 == true, "ParseRule - Standard active rule");

            // Case 3: Inactive rule
            bool ok3 = FilterService.ParseRule(@"<.*?>|HTML Tags|false", out string p3, out string d3, out bool a3);
            Assert(ok3 && p3 == @"<.*?>" && d3 == "HTML Tags" && a3 == false, "ParseRule - Standard inactive rule");

            // Case 4: Pattern with pipe '|' inside pattern
            bool ok4 = FilterService.ParseRule(@"(CAT|DOG|PIG)_\d+|Animal IDs|true", out string p4, out string d4, out bool a4);
            Assert(ok4 && p4 == @"(CAT|DOG|PIG)_\d+" && d4 == "Animal IDs" && a4 == true, "ParseRule - Pattern with multiple pipes");

            // Case 5: Pattern with pipes and empty description
            bool ok5 = FilterService.ParseRule(@"(A|B)||true", out string p5, out string d5, out bool a5);
            Assert(ok5 && p5 == @"(A|B)" && d5 == "" && a5 == true, "ParseRule - Pattern with pipe and empty description");

            // Case 5b: Pattern with pipes and single trailing boolean (no description pipe)
            bool ok5b = FilterService.ParseRule(@"(A|B)|true", out string p5b, out string d5b, out bool a5b);
            Assert(ok5b && p5b == @"(A|B)" && d5b == "" && a5b == true, "ParseRule - Pattern with pipe and single trailing boolean");

            bool ok5c = FilterService.ParseRule(@"(A|B|C)|false", out string p5c, out string d5c, out bool a5c);
            Assert(ok5c && p5c == @"(A|B|C)" && d5c == "" && a5c == false, "ParseRule - Complex pattern with pipe and single trailing boolean false");

            // Case 6: Raw pattern containing pipe but not ending in boolean
            bool ok6 = FilterService.ParseRule(@"cat|dog|bird", out string p6, out string d6, out bool a6);
            Assert(ok6 && p6 == @"cat|dog|bird" && a6 == true, "ParseRule - Raw regex with pipes without metadata");

            // Case 7: Invalid or empty inputs
            Assert(!FilterService.ParseRule("", out _, out _, out _), "ParseRule - Empty string returns false");
            Assert(!FilterService.ParseRule("   ", out _, out _, out _), "ParseRule - Whitespace returns false");
            Assert(!FilterService.ParseRule(null, out _, out _, out _), "ParseRule - Null returns false");
            Assert(!FilterService.ParseRule("||true", out _, out _, out _), "ParseRule - Empty pattern returns false");
        }

        static void TestOverlappingLongerMatchWins()
        {
            // Text: "ABCDEF"
            // Rule 1: "BCD" (len 3)
            // Rule 2: "BCDE" (len 4)
            // Longer match "BCDE" must win over "BCD"
            string text = "ABCDEF";
            var rules = new List<string> { "BCD|Short|true", "BCDE|Long|true" };

            var session = FilterService.Protect(text, rules);
            Assert(session.TokenizedText == "A[[TKN_0]]F", "Longer match wins - Tokenized text");
            Assert(session.Tokens.Count == 1 && session.Tokens["[[TKN_0]]"] == "BCDE", "Longer match wins - Token value");

            string restored = FilterService.Restore(session.TokenizedText, session.Tokens);
            Assert(restored == text, "Longer match wins - Restored text");

            // Reverse rule order: longer match is rule 1, shorter is rule 2
            var rulesReversed = new List<string> { "BCDE|Long|true", "BCD|Short|true" };
            var session2 = FilterService.Protect(text, rulesReversed);
            Assert(session2.TokenizedText == "A[[TKN_0]]F", "Longer match wins (reversed rule order) - Tokenized text");
            Assert(session2.Tokens["[[TKN_0]]"] == "BCDE", "Longer match wins (reversed rule order) - Token value");
        }

        static void TestOverlappingEarlierRuleWins()
        {
            // Text: "ABCDEF"
            // Rule 1: "BCD" (len 3, order 0)
            // Rule 2: "CDE" (len 3, order 1)
            // Same length: earlier rule Rule 1 must win over Rule 2
            string text = "ABCDEF";
            var rules = new List<string> { "BCD|Rule 1|true", "CDE|Rule 2|true" };

            var session = FilterService.Protect(text, rules);
            Assert(session.TokenizedText == "A[[TKN_0]]EF", "Earlier rule wins on equal length - Tokenized text");
            Assert(session.Tokens.Count == 1 && session.Tokens["[[TKN_0]]"] == "BCD", "Earlier rule wins - Token value is BCD");

            string restored = FilterService.Restore(session.TokenizedText, session.Tokens);
            Assert(restored == text, "Earlier rule wins - Restored text");

            // Reverse: Rule 1 is CDE, Rule 2 is BCD
            var rulesReversed = new List<string> { "CDE|Rule 1|true", "BCD|Rule 2|true" };
            var session2 = FilterService.Protect(text, rulesReversed);
            Assert(session2.TokenizedText == "AB[[TKN_0]]F", "Earlier rule wins (reversed) - Tokenized text");
            Assert(session2.Tokens["[[TKN_0]]"] == "CDE", "Earlier rule wins (reversed) - Token value is CDE");
        }

        static void TestAdjacentNonOverlapping()
        {
            string text = "ABCDEF";
            var rules = new List<string> { "ABC|Left|true", "DEF|Right|true" };

            var session = FilterService.Protect(text, rules);
            Assert(session.TokenizedText == "[[TKN_0]][[TKN_1]]", "Adjacent non-overlapping - Tokenized text");
            Assert(session.Tokens["[[TKN_0]]"] == "ABC", "Adjacent - Token 0");
            Assert(session.Tokens["[[TKN_1]]"] == "DEF", "Adjacent - Token 1");

            string restored = FilterService.Restore(session.TokenizedText, session.Tokens);
            Assert(restored == text, "Adjacent non-overlapping - Restored text");
        }

        static void TestTokenCorruptionImmunity()
        {
            // Crucial edge case: Rule 2 matches bracketed expressions `\[.*?\]` and Rule 3 matches `\[\[.*?\]\]`.
            // In a multi-pass system, Rule 3 would match [[TKN_0]] and corrupt it!
            // In single-pass system, rules ONLY match against original clean text.
            string text = "Connect [Motor_1] and [Valve_2] to System";
            var rules = new List<string>
            {
                @"\[Motor_\d+\]|Motor|true",
                @"\[Valve_\d+\]|Valve|true",
                @"\[\[.*?\]\]|Dangerous double brackets|true",
                @"\[.*?\]|Generic brackets|true",
                @"TKN_\d+|Token name collision|true"
            };

            var session = FilterService.Protect(text, rules);
            Assert(session.TokenizedText == "Connect [[TKN_0]] and [[TKN_1]] to System", "Corruption immunity - Tokenized text");
            Assert(session.Tokens["[[TKN_0]]"] == "[Motor_1]", "Corruption immunity - Token 0");
            Assert(session.Tokens["[[TKN_1]]"] == "[Valve_2]", "Corruption immunity - Token 1");
            Assert(!session.TokenizedText.Contains("[[["), "Corruption immunity - No nested tokens");

            string restored = FilterService.Restore(session.TokenizedText, session.Tokens);
            Assert(restored == text, "Corruption immunity - Restored text");
        }

        static void TestPipesInPatternAndInactiveRules()
        {
            string text = "Fault: ERR_404, WARN_500, INFO_100, DEBUG_001";
            var rules = new List<string>
            {
                @"(ERR|WARN)_\d+|Active alerts|true",
                @"(INFO|DEBUG)_\d+|Inactive alerts|false" // Inactive! Must not be tokenized
            };

            var session = FilterService.Protect(text, rules);
            Assert(session.TokenizedText == "Fault: [[TKN_0]], [[TKN_1]], INFO_100, DEBUG_001", "Pipes and inactive - Tokenized text");
            Assert(session.Tokens.Count == 2, "Pipes and inactive - Only 2 tokens created");
            Assert(session.Tokens["[[TKN_0]]"] == "ERR_404", "Pipes and inactive - Token 0");
            Assert(session.Tokens["[[TKN_1]]"] == "WARN_500", "Pipes and inactive - Token 1");

            string restored = FilterService.Restore(session.TokenizedText, session.Tokens);
            Assert(restored == text, "Pipes and inactive - Restored text");
        }

        static void TestRestoreResilience()
        {
            var tokens = new Dictionary<string, string>
            {
                { "[[TKN_0]]", "Motor_1" },
                { "[[TKN_1]]", "Sensor_2" }
            };

            // 1. Lowercase tokens
            string lowerText = "Device [[tkn_0]] and [[tkn_1]]";
            string restoredLower = FilterService.Restore(lowerText, tokens);
            Assert(restoredLower == "Device Motor_1 and Sensor_2", "Restore resilience - Lowercase tokens");

            // 2. Whitespace inside tokens
            string spacedText = "Device [[ TKN_0 ]] and [[  tkn_1  ]]";
            string restoredSpaced = FilterService.Restore(spacedText, tokens);
            Assert(restoredSpaced == "Device Motor_1 and Sensor_2", "Restore resilience - Spaces inside tokens");

            // 3. Single-pass non-cascading replacement
            // If Token 0's value contains a literal "[[TKN_1]]", it must NOT be replaced by Token 1!
            var cascadingTokens = new Dictionary<string, string>
            {
                { "[[TKN_0]]", "literal_[[TKN_1]]_inside" },
                { "[[TKN_1]]", "SURPRISE" }
            };
            string cascadeInput = "Start [[TKN_0]] End";
            string restoredCascade = FilterService.Restore(cascadeInput, cascadingTokens);
            Assert(restoredCascade == "Start literal_[[TKN_1]]_inside End", "Restore resilience - No cascading replacement");

            // 4. Space between brackets
            string spacedBrackets = "Device [ [ TKN_0 ] ] and [ [ tkn_1 ] ]";
            string restoredSpacedBrackets = FilterService.Restore(spacedBrackets, tokens);
            Assert(restoredSpacedBrackets == "Device Motor_1 and Sensor_2", "Restore resilience - Space between brackets");

            // 5. Chinese full-width double brackets
            string fullWidthDouble = "设备 【【TKN_0】】 与 【【TKN_1】】 运行中";
            string restoredFullWidthDouble = FilterService.Restore(fullWidthDouble, tokens);
            Assert(restoredFullWidthDouble == "设备 Motor_1 与 Sensor_2 运行中", "Restore resilience - Chinese full-width double brackets");

            // 6. Chinese full-width single brackets
            string fullWidthSingle = "设备 【TKN_0】 与 【TKN_1】 运行中";
            string restoredFullWidthSingle = FilterService.Restore(fullWidthSingle, tokens);
            Assert(restoredFullWidthSingle == "设备 Motor_1 与 Sensor_2 运行中", "Restore resilience - Chinese full-width single brackets");

            // 7. Full-width ASCII brackets
            string fullWidthAscii = "设备 ［［TKN_0］］ 与 ［［tkn_1］］ 运行中";
            string restoredFullWidthAscii = FilterService.Restore(fullWidthAscii, tokens);
            Assert(restoredFullWidthAscii == "设备 Motor_1 与 Sensor_2 运行中", "Restore resilience - Full-width ASCII brackets");
        }

        static void TestFullMatchVsPartialMatchFilter()
        {
            var rules = new List<string> { @"PLC_.*|PLC Tags|true", @"<.*?>|HTML Tags|true" };

            // Substring case: "PLC_IO 系统控制柜" contains translatable text alongside protected token
            string mixedText = "PLC_IO 系统控制柜";
            var session = FilterService.Protect(mixedText, rules);
            Assert(session.Tokens.Count == 1, "Full vs Partial - Substring matched and tokenized");
            Assert(session.TokenizedText.Contains("[[TKN_0]]"), "Full vs Partial - Token inserted");

            // Simulated translation
            string translated = session.TokenizedText.Replace("系统控制柜", "System Control Cabinet");
            string restored = FilterService.Restore(translated, session.Tokens);
            Assert(restored == "PLC_IO System Control Cabinet", "Full vs Partial - Restored mixed text successfully");
        }

        static void TestBoundaryAndEmptyCases()
        {
            // Null / Empty text
            var s1 = FilterService.Protect("", new List<string> { ".*" });
            Assert(s1.TokenizedText == "" && s1.Tokens.Count == 0, "Boundary - Empty text");

            var s2 = FilterService.Protect(null, new List<string> { ".*" });
            Assert(s2.TokenizedText == "" && s2.Tokens.Count == 0, "Boundary - Null text");

            // Null / Empty rules
            var s3 = FilterService.Protect("Hello", null);
            Assert(s3.TokenizedText == "Hello" && s3.Tokens.Count == 0, "Boundary - Null rules");

            var s4 = FilterService.Protect("Hello", new List<string>());
            Assert(s4.TokenizedText == "Hello" && s4.Tokens.Count == 0, "Boundary - Empty rules");

            // Malformed regex
            var s5 = FilterService.Protect("Hello", new List<string> { "[a-z+|Unclosed bracket|true" });
            Assert(s5.TokenizedText == "Hello" && s5.Tokens.Count == 0, "Boundary - Malformed regex handled gracefully");

            // Restore null / empty
            Assert(FilterService.Restore(null, new Dictionary<string, string>()) == null, "Boundary - Restore null text");
            Assert(FilterService.Restore("", new Dictionary<string, string>()) == "", "Boundary - Restore empty text");
            Assert(FilterService.Restore("Hello", null) == "Hello", "Boundary - Restore null tokens");
            Assert(FilterService.Restore("Hello", new Dictionary<string, string>()) == "Hello", "Boundary - Restore empty tokens");
        }
    }
}
