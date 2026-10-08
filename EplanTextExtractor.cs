using System;
using System.Collections.Generic;
using System.Linq;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.Gui;
using TranslationEplAddIn.Tool.Models;

namespace TranslationEplAddIn.Tool
{
    public static class EplanTextExtractor
    {
        public static List<TranslationItem> ExtractTexts(Project project, bool entireProject, List<Page> selectedPages, ISOCode.Language sourceLangCode, IProgress<int> progress = null)
        {
            List<TranslationItem> items = new List<TranslationItem>();
            if (project == null) return items;

            string projectFullName = project.ProjectFullName;

            if (progress != null) progress.Report(2);

            // 1. Project Properties & Structure Identifiers (Locations)
            if (entireProject)
            {
                var plcAddressMap = BuildPlcAddressToPageMap(project);
                ExtractProjectProperties(project, projectFullName, sourceLangCode, items);
                if (progress != null) progress.Report(5);
                ExtractLocationDescriptions(project, projectFullName, sourceLangCode, items);
                if (progress != null) progress.Report(10);
                ExtractPlcIOEntries(project, projectFullName, sourceLangCode, items, plcAddressMap);
                if (progress != null) progress.Report(12);
            }

            IEnumerable<Page> pagesToProcess;
            if (entireProject)
            {
                pagesToProcess = project.Pages;
            }
            else
            {
                pagesToProcess = selectedPages ?? new List<Page>();
            }
            var pagesList = pagesToProcess.ToList();
            int totalPages = pagesList.Count;

            for (int i = 0; i < totalPages; i++)
            {
                Page page = pagesList[i];
                if (page == null) continue;

                if (progress != null)
                {
                    int percent = 10 + (int)((double)(i + 1) / totalPages * 90);
                    if (percent > 100) percent = 100;
                    progress.Report(percent);
                }

                // Check page blacklist
                if (SettingsManager.Current.BlacklistedPages.Contains(page.Name))
                {
                    continue;
                }

                string pageName = page.Name;
                string pageId = page.ToStringIdentifier();

                // 2. Page Properties (e.g. Page Description)
                ExtractPageProperty(page, 11011, TextElementType.PageDescription, pageName, pageId, projectFullName, sourceLangCode, items); // PAGE_DESCRIPTION

                // 3. Objects on the Page
                foreach (Placement placement in page.AllPlacements)
                {
                    if (placement is Function func)
                    {
                        // Extract Device Tag (DT) - Excluded as structural identifier
                        // ExtractFunctionProperty(func, 20006, TextElementType.DeviceTag, pageName, pageId, projectFullName, sourceLangCode, items); // FUNC_VISIBLE_DEVICE_TAG
                        
                        // Extract Function Text
                        ExtractFunctionProperty(func, 20011, TextElementType.FunctionalText, pageName, pageId, projectFullName, sourceLangCode, items); // FUNC_TEXT
                        
                        // Extract Additional Text
                        ExtractFunctionProperty(func, 20054, TextElementType.FunctionalText, pageName, pageId, projectFullName, sourceLangCode, items); // FUNC_TEXT_ADDITIONAL

                        // Extract Technical characteristics
                        ExtractFunctionProperty(func, 20027, TextElementType.FunctionalText, pageName, pageId, projectFullName, sourceLangCode, items); // FUNC_TECHNICAL_CHARACTERISTIC

                        // Extract Terminal / Pin descriptions (FUNC_PINORTERMINALDESCRIPTION = 20225)
                        ExtractFunctionProperty(func, 20225, TextElementType.TerminalDescription, pageName, pageId, projectFullName, sourceLangCode, items);

                        // Extract Connection point descriptions (FUNC_CONNECTIONDESCRIPTION = 20023, indexed 1 to 1000)
                        ExtractFunctionProperty(func, 20023, TextElementType.TerminalDescription, pageName, pageId, projectFullName, sourceLangCode, items);
                        
                        // Extract PLC descriptions
                        if (func.FunctionCategory == Eplan.EplApi.Base.Enums.FunctionCategory.PLCTerminal || func.FunctionCategory == Eplan.EplApi.Base.Enums.FunctionCategory.PLCBox)
                        {
                            ExtractFunctionProperty(func, 20140, TextElementType.PlcDescription, pageName, pageId, projectFullName, sourceLangCode, items); // FUNC_PLC_ADDRESS_DESCRIPTION
                        }

                        // Extract Part Designation / Description 1, 2, 3 (20193, 20194, 20203)
                        ExtractFunctionProperty(func, 20193, TextElementType.PlaceholderText, pageName, pageId, projectFullName, sourceLangCode, items); // FUNC_ARTICLE_DESCR1 (Designation 1)
                        ExtractFunctionProperty(func, 20194, TextElementType.PlaceholderText, pageName, pageId, projectFullName, sourceLangCode, items); // FUNC_ARTICLE_DESCR2 (Designation 2)
                        ExtractFunctionProperty(func, 20203, TextElementType.PlaceholderText, pageName, pageId, projectFullName, sourceLangCode, items); // FUNC_ARTICLE_DESCR3 (Designation 3)

                        // Extract Part Designation 1, 2, 3 (22004, 22005, 22006) from all assigned parts (ArticleReferences)
                        if (func.ArticleReferences != null && func.ArticleReferences.Length > 0)
                        {
                            foreach (var artRef in func.ArticleReferences)
                            {
                                if (artRef == null) continue;
                                ExtractArticleReferenceProperty(artRef, 22004, TextElementType.PlaceholderText, pageName, pageId, projectFullName, sourceLangCode, items);
                                ExtractArticleReferenceProperty(artRef, 22005, TextElementType.PlaceholderText, pageName, pageId, projectFullName, sourceLangCode, items);
                                ExtractArticleReferenceProperty(artRef, 22006, TextElementType.PlaceholderText, pageName, pageId, projectFullName, sourceLangCode, items);
                            }
                        }
                    }
                    else if (placement is TextBase textBase)
                    {
                        // Graphic / Free texts or PathText
                        TextElementType elemType = (placement is PathText) ? TextElementType.PathText : TextElementType.GraphicText;
                        ISOCode.Language actualLang = sourceLangCode;
                        string rawText = string.Empty;
                        if (textBase.Contents != null)
                        {
                            rawText = GetMultiLangStringValue(textBase.Contents, sourceLangCode, out actualLang);
                        }
                        if (!string.IsNullOrEmpty(rawText) && !IsProtectedOrBlacklisted(rawText))
                        {
                            if (ShouldSkipText(rawText, sourceLangCode))
                            {
                                continue;
                            }

                            items.Add(new TranslationItem
                            {
                                SourceText = rawText,
                                ObjectId = textBase.ToStringIdentifier(),
                                ElementType = elemType,
                                PageName = pageName,
                                PageId = pageId,
                                ProjectFullName = projectFullName,
                                Status = "Pending",
                                ActualLanguageCode = actualLang
                            });
                        }
                    }
                }
            }

            return items;
        }

