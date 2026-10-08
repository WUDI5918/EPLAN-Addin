using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Xml;

namespace TranslationEplAddIn.Tool
{
    public static class GlossaryManager
    {
        public static Dictionary<string, string> Glossary { get; private set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void LoadGlossary(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return;
            }

            try
            {
                string ext = Path.GetExtension(filePath).ToLower();
                switch (ext)
                {
                    case ".csv":
                        LoadCsv(filePath);
                        break;
                    case ".xlsx":
                        LoadXlsx(filePath);
                        break;
                    case ".tmx":
                        LoadTmx(filePath);
                        break;
                    case ".tbx":
                        LoadTbx(filePath);
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error loading glossary {filePath}");
            }
        }

        public static string Lookup(string sourceText)
        {
            if (string.IsNullOrEmpty(sourceText)) return null;
            if (Glossary.TryGetValue(sourceText.Trim(), out string translation))
            {
                return translation;
            }
            return null;
        }

        private static void LoadCsv(string filePath)
        {
            using (var reader = new StreamReader(filePath))
            {
                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    string[] parts = line.Split(new[] { ',', ';' }, 2);
                    if (parts.Length == 2)
                    {
                        string source = parts[0].Trim().Trim('"');
                        string target = parts[1].Trim().Trim('"');
                        if (!string.IsNullOrEmpty(source))
                        {
                            Glossary[source] = target;
                        }
                    }
                }
            }
        }

        private static void LoadXlsx(string filePath)
        {
            // Simple pure-dotnet xlsx reader using ZipArchive to parse sheet1.xml and sharedStrings.xml
            using (ZipArchive archive = ZipFile.OpenRead(filePath))
            {
                // 1. Read Shared Strings
                var sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml");
                if (sharedStringsEntry == null) return;

                List<string> sharedStrings = new List<string>();
                using (var stream = sharedStringsEntry.Open())
                {
                    XmlDocument xml = new XmlDocument();
                    xml.Load(stream);
                    XmlNodeList stringNodes = xml.GetElementsByTagName("t");
                    foreach (XmlNode node in stringNodes)
                    {
                        sharedStrings.Add(node.InnerText);
                    }
                }

                // 2. Read Sheet1 values
                var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
                if (sheetEntry == null) return;

                using (var stream = sheetEntry.Open())
                {
                    XmlDocument xml = new XmlDocument();
                    xml.Load(stream);
                    XmlNamespaceManager nsmgr = new XmlNamespaceManager(xml.NameTable);
                    nsmgr.AddNamespace("x", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");

                    XmlNodeList rowNodes = xml.SelectNodes("//x:row", nsmgr);
                    foreach (XmlNode row in rowNodes)
                    {
                        XmlNodeList cellNodes = row.SelectNodes("x:c", nsmgr);
                        if (cellNodes.Count >= 2)
                        {
                            string sourceVal = GetCellValue(cellNodes[0], sharedStrings);
                            string targetVal = GetCellValue(cellNodes[1], sharedStrings);

                            if (!string.IsNullOrEmpty(sourceVal) && !string.IsNullOrEmpty(targetVal))
                            {
                                Glossary[sourceVal] = targetVal;
                            }
                        }
                    }
                }
            }
        }

        private static string GetCellValue(XmlNode cellNode, List<string> sharedStrings)
        {
            if (cellNode == null) return null;
            XmlAttribute typeAttr = cellNode.Attributes["t"];
            
            XmlNamespaceManager nsmgr = new XmlNamespaceManager(cellNode.OwnerDocument.NameTable);
            nsmgr.AddNamespace("x", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            XmlNode valNode = cellNode.SelectSingleNode("x:v", nsmgr);
            
            if (valNode == null) return null;
            string rawVal = valNode.InnerText;

            if (typeAttr != null && typeAttr.Value == "s") // Shared String
            {
                if (int.TryParse(rawVal, out int index) && index >= 0 && index < sharedStrings.Count)
                {
                    return sharedStrings[index];
                }
            }
            return rawVal;
        }

        private static void LoadTmx(string filePath)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(filePath);
            XmlNodeList tuNodes = doc.GetElementsByTagName("tu");

            foreach (XmlNode tu in tuNodes)
            {
                XmlNodeList tuvList = tu.SelectNodes("tuv");
                string source = null;
                string target = null;

                foreach (XmlNode tuv in tuvList)
                {
                    string lang = tuv.Attributes["xml:lang"]?.Value ?? tuv.Attributes["lang"]?.Value;
                    XmlNode seg = tuv.SelectSingleNode("seg");
                    if (seg == null) continue;

                    if (lang != null && (lang.StartsWith("zh") || lang.StartsWith("ZH")))
                    {
                        source = seg.InnerText.Trim();
                    }
                    else if (lang != null && (lang.StartsWith("en") || lang.StartsWith("EN") || lang.StartsWith("de") || lang.StartsWith("DE")))
                    {
                        target = seg.InnerText.Trim();
                    }
                }

                if (!string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(target))
                {
                    Glossary[source] = target;
                }
            }
        }

        private static void LoadTbx(string filePath)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(filePath);
            XmlNodeList termEntries = doc.GetElementsByTagName("termEntry");

            foreach (XmlNode entry in termEntries)
            {
                XmlNodeList langSets = entry.SelectNodes("langSet");
                string source = null;
                string target = null;

                foreach (XmlNode langSet in langSets)
                {
                    string lang = langSet.Attributes["xml:lang"]?.Value ?? langSet.Attributes["lang"]?.Value;
                    XmlNode termNode = langSet.SelectSingleNode(".//term");
                    if (termNode == null) continue;

                    if (lang != null && (lang.StartsWith("zh") || lang.StartsWith("ZH")))
                    {
                        source = termNode.InnerText.Trim();
                    }
                    else if (lang != null && (lang.StartsWith("en") || lang.StartsWith("EN") || lang.StartsWith("de") || lang.StartsWith("DE")))
                    {
                        target = termNode.InnerText.Trim();
                    }
                }

                if (!string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(target))
                {
                    Glossary[source] = target;
                }
            }
        }
    }
}
