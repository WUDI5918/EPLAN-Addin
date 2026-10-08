using System;
using Eplan.EplApi.Base;

namespace TranslationEplAddIn.Tool.Models
{
    public enum TextElementType
    {
        DeviceTag,
        FunctionalText,
        TerminalDescription,
        CableDescription,
        PlcDescription,
        PageDescription,
        GraphicText,
        PathText,
        ProjectAttribute,
        PageAttribute,
        PlaceholderText
    }

    public class TranslationItem
    {
        public string SourceText { get; set; } = string.Empty;
        public string TranslatedText { get; set; } = string.Empty;
        public string ObjectId { get; set; } = string.Empty; // EPLAN StorableObject ID
        public int PropertyId { get; set; } // EPLAN Property ID (if applicable)
        public int PropertyIndex { get; set; } // EPLAN Property Index (if applicable)
        public TextElementType ElementType { get; set; }
        
        public string PageName { get; set; } = string.Empty;
        public string PageId { get; set; } = string.Empty;
        public string ProjectFullName { get; set; } = string.Empty;
        
        // Status fields for proofreading panel
        public string Status { get; set; } = "Pending"; // Pending, Translated, Error, Glossary
        public string EngineUsed { get; set; } = string.Empty;

        // Tracks the actual language slot the text was extracted from
        public ISOCode.Language ActualLanguageCode { get; set; }

        // Tracks whether this property supports multi-language input in EPLAN
        public bool IsMultiLang { get; set; } = true;
    }
}