        public static bool WriteTexts(Project project, List<TranslationItem> items, ISOCode.Language sourceLang, ISOCode.Language targetLang, out List<string> errors)
        {
            errors = new List<string>();
            if (project == null || items == null || items.Count == 0)
            {
                errors.Add("Project is null or items list is empty.");
                return false;
            }

            uint currentDbId = project.DatabaseIdentifier;

            // Pre-fetch all PlcIO objects if any PlcDescription items need to be written back
            Dictionary<string, PlcIO> plcIoMap = null;
            if (items.Any(i => i.ElementType == TextElementType.PlcDescription))
            {
                try
                {
                    PlcIO[] plcIOs = PlcIO.GetAll(project);
                    if (plcIOs != null)
                    {
                        plcIoMap = new Dictionary<string, PlcIO>(StringComparer.OrdinalIgnoreCase);
                        foreach (var plc in plcIOs)
                        {
                            if (plc != null)
                            {
                                string id = plc.ToStringIdentifier();
                                if (!string.IsNullOrEmpty(id) && !plcIoMap.ContainsKey(id))
                                {
                                    plcIoMap[id] = plc;
                                }
                            }
                        }
                    }
                }
                catch (Exception exPlc)
                {
                    Logger.Warn($"预提取项目 PLC IO 列表失败: {exPlc.Message}");
                }
            }

            try
            {
                // Group transactions by page to optimize performance
                using (Transaction transaction = new TransactionManager().CreateTransaction())
                {
                    try
                    {
                        foreach (var item in items)
                        {
                            if (item.Status != "Translated" && item.Status != "Glossary") continue;
                            if (string.IsNullOrEmpty(item.TranslatedText)) continue;

                            StorableObject obj = null;
                            bool fromPlcMap = false;
                            try
                            {
                                string updatedObjectId = UpdateDatabaseIdInIdentifier(item.ObjectId, currentDbId);
                                try
                                {
                                    obj = StorableObject.FromStringIdentifier(updatedObjectId);
                                }
                                catch
                                {
                                    // StorableObject.FromStringIdentifier may fail for some object types (e.g. PlcIO)
                                    // Will fallback to pre-built cache below without polluting errors
                                }

                                if (obj == null && plcIoMap != null)
                                {
                                    if (plcIoMap.TryGetValue(updatedObjectId, out var plc) || plcIoMap.TryGetValue(item.ObjectId, out plc))
                                    {
                                        obj = plc;
                                        fromPlcMap = true;
                                    }
                                }

                                if (obj == null)
                                {
                                    errors.Add($"找不到对应的图纸元素 (ID: {updatedObjectId}, 文本: {item.SourceText})");
                                    continue;
                                }

                                if (obj.IsReadOnly)
                                {
                                    errors.Add($"图纸元素处于只读状态 (ID: {updatedObjectId}, 文本: {item.SourceText})");
                                    continue;
                                }

                                if (obj is Page page)
                                {
                                    WritePageProperty(page, item, sourceLang, targetLang);
                                }
                                else if (obj is Function func)
                                {
                                    WriteFunctionProperty(func, item, sourceLang, targetLang);
                                }
                                else if (obj is ArticleReference artRef)
                                {
                                    WriteArticleReferenceProperty(artRef, item, sourceLang, targetLang);
                                }
                                else if (obj is Article article)
                                {
                                    WriteArticleProperty(article, item, sourceLang, targetLang);
                                }
                                else if (obj is TextBase textBase)
                                {
                                    WriteTextBase(textBase, item, sourceLang, targetLang);
                                }
                                else if (obj is Project proj)
                                {
                                    WriteProjectProperty(proj, item, sourceLang, targetLang);
                                }
                                else if (obj is Location loc)
                                {
                                    WriteLocationProperty(loc, item, sourceLang, targetLang);
                                }
                                else if (obj is PlcIO plcIO)
                                {
                                    WritePlcIOProperty(plcIO, item, sourceLang, targetLang);
                                }
                            }
                            catch (Exception itemEx)
                            {
                                errors.Add($"回写单个元素失败 (ID: {item.ObjectId}, 文本: {item.SourceText}): {itemEx.Message}");
                            }
                            finally
                            {
                                if (obj != null && !(obj is Project) && !fromPlcMap)
                                {
                                    obj.Dispose();
                                }
                            }
                        }

                        transaction.Commit();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        transaction.Abort();
                        errors.Add($"事务整体提交失败并已回滚: {ex.Message}");
                        return false;
                    }
                }
            }
            finally
            {
                if (plcIoMap != null)
                {
                    foreach (var plc in plcIoMap.Values)
                    {
                        try { plc?.Dispose(); } catch { }
                    }
                }
            }
        }

