using System;

namespace TranslationEplAddIn.Tool.Models
{
    public class TranslationMemoryItem
    {
        public string SourceHash { get; set; }
        public string SourceLang { get; set; }
        public string TargetLang { get; set; }
        public string OriginalText { get; set; }
        public string TranslatedText { get; set; }
        public string EngineUsed { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
