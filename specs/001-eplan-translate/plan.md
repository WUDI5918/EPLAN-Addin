# Technical Implementation Plan: EPLAN Translation Add-in

**Branch**: `001-eplan-translate` | **Date**: 2026-06-18 | **Spec**: [specs/001-eplan-translate/spec.md](file:///c:/Users/62789/source/repos/TranslationEplAddIn.Tool/specs/001-eplan-translate/spec.md)

**Input**: Feature specification from `specs/001-eplan-translate/spec.md`

## Summary

The EPLAN Translation Add-in is a C# .NET Framework 4.6.2 class library loaded by EPLAN Electric P8. It implements the EPLAN API to extract drawing texts (DT, functional texts, page properties, plc descriptions), filter them using user-defined regex rules and glossary, translate them using modern LLMs (Deepseek, Zhipu, Wenxin) and MT engines, and write them back either directly or into the native EPLAN project dictionary. The plan details the UI layout (Ribbon menus & WPF Dockable Windows), asynchronous pipeline, backup/undo transaction flow, and drawing-coordinate focus mechanism.

## Technical Context

**Language/Version**: C# / .NET Framework 4.6.2 (matching the csproj project target)

**Primary Dependencies**: 
- Eplan.EplApi.Baseu.dll, Eplan.EplApi.Guiu.dll, Eplan.EplApi.DataModelu.dll, Eplan.EplApi.HEServicesu.dll (v2.7 to 2026)
- System.Net.Http (for REST API requests to LLM/MT engines)
- Newtonsoft.Json (for JSON serialization of API payloads)
- System.Security (for DPAPI encryption of local API keys)

**Storage**: Local settings encrypted with DPAPI (saved in AppData folder); Glossary database loaded from CSV, Excel (using OpenXML or ExcelDataReader), or TMX/TBX files.

**Testing**: 
- Unit tests using NUnit for non-EPLAN services (Glossary mapping, Regex filter, Settings Encryption, API client serialization).
- Integration tests / Manual validation via EPLAN Debug mode targeting a test project.

**Target Platform**: Windows 10/11 running EPLAN Electric P8 2.7+ (specifically tested on EPLAN 2026.0.3).

**Project Type**: EPLAN API Add-in (.dll Class Library).

**Performance Goals**: Asynchronous processing with UI responsive rate of 60fps; Text extraction under 2 seconds for a 100-page project; UI highlighting under 500ms.

**Constraints**: Must execute in EPLAN's main thread for EPLAN API model manipulations (using EPLAN Transactions or Thread marshalling if called from async tasks).

---

## Constitution Check

*GATE: All principles are aligned.*
- **No forbidden libraries**: Standard System libraries and OpenXML/Newtonsoft.Json will be used.
- **Asynchronous Execution**: Asynchronous processing is enforced for all network calls to prevent EPLAN UI freezing.

---

## Project Structure

### Documentation (this feature)

```text
specs/001-eplan-translate/
├── spec.md              # Feature specification
├── plan.md              # This technical plan
└── checklists/
    └── requirements.md  # Quality checklist
```

### Source Code Structure

```text
TranslationEplAddIn.Tool/
├── Properties/
│   └── AssemblyInfo.cs
├── TranslationAddIn.cs          # Main entry implementing IEplAddIn, OnInitGui
├── EplanTextExtractor.cs        # Extracts and writes texts using EPLAN API
├── TranslationService.cs        # Communicates with Deepseek, DeepL, etc.
├── GlossaryManager.cs           # Manages local CSV/Excel/TMX dictionaries
├── BackupService.cs             # Wraps EPLAN's backup (.zw1) and restore APIs
├── SettingsManager.cs           # Configuration settings and API key encryption
├── Models/
│   ├── TranslationItem.cs       # Text model (Source, Target, Object ID, Type)
│   └── Configuration.cs         # App settings model
├── UI/
│   ├── MainWindow.xaml          # Main dockable sidebar panel
│   ├── MainWindow.xaml.cs
│   ├── SettingsWindow.xaml      # Popup for engine configurations, API keys, regex
│   ├── SettingsWindow.xaml.cs
│   ├── ProofreadingPanel.xaml   # DataGrid displaying translation diffs
│   ├── ProofreadingPanel.xaml.cs
│   └── DrawingLocator.cs        # EPLAN SelectionSet coordinate highlighter
└── Translate.cs                 # Action declarations for EPLAN
```

---

## Detailed Design & API Mapping

### 1. Main Entry & UI Integration (`TranslationAddIn.cs` & `Translate.cs`)
- Implement `Eplan.EplApi.ApplicationFramework.IEplAddIn`.
- In `OnInitGui()`, retrieve the `Eplan.EplApi.Gui.RibbonBar` (or fallback to legacy `Menu` if older EPLAN detected).
- Register a custom Tab `EPLAN 翻译助手` and Command Group `翻译管理`.
- Add buttons: `翻译主面板` (executes action `ShowTranslatePanel`), `一键回退` (executes action `RevertTranslation`), `设置中心` (executes action `ShowSettings`).
- Implement actions using `[DeclareAction("ShowTranslatePanel")]` etc.

### 2. Text Extraction & Writing (`EplanTextExtractor.cs`)
- To traverse texts, open the project using `Eplan.EplApi.DataModel.ProjectManager`.
- Iterate through Pages in `Project.Pages`.
- For each Page, iterate through `Page.AllObjects` to extract:
  - `Function` (device description, supplementary text).
  - `Terminal` and `Cable` descriptions.
  - `Page` attributes (Page Description, Page Name).
  - Graphic text boxes (`Eplan.EplApi.DataModel.Graphics.Text`).
- Build a list of `TranslationItem` carrying:
  - SourceText (string)
  - ObjectId (StorableObject ID)
  - PropId (Property ID)
  - ElementType (Enum)
- **Writing Back**:
  - Direct Modification: Open an EPLAN Transaction (`Eplan.EplApi.DataModel.Transaction`). Update properties.
  - native Dictionary Mode: Insert translated items into EPLAN project dictionary database `Eplan.EplApi.DataModel.ProjectDictionary` and call EPLAN translation system action `Translate`.

### 3. Asynchronous Translation Pipeline (`TranslationService.cs`)
- Implement an async pipeline using `System.Threading.Tasks`.
- Maintain a connection pool to LLM providers (using OpenAI-compatible endpoints) and traditional MT REST APIs.
- Construct specialized electrical prompt templates for LLMs:
  * "You are an expert electrical engineer. Translate the following EPLAN engineering texts from [Src] to [Dest]. Keep variables/placeholders unchanged."
- Implement rate limiting and batching (send up to 50 text snippets per request).

### 4. Text Protection & Regular Expressions (`FilterService.cs`)
- Prior to sending texts to translation, run regular expression checks configured by the user (e.g. `^PLC_.*`, `<.*?>`).
- Replace matched blocks with temporary token markers (e.g., `##TOKEN_0##`), translate the outer text, and restore the tokens in post-processing.
- Apply a page-level blacklist to skip extraction.

### 5. Backup & Revert (`BackupService.cs`)
- Use `Eplan.EplApi.HEServices.Backup` class to command a project backup before starting translation.
- Save the backup file `.zw1` under a temporary directory `C:\Users\62789\AppData\Local\EPLAN_Translate_Backup\[ProjectName]_[Timestamp].zw1`.
- Provide a `RestoreBackup` method that uses EPLAN API to reload the backup in case the user selects "Undo".

### 6. Interactive Drawing Locator (`DrawingLocator.cs`)
- Given a double-clicked `TranslationItem` containing a storable object ID.
- Retrieve the `StorableObject` using EPLAN `Project.GetObject(objectId)`.
- Use `Eplan.EplApi.Gui.SelectionSet` to highlight the object:
  ```csharp
  SelectionSet selection = new SelectionSet();
  selection.Select(storableObject);
  ```
- Trigger EPLAN's zoom action or navigate page context.

---

## Verification Plan

### Automated Tests
- Run `dotnet test` (configured in a separate test project or NUnit launcher) targeting:
  - `GlossaryManagerTests`: verifying TMX, CSV, Excel parsing and dictionary matching.
  - `SettingsTests`: verifying secure API key DPAPI encryption/decryption.
  - `FilterTests`: verifying regex token replacement and restoration.

### Manual Verification
- Compile the DLL and register the Add-in in EPLAN 2026.
- Run translation on a sample project with Chinese texts:
  - Check that the Ribbon tab `EPLAN 翻译助手` appears.
  - Click `翻译主面板`, open the sidebar, select target language "English", and click "Translate".
  - Verify that progress bar updates and does not freeze the UI.
  - Double-click a line in the proofreading grid, check if EPLAN centers on that drawing element.
  - Verify that Undo restores all original Chinese texts.