        private static string UpdateDatabaseIdInIdentifier(string objectId, uint currentDbId)
        {
            if (string.IsNullOrEmpty(objectId)) return objectId;
            int firstSlash = objectId.IndexOf('/');
            if (firstSlash > 0)
            {
                return currentDbId.ToString() + objectId.Substring(firstSlash);
            }
            return objectId;
        }

        private static void ExtractProjectProperties(Project project, string projectFullName, ISOCode.Language sourceLangCode, List<TranslationItem> items)
        {
            // Define commonly used Project Property IDs
            // 10011: PROJ_DESCRIPTION
            // 10012: PROJ_COMPANY
            // 10013: PROJ_COMMISSION
            // 10014: PROJ_CUSTOMER
            // 10015: PROJ_CUSTOMER_COMPANY
            // 10016: PROJ_CUSTOMER_ADDRESS_1
            // 10022: PROJ_PROJECT_ADDRESS_1
            // 10901: PROJ_NUMBER
            // 10008: PROJ_CREATOR
            int[] commonProjPropIds = new[] { 10011, 10012, 10013, 10014, 10015, 10016, 10022, 10901, 10008 };

            foreach (int propId in commonProjPropIds)
            {
                try
                {
                    if (!project.Properties.Exists(propId)) continue;
                    PropertyValue propVal = project.Properties[propId];
                    if (propVal.IsEmpty) continue;

                    ISOCode.Language actualLang;
                    string text = GetMultiLangStringValue(propVal, sourceLangCode, out actualLang);

                    if (!string.IsNullOrEmpty(text) && !IsProtectedOrBlacklisted(text))
                    {
                        if (ShouldSkipText(text, sourceLangCode))
                        {
                            continue;
                        }

                        PropertyDefinition def = new PropertyDefinition(propId);
                        bool isMultiLang = def.Type == PropertyDefinition.PropertyType.MultilangString;

                        items.Add(new TranslationItem
                        {
                            SourceText = text,
                            ObjectId = project.ToStringIdentifier(),
                            PropertyId = propId,
                            ElementType = TextElementType.ProjectAttribute,
                            PageName = string.Empty,
                            PageId = "",
                            ProjectFullName = projectFullName,
                            Status = "Pending",
                            ActualLanguageCode = actualLang,
                            IsMultiLang = isMultiLang
                        });
                    }
                }
                catch
                {
                    // Skip if property is not supported on this project
                }
            }
        }

        private static void ExtractLocationDescriptions(Project project, string projectFullName, ISOCode.Language sourceLangCode, List<TranslationItem> items)
        {
            Project.Hierarchy[] hierarchies = new[]
            {
                Project.Hierarchy.Plant,       // Higher-level function (=)
                Project.Hierarchy.Place,       // Mounting location (+)
                Project.Hierarchy.Document,    // Document type (&)
                Project.Hierarchy.Installation,// Installation site (++)
                Project.Hierarchy.UserDef      // User-defined
            };

            foreach (var hierarchy in hierarchies)
            {
                try
                {
                    Location[] locObjects = project.GetLocationObjects(hierarchy);
                    if (locObjects == null || locObjects.Length == 0) continue;

                    foreach (var loc in locObjects)
                    {
                        if (loc == null) continue;

                        // LOCATION_DESCRIPTION is property ID 18301
                        int propId = 18301;
                        try
                        {
                            if (!loc.Properties.Exists(propId)) continue;
                            PropertyValue propVal = loc.Properties[propId];
                            if (propVal.IsEmpty) continue;

                            ISOCode.Language actualLang;
                            string text = GetMultiLangStringValue(propVal, sourceLangCode, out actualLang);

                            if (!string.IsNullOrEmpty(text) && !IsProtectedOrBlacklisted(text))
                            {
                                if (ShouldSkipText(text, sourceLangCode))
                                {
                                    continue;
                                }

                                PropertyDefinition def = new PropertyDefinition(propId);
                                bool isMultiLang = def.Type == PropertyDefinition.PropertyType.MultilangString;

                                string hierarchyName = GetHierarchyName(hierarchy);
                                items.Add(new TranslationItem
                                {
                                    SourceText = text,
                                    ObjectId = loc.ToStringIdentifier(),
                                    PropertyId = propId,
                                    ElementType = TextElementType.PageAttribute,
                                    PageName = string.Empty,
                                    PageId = loc.Name,
                                    ProjectFullName = projectFullName,
                                    Status = "Pending",
                                    ActualLanguageCode = actualLang,
                                    IsMultiLang = isMultiLang
                                });
                            }
                        }
                        catch
                        {
                            // Skip this location description
                        }
                    }
                }
                catch
                {
                    // Skip hierarchy level
                }
            }
        }

