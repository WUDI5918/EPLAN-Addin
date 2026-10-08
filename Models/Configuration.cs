using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace TranslationEplAddIn.Tool.Models
{
    public class Configuration
    {
        public string SelectedEngine { get; set; } = "Deepseek";
        public string DeepseekApiKey { get; set; } = string.Empty;
        public string ZhipuApiKey { get; set; } = string.Empty;
        public string WenxinApiKey { get; set; } = string.Empty;
        public string DeeplApiKey { get; set; } = string.Empty;
        public string GoogleApiKey { get; set; } = string.Empty;
        public string BaiduApiKey { get; set; } = string.Empty;
        public string DeepseekEndpoint { get; set; } = "https://api.deepseek.com/v1";
        public string ZhipuEndpoint { get; set; } = "https://open.bigmodel.cn/api/paas/v4";
        
        public string DeepseekModel { get; set; } = "deepseek-v4-flash";
        public string ZhipuModel { get; set; } = "glm-4";
        
        public List<string> RegexProtectionRules { get; set; } = new List<string> { @"<.*?>", @"PLC_.*" };
        public List<string> BlacklistedPages { get; set; } = new List<string>();
        
        public bool AppendMode { get; set; } = false;
        public string AppendSeparator { get; set; } = "\n"; // Default is newline
        
        public List<string> GlossaryPaths { get; set; } = new List<string>();
        public bool UseNativeEplanDictionary { get; set; } = false;
    }

    public class RegexRuleItem : INotifyPropertyChanged
    {
        private bool _isActive = true;
        private string _pattern = string.Empty;
        private string _description = string.Empty;

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;
                    OnPropertyChanged(nameof(IsActive));
                }
            }
        }

        public string Pattern
        {
            get => _pattern;
            set
            {
                if (_pattern != value)
                {
                    _pattern = value;
                    OnPropertyChanged(nameof(Pattern));
                }
            }
        }

        public string Description
        {
            get => _description;
            set
            {
                if (_description != value)
                {
                    _description = value;
                    OnPropertyChanged(nameof(Description));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class GlossaryDisplayItem
    {
        public string Source { get; set; }
        public string Target { get; set; }
        public string Category { get; set; } = "通用"; // 电气, 机械, 通用
    }
}