        private static Dictionary<string, string> BuildPlcAddressToPageMap(Project project)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (Page page in project.Pages)
                {
                    if (page == null) continue;
                    string pageName = page.Name;
                    foreach (Placement placement in page.AllPlacements)
                    {
                        if (placement is Function func)
                        {
                            if (func.FunctionCategory == Eplan.EplApi.Base.Enums.FunctionCategory.PLCTerminal || 
                                func.FunctionCategory == Eplan.EplApi.Base.Enums.FunctionCategory.PLCBox)
                            {
                                if (func.Properties.Exists(20400)) // FUNC_PLCADDRESS
                                {
                                    string address = func.Properties[20400].ToString()?.Trim();
                                    if (!string.IsNullOrEmpty(address) && !map.ContainsKey(address))
                                    {
                                        map[address] = pageName;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Ignore gracefully
            }
            return map;
        }

        private static void ExtractPlcIOEntries(Project project, string projectFullName, ISOCode.Language sourceLangCode, List<TranslationItem> items, Dictionary<string, string> plcAddressMap)
        {
            try
            {
                PlcIO[] plcIOs = PlcIO.GetAll(project);
                if (plcIOs == null || plcIOs.Length == 0) return;

                foreach (var plcIO in plcIOs)
                {
                    if (plcIO == null) continue;

                    MultiLangString mls = plcIO.FunctionText;
                    if (mls != null)
                    {
                        ISOCode.Language actualLang;
                        string text = GetMultiLangStringValue(mls, sourceLangCode, out actualLang);
                        if (!string.IsNullOrEmpty(text) && !IsProtectedOrBlacklisted(text))
                        {
                            if (ShouldSkipText(text, sourceLangCode))
                            {
                                continue;
                            }

                            string address = plcIO.Address?.Trim();
                            string pageName = string.Empty;
                            if (!string.IsNullOrEmpty(address) && plcAddressMap != null && plcAddressMap.TryGetValue(address, out string mappedPage))
                            {
                                pageName = mappedPage;
                            }

                            items.Add(new TranslationItem
                            {
                                SourceText = text,
                                ObjectId = plcIO.ToStringIdentifier(),
                                PropertyId = 0,
                                ElementType = TextElementType.PlcDescription,
                                PageName = pageName,
                                PageId = plcIO.Address,
                                ProjectFullName = projectFullName,
                                Status = "Pending",
                                ActualLanguageCode = actualLang,
                                IsMultiLang = true
                            });
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Skip gracefully
            }
        }

        private static string GetHierarchyName(Project.Hierarchy hierarchy)
        {
            switch (hierarchy)
            {
                case Project.Hierarchy.Plant: return "高层代号 =";
                case Project.Hierarchy.Place: return "位置代号 +";
                case Project.Hierarchy.Document: return "文档类型 &";
                case Project.Hierarchy.Installation: return "安装地点 ++";
                case Project.Hierarchy.UserDef: return "用户自定义";
                default: return hierarchy.ToString();
            }
        }

        private static void ExtractPageProperty(Page page, int propId, TextElementType type, string pageName, string pageId, string projectFullName, ISOCode.Language sourceLangCode, List<TranslationItem> items)
        {
            try
            {
                if (!page.Properties.Exists(propId)) return;
                PropertyValue propVal = page.Properties[propId];
                if (!propVal.IsEmpty)
                {
                    ISOCode.Language actualLang;
                    string text = GetMultiLangStringValue(propVal, sourceLangCode, out actualLang);
                    if (!string.IsNullOrEmpty(text) && !IsProtectedOrBlacklisted(text))
                    {
                        if (ShouldSkipText(text, sourceLangCode))
                        {
                            return;
                        }

                        PropertyDefinition def = new PropertyDefinition(propId);
                        bool isMultiLang = def.Type == PropertyDefinition.PropertyType.MultilangString;

                        items.Add(new TranslationItem
                        {
                            SourceText = text,
                            ObjectId = page.ToStringIdentifier(),
                            PropertyId = propId,
                            ElementType = type,
                            PageName = pageName,
                            PageId = pageId,
                            ProjectFullName = projectFullName,
                            Status = "Pending",
                            ActualLanguageCode = actualLang,
                            IsMultiLang = isMultiLang
                        });
                    }
                }
            }
            catch (Exception)
            {
                // Property not valid for this page/object, skip gracefully
            }
        }

        private static void ExtractArticleReferenceProperty(ArticleReference artRef, int propId, TextElementType type, string pageName, string pageId, string projectFullName, ISOCode.Language sourceLangCode, List<TranslationItem> items)
        {
            try
            {
                PropertyValue propVal = null;
                StorableObject targetObj = null;

                if (artRef.Properties.Exists(propId))
                {
                    var val = artRef.Properties[propId];
                    if (val != null && !val.IsEmpty)
                    {
                        propVal = val;
                        targetObj = artRef;
                    }
                }

                // If not found or empty on ArticleReference, fall back to the Article stored in the project
                if (propVal == null && artRef.Article != null)
                {
                    if (artRef.Article.Properties.Exists(propId))
                    {
                        var val = artRef.Article.Properties[propId];
                        if (val != null && !val.IsEmpty)
                        {
                            propVal = val;
                            targetObj = artRef.Article;
                        }
                    }
                }

                if (propVal != null && targetObj != null)
                {
                    ISOCode.Language actualLang;
                    string text = GetMultiLangStringValue(propVal, sourceLangCode, out actualLang);
                    if (!string.IsNullOrEmpty(text) && !IsProtectedOrBlacklisted(text))
                    {
                        if (ShouldSkipText(text, sourceLangCode))
                        {
                            return;
                        }

                        PropertyDefinition def = new PropertyDefinition(propId);
                        bool isMultiLang = def.Type == PropertyDefinition.PropertyType.MultilangString;

                        // Ensure we do not extract the same Article property multiple times
                        string targetObjectId = targetObj.ToStringIdentifier();
                        if (items.Any(i => i.ObjectId == targetObjectId && i.PropertyId == propId))
                        {
                            return;
                        }

                        items.Add(new TranslationItem
                        {
                            SourceText = text,
                            ObjectId = targetObjectId,
                            PropertyId = propId,
                            ElementType = type,
                            PageName = pageName,
                            PageId = pageId,
                            ProjectFullName = projectFullName,
                            Status = "Pending",
                            ActualLanguageCode = actualLang,
                            IsMultiLang = isMultiLang
                        });
                    }
                }
            }
            catch (Exception)
            {
                // Skip gracefully
            }
        }

        private static void ExtractFunctionProperty(Function func, int propId, TextElementType type, string pageName, string pageId, string projectFullName, ISOCode.Language sourceLangCode, List<TranslationItem> items)
        {
            // Explicitly exclude Part Brand (20921) and Part Number (20100) from translation
            if (propId == 20100 || propId == 20921) return;

            try
            {
                if (!func.Properties.Exists(propId)) return;

                PropertyDefinition def = new PropertyDefinition(propId);
                if (def.IsIndexed)
                {
                    int max = def.MaxIndex;
                    if (max <= 0) max = def.UpperBound;
                    if (max <= 0) max = 128; // fallback limit

                    for (int idx = 1; idx <= max; idx++)
                    {
                        try
                        {
                            PropertyValue propVal = func.Properties[propId, idx];
                            if (propVal != null && !propVal.IsEmpty)
                            {
                                ISOCode.Language actualLang;
                                string text = GetMultiLangStringValue(propVal, sourceLangCode, out actualLang);
                                if (!string.IsNullOrEmpty(text) && !IsProtectedOrBlacklisted(text))
                                {
                                    if (ShouldSkipText(text, sourceLangCode))
                                    {
                                        continue;
                                    }

                                    bool isMultiLang = def.Type == PropertyDefinition.PropertyType.MultilangString;

                                    items.Add(new TranslationItem
                                    {
                                        SourceText = text,
                                        ObjectId = func.ToStringIdentifier(),
                                        PropertyId = propId,
                                        PropertyIndex = idx,
                                        ElementType = type,
                                        PageName = pageName,
                                        PageId = pageId,
                                        ProjectFullName = projectFullName,
                                        Status = "Pending",
                                        ActualLanguageCode = actualLang,
                                        IsMultiLang = isMultiLang
                                    });
                                }
                            }
                        }
                        catch (PropertyNotFoundException) { }
                        catch (IndexedPropertyException) { }
                        catch (InvalidIndexException) { }
                    }
                }
                else
                {
                    PropertyValue propVal = func.Properties[propId];
                    if (!propVal.IsEmpty)
                    {
                        ISOCode.Language actualLang;
                        string text = GetMultiLangStringValue(propVal, sourceLangCode, out actualLang);
                        if (!string.IsNullOrEmpty(text) && !IsProtectedOrBlacklisted(text))
                        {
                            if (ShouldSkipText(text, sourceLangCode))
                            {
                                return;
                            }

                            bool isMultiLang = def.Type == PropertyDefinition.PropertyType.MultilangString;

                            items.Add(new TranslationItem
                            {
                                SourceText = text,
                                ObjectId = func.ToStringIdentifier(),
                                PropertyId = propId,
                                ElementType = type,
                                PageName = pageName,
                                PageId = pageId,
                                ProjectFullName = projectFullName,
                                Status = "Pending",
                                ActualLanguageCode = actualLang,
                                IsMultiLang = isMultiLang
                            });
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Property not valid for this function/object, skip gracefully
            }
        }

        private static string GetMultiLangStringValue(PropertyValue propVal, ISOCode.Language sourceLangCode, out ISOCode.Language actualLangCode)
        {
            actualLangCode = sourceLangCode;
            if (propVal.IsEmpty) return string.Empty;
            MultiLangString mls = propVal.ToMultiLangString();
            if (mls != null)
            {
                return GetMultiLangStringValue(mls, sourceLangCode, out actualLangCode);
            }
            return propVal.ToString();
        }

        private static string GetMultiLangStringValue(MultiLangString mls, ISOCode.Language sourceLangCode, out ISOCode.Language actualLangCode)
        {
            actualLangCode = sourceLangCode;
            if (mls == null) return string.Empty;

            // Try the selected source language first
            string text = mls.GetString(sourceLangCode);
            if (!string.IsNullOrEmpty(text))
            {
                return text;
            }

            // If empty, try the neutral language (ISOCode.Language.L___)
            text = mls.GetString(ISOCode.Language.L___);
            if (!string.IsNullOrEmpty(text))
            {
                actualLangCode = ISOCode.Language.L___;
                return text;
            }

            // Try English
            text = mls.GetString(ISOCode.Language.L_en_US);
            if (!string.IsNullOrEmpty(text))
            {
                actualLangCode = ISOCode.Language.L_en_US;
                return text;
            }

            // Try German
            text = mls.GetString(ISOCode.Language.L_de_DE);
            if (!string.IsNullOrEmpty(text))
            {
                actualLangCode = ISOCode.Language.L_de_DE;
                return text;
            }

            var otherLangs = new[]
            {
                ISOCode.Language.L_zh_TW,
                ISOCode.Language.L_zh_HK,
                ISOCode.Language.L_en_EN,
                ISOCode.Language.L_ru_RU,
                ISOCode.Language.L_fr_FR,
                ISOCode.Language.L_es_ES,
                ISOCode.Language.L_it_IT,
                ISOCode.Language.L_cs_CZ,
                ISOCode.Language.L_da_DK,
                ISOCode.Language.L_hu_HU,
                ISOCode.Language.L_ja_JP,
                ISOCode.Language.L_ko_KR,
                ISOCode.Language.L_nl_NL,
                ISOCode.Language.L_pl_PL,
                ISOCode.Language.L_pt_BR,
                ISOCode.Language.L_pt_PT,
                ISOCode.Language.L_ro_RO,
                ISOCode.Language.L_sv_SE,
                ISOCode.Language.L_tr_TR
            };

            foreach (var lang in otherLangs)
            {
                text = mls.GetString(lang);
                if (!string.IsNullOrEmpty(text))
                {
                    actualLangCode = lang;
                    return text;
                }
            }

            // Fallback to GetAsString() but clean EPLAN control tags
            string raw = mls.GetAsString();
            if (!string.IsNullOrEmpty(raw))
            {
                return CleanEplanMultiLangString(raw);
            }

            return string.Empty;
        }

        private static string CleanEplanMultiLangString(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            if (!raw.Contains("@")) return raw;

            // Example raw: "??_??@Plug-in Terminal (Feed) ;"
            // or "en_US@English text;zh_CN@中文文本;"
            var parts = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> texts = new List<string>();
            foreach (var part in parts)
            {
                int idx = part.IndexOf('@');
                if (idx >= 0 && idx < part.Length - 1)
                {
                    string textVal = part.Substring(idx + 1).Trim();
                    if (!string.IsNullOrEmpty(textVal))
                    {
                        texts.Add(textVal);
                    }
                }
                else
                {
                    string textVal = part.Trim();
                    if (!string.IsNullOrEmpty(textVal))
                    {
                        texts.Add(textVal);
                    }
                }
            }

            if (texts.Count > 0)
            {
                return texts[0];
            }

            return raw;
        }

        private static void WritePageProperty(Page page, TranslationItem item, ISOCode.Language sourceLang, ISOCode.Language targetLang)
        {
            try
            {
                if (!page.Properties.Exists(item.PropertyId)) return;
                PropertyValue propVal = page.Properties[item.PropertyId];
                if (propVal != null && !propVal.IsEmpty)
                {
                    PropertyDefinition def = new PropertyDefinition(item.PropertyId);
                    if (def.Type == PropertyDefinition.PropertyType.MultilangString)
                    {
                        MultiLangString mls = propVal.ToMultiLangString();
                        if (mls != null)
                        {
                            ApplyTranslationStrategy(mls, item.SourceText, item.TranslatedText, sourceLang, targetLang, item.ActualLanguageCode);
                            propVal.Set(mls);
                        }
                    }
                    else
                    {
                        propVal.Set(item.TranslatedText);
                    }
                }
            }
            catch (Exception)
            {
                // Skip writing if property is not writable or valid
            }
        }

        private static void WriteFunctionProperty(Function func, TranslationItem item, ISOCode.Language sourceLang, ISOCode.Language targetLang)
        {
            try
            {
                if (!func.Properties.Exists(item.PropertyId)) return;

                PropertyValue propVal;
                if (item.PropertyIndex > 0)
                {
                    propVal = func.Properties[item.PropertyId, item.PropertyIndex];
                }
                else
                {
                    propVal = func.Properties[item.PropertyId];
                }

                if (propVal != null && !propVal.IsEmpty)
                {
                    PropertyDefinition def = new PropertyDefinition(item.PropertyId);
                    if (def.Type == PropertyDefinition.PropertyType.MultilangString)
                    {
                        MultiLangString mls = propVal.ToMultiLangString();
                        if (mls != null)
                        {
                            ApplyTranslationStrategy(mls, item.SourceText, item.TranslatedText, sourceLang, targetLang, item.ActualLanguageCode);
                            propVal.Set(mls);
                        }
                    }
                    else
                    {
                        propVal.Set(item.TranslatedText);
                    }
                }
            }
            catch (Exception)
            {
                // Skip writing if property is not writable or valid
            }
        }

        private static void WriteArticleReferenceProperty(ArticleReference artRef, TranslationItem item, ISOCode.Language sourceLang, ISOCode.Language targetLang)
        {
            try
            {
                if (!artRef.Properties.Exists(item.PropertyId)) return;

                PropertyValue propVal = artRef.Properties[item.PropertyId];
                if (propVal != null && !propVal.IsEmpty)
                {
                    PropertyDefinition def = new PropertyDefinition(item.PropertyId);
                    if (def.Type == PropertyDefinition.PropertyType.MultilangString)
                    {
                        MultiLangString mls = propVal.ToMultiLangString();
                        if (mls != null)
                        {
                            ApplyTranslationStrategy(mls, item.SourceText, item.TranslatedText, sourceLang, targetLang, item.ActualLanguageCode);
                            propVal.Set(mls);
                        }
                    }
                    else
                    {
                        propVal.Set(item.TranslatedText);
                    }
                }
            }
            catch (Exception)
            {
                // Skip gracefully
            }
        }

        private static void WriteArticleProperty(Article article, TranslationItem item, ISOCode.Language sourceLang, ISOCode.Language targetLang)
        {
            try
            {
                if (!article.Properties.Exists(item.PropertyId)) return;

                PropertyValue propVal = article.Properties[item.PropertyId];
                if (propVal != null && !propVal.IsEmpty)
                {
                    PropertyDefinition def = new PropertyDefinition(item.PropertyId);
                    if (def.Type == PropertyDefinition.PropertyType.MultilangString)
                    {
                        MultiLangString mls = propVal.ToMultiLangString();
                        if (mls != null)
                        {
                            ApplyTranslationStrategy(mls, item.SourceText, item.TranslatedText, sourceLang, targetLang, item.ActualLanguageCode);
                            propVal.Set(mls);
                        }
                    }
                    else
                    {
                        propVal.Set(item.TranslatedText);
                    }
                }
            }
            catch (Exception)
            {
                // Skip gracefully
            }
        }

        private static void WriteTextBase(TextBase textBase, TranslationItem item, ISOCode.Language sourceLang, ISOCode.Language targetLang)
        {
            try
            {
                MultiLangString mls = textBase.Contents ?? new MultiLangString();
                ApplyTranslationStrategy(mls, item.SourceText, item.TranslatedText, sourceLang, targetLang, item.ActualLanguageCode);
                textBase.Contents = mls;
            }
            catch (Exception)
            {
                // Skip
            }
        }

        private static void WriteProjectProperty(Project project, TranslationItem item, ISOCode.Language sourceLang, ISOCode.Language targetLang)
        {
            try
            {
                if (!project.Properties.Exists(item.PropertyId)) return;
                PropertyValue propVal = project.Properties[item.PropertyId];
                if (propVal != null && !propVal.IsEmpty)
                {
                    PropertyDefinition def = new PropertyDefinition(item.PropertyId);
                    if (def.Type == PropertyDefinition.PropertyType.MultilangString)
                    {
                        MultiLangString mls = propVal.ToMultiLangString();
                        if (mls != null)
                        {
                            ApplyTranslationStrategy(mls, item.SourceText, item.TranslatedText, sourceLang, targetLang, item.ActualLanguageCode);
                            propVal.Set(mls);
                        }
                    }
                    else
                    {
                        propVal.Set(item.TranslatedText);
                    }
                }
            }
            catch (Exception)
            {
                // Skip
            }
        }

        private static void WriteLocationProperty(Location loc, TranslationItem item, ISOCode.Language sourceLang, ISOCode.Language targetLang)
        {
            try
            {
                if (!loc.Properties.Exists(item.PropertyId)) return;
                PropertyValue propVal = loc.Properties[item.PropertyId];
                if (propVal != null && !propVal.IsEmpty)
                {
                    PropertyDefinition def = new PropertyDefinition(item.PropertyId);
                    if (def.Type == PropertyDefinition.PropertyType.MultilangString)
                    {
                        MultiLangString mls = propVal.ToMultiLangString();
                        if (mls != null)
                        {
                            ApplyTranslationStrategy(mls, item.SourceText, item.TranslatedText, sourceLang, targetLang, item.ActualLanguageCode);
                            propVal.Set(mls);
                        }
                    }
                    else
                    {
                        propVal.Set(item.TranslatedText);
                    }
                }
            }
            catch (Exception)
            {
                // Skip
            }
        }

        private static void WritePlcIOProperty(PlcIO plcIO, TranslationItem item, ISOCode.Language sourceLang, ISOCode.Language targetLang)
        {
            try
            {
                MultiLangString mls = plcIO.FunctionText ?? new MultiLangString();
                ApplyTranslationStrategy(mls, item.SourceText, item.TranslatedText, sourceLang, targetLang, item.ActualLanguageCode);
                plcIO.FunctionText = mls;
            }
            catch (Exception)
            {
                // Skip
            }
        }

        private static void ApplyTranslationStrategy(MultiLangString mls, string sourceText, string translatedText, ISOCode.Language sourceLangCode, ISOCode.Language targetLangCode, ISOCode.Language actualLangCode)
        {
            if (mls == null) return;

            if (SettingsManager.Current.AppendMode)
            {
                string originalSource = mls.GetString(actualLangCode);
                if (string.IsNullOrEmpty(originalSource)) originalSource = sourceText;

                string separator = SettingsManager.Current.AppendSeparator;
                // Avoid double appending
                if (!originalSource.Contains(translatedText))
                {
                    string appended = originalSource + separator + translatedText;
                    mls.AddString(actualLangCode, appended);
                }
            }
            else
            {
                // Override mode: write directly to target language slot, leaving original source language slot untouched
                // To prevent EPLAN from automatically copying or overwriting, we explicitly re-write the original source text
                mls.AddString(actualLangCode, sourceText);
                if (actualLangCode != sourceLangCode)
                {
                    mls.AddString(sourceLangCode, sourceText);
                }
                mls.AddString(targetLangCode, translatedText);
            }
        }

        private static bool IsProtectedOrBlacklisted(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;

            string trimmed = text.Trim();
            if (string.IsNullOrEmpty(trimmed)) return true;

            // Apply regular expression protection rules:
            // Only skip extraction if the ENTIRE text matches a protection rule (e.g. pure tag or un-translatable code).
            // Partial substring matches must NOT be skipped here; they are tokenized and protected during translation by FilterService.
            foreach (var rule in SettingsManager.Current.RegexProtectionRules)
            {
                if (string.IsNullOrWhiteSpace(rule)) continue;
                if (FilterService.ParseRule(rule, out string pattern, out _, out bool isActive))
                {
                    if (!isActive || string.IsNullOrEmpty(pattern)) continue;
                    try
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(trimmed, pattern);
                        if (match.Success && match.Index == 0 && match.Length == trimmed.Length)
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Invalid regex rule, ignore
                    }
                }
            }
            return false;
        }

        private static bool ContainsChinese(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            // Chinese character range: \u4e00-\u9fa5
            return System.Text.RegularExpressions.Regex.IsMatch(text, @"[\u4e00-\u9fa5]");
        }

        private static bool ContainsEnglish(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            // English character range: a-zA-Z
            return System.Text.RegularExpressions.Regex.IsMatch(text, @"[a-zA-Z]");
        }

        private static bool IsSystemGeneratedOrNonTranslatable(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            string trimmed = text.Trim();

            // 如果包含中文字符，则属于人工说明性文本，不属于系统生成或不可翻译文本
            if (ContainsChinese(trimmed))
            {
                return false;
            }

            // 1. 自动交叉引用 (Automatic cross-references, e.g. /3.1, /Page.1, /A.1)
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^/[a-zA-Z0-9_\-\.\[\]]+$"))
            {
                return true;
            }

            // 2. 触点图文本 (Contact image text, e.g. "13 14 /2.5")
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[0-9\s/\\.,\-\r\n]+$"))
            {
                return true;
            }

            // 3. 纯数字
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[0-9\.]+$"))
            {
                return true;
            }

            // 4. 自动线号 / 短编码 (e.g. 24V, GND, W101, X1:1)
            if (trimmed.Length <= 8 && !trimmed.Contains(" "))
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"\d"))
                {
                    return true;
                }

                string upper = trimmed.ToUpper();
                if (upper == "GND" || upper == "PE" || upper == "L" || upper == "N" || 
                    upper == "L1" || upper == "L2" || upper == "L3" || upper == "L+" || 
                    upper == "L-" || upper == "COM")
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldSkipText(string text, ISOCode.Language sourceLangCode)
        {
            if (string.IsNullOrEmpty(text)) return true;

            // 规则 1：排除自动渲染的或非翻译文本
            if (IsSystemGeneratedOrNonTranslatable(text))
            {
                return true;
            }

            // 规则 2：源语言为中文时，必须包含中文字符才提取
            if (sourceLangCode == ISOCode.Language.L_zh_CN && !ContainsChinese(text))
            {
                return true;
            }

            // 规则 3：源语言为英文时，必须包含英文字符才提取
            if ((sourceLangCode == ISOCode.Language.L_en_US || sourceLangCode == ISOCode.Language.L_en_EN) && !ContainsEnglish(text))
            {
                return true;
            }

            return false;
        }
    }
}
