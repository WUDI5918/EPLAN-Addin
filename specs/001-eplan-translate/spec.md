# Feature Specification: EPLAN Translation Add-in (EPLAN 翻译插件)

**Feature Branch**: `001-eplan-translate`

**Created**: 2026-06-18

**Status**: Draft

**Input**: User description: EPLAN 翻译插件产品需求文档 (PRD) V2.0

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Translate EPLAN Project Content (Priority: P1)

As an electrical designer, I want to automatically translate all text elements (such as device tags, function text, terminal descriptions, page/project attributes, etc.) in a selected scope (entire project or current page) into a target language, so that I don't have to manually search and translate them.

**Why this priority**: This is the core functionality of the translation add-in. Without it, the tool cannot provide any value.

**Independent Test**: Can be verified by running a translation on a sample EPLAN project with standard page texts and device descriptions, checking if they are converted to the target language and rendered correctly on the screen.

**Acceptance Scenarios**:

1. **Given** a loaded EPLAN project with multiple pages containing functional texts and device tags in Chinese, **When** I run the translation command with English as the target language, **Then** all translatable texts within the specified scope are translated into English.
2. **Given** a text element containing a protected pattern (e.g. a variable like `<.*?>` or a placeholder), **When** translation is executed with regex protection rules active, **Then** the protected part remains untranslated while other parts are translated.
3. **Given** a text element in Chinese and target language English, **When** "Append Mode" is selected with a newline separator, **Then** the final text displays both Chinese and English, separated by a newline.

---

### User Story 2 - Safe Rollback and Backup (Priority: P1)

As an electrical designer, I want to ensure that my EPLAN project data is safe and that I can easily undo any translation changes if the layout gets messed up or if the translation quality is unsatisfactory.

**Why this priority**: Automatic bulk modifications can easily break drawing layout or overwrite critical electrical data. A reliable recovery mechanism is critical for engineering safety.

**Independent Test**: Can be verified by running a translation, confirming that a backup was created, making a manual change, and triggering an undo to restore the project back to its pre-translation state.

**Acceptance Scenarios**:

1. **Given** an EPLAN project open, **When** I initiate the translation process, **Then** a backup or transaction restore point is automatically created before any modification is written.
2. **Given** a completed translation that has modified drawing texts, **When** I select the "Global Undo" option, **Then** all modified drawing elements are restored to their exact pre-translation states.

---

### User Story 3 - Glossary and Terminology Management (Priority: P2)

As a company standards manager, I want to configure a custom dictionary or terminology database (via Excel, CSV, TMX, or TBX formats) so that professional electrical terms are translated consistently according to corporate standards instead of using generic machine translations.

**Why this priority**: Generic translation engines lack the specific vocabulary of industrial electrical engineering (e.g. translating "端子" as "Terminal" instead of "Pin"). Corporate term libraries ensure engineering translation precision.

**Independent Test**: Can be verified by importing a custom CSV dictionary mapping specific terms, running the translator, and checking if the output matches the imported glossary rather than the default engine translation.

**Acceptance Scenarios**:

1. **Given** a custom glossary mapping "熔断器" to "Fuse (Standard)", **When** I run the translation on a diagram containing "熔断器", **Then** the term is translated to "Fuse (Standard)" instead of a generic engine translation.
2. **Given** a glossary in TMX or TBX format, **When** I import it via the glossary workbench, **Then** the terms are successfully loaded into the local add-in dictionary.

---

### User Story 4 - Drawing Co-location / Interactive Calibration (Priority: P2)

As an editor or reviewer, I want to double-click a translation log or verification record in the review dashboard and have EPLAN automatically navigate to and zoom/focus on the corresponding element in the schematic, so that I can inspect the layout changes and font alignments.

**Why this priority**: Translated texts often differ in length, causing visual overlapping. Reviewers need an interactive way to locate text overflows.

**Independent Test**: Can be verified by selecting a record in the translation log panel, double-clicking it, and verifying that the EPLAN active window changes the current page and highlights the text box.

**Acceptance Scenarios**:

1. **Given** a list of translated records displayed in the proofreading panel, **When** I double-click a record corresponding to a terminal strip description on Page 3, **Then** EPLAN navigates to Page 3, centers on the terminal strip component, and highlights it.

---

### User Story 5 - Batch Translation and Logs (Priority: P3)

As a project manager, I want to translate multiple EPLAN project files in batch mode and receive a detailed report summarizing the translation status, engine used, execution time, and any skipped elements.

**Why this priority**: Large projects consist of multiple sub-project files. Translating them one by one is inefficient.

**Independent Test**: Can be verified by supplying a list of EPLAN projects, triggering batch execution, and verifying the completion report is generated in the outputs folder.

**Acceptance Scenarios**:

1. **Given** three EPLAN project files selected in the batch manager, **When** I click translate, **Then** the tool processes them sequentially in the background without user intervention, and displays a summary of translated texts, skipped read-only files, and execution durations.

---

### Edge Cases

- **Locked / Read-Only Elements**: If a page is locked or signed out by another user, the add-in must skip the element, record a warning in the log, and proceed to the next item instead of crashing.
- **API Failure / Network Disconnect**: If the external translation API (e.g., Deepseek, DeepL) fails or the network is disconnected mid-way, the system must pause gracefully, display an error message, and preserve all already translated parts with the option to retry or rollback.
- **Extremely Long Text / Font Overflow**: If the translated text length exceeds the EPLAN box boundaries, the add-in should either downscale the font size or adapt the border, depending on user settings, to avoid text overlap.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001 (Extraction)**: The add-in MUST extract all relevant text elements from EPLAN drawings: Device Tags (DT), High-level/Location codes, functional text, terminal descriptions, cable descriptions, PLC I/O descriptions, reports, free/graphical texts, project/page attributes, and placeholder texts.
- **FR-002 (Protection)**: Users MUST be able to define regular expression rules and blacklists/whitelists to isolate specific terms (like code structures, price info, or variables) from being translated.
- **FR-003 (Translation Integration)**: The add-in MUST support both traditional translation engines (Google, DeepL, Microsoft, Baidu) and LLMs (Deepseek, Zhipu, Wenxin) with custom engineering Prompts and API keys.
- **FR-004 (Display Strategy)**: The add-in MUST support both "Override Mode" (overwrite source text) and "Append Mode" (append target text with a customizable separator like `\n` or `/`).
- **FR-005 (EPLAN Dictionary Integration)**: The add-in MUST support writing translated texts directly to the drawings OR writing them to the native EPLAN project dictionary for EPLAN's built-in translation module to display.
- **FR-006 (Safety)**: The add-in MUST automatically create a project backup (.zw1) or a transaction checkpoint before starting translation, and provide a "One-click Undo" button to revert all changes.
- **FR-007 (Interactive Review)**: The proofreading panel MUST allow double-clicking any translation log entry to auto-navigate and highlight the corresponding component on the EPLAN drawing canvas.
- **FR-008 (Layout Adaptation)**: The add-in MUST dynamically adjust text box boundaries or font scaling when text length increases to prevent overlapping.
- **FR-009 (Batch Processing)**: The add-in MUST support selecting multiple EPLAN projects and translating them in a queue, producing an execution report (.csv or .html).

### Non-Functional Requirements

- **NFR-001 (Asynchronous execution)**: The translation extraction and execution MUST run asynchronously, offering a progress bar with a cancel button to keep the EPLAN GUI responsive.
- **NFR-002 (Error Tolerance)**: The add-in MUST NOT crash when encountering read-only pages, locked projects, or corrupt objects. It must log the errors and skip those items.
- **NFR-003 (Compatibility)**: The add-in MUST support EPLAN Electric P8 v2.7 through v2024 (and EPLAN Platform 2026/2026.0.3 as referenced in the csproj).
- **NFR-004 (Security)**: User API keys and dictionary credentials stored locally MUST be encrypted using Windows Data Protection API (DPAPI) or equivalent secure storage.

### Key Entities *(include if feature involves data)*

- **EplanProject**: Represents the EPLAN project being translated. Contains pages, properties, and symbols.
- **TextElement**: A model representing a translation unit (source text, translated text, element type, page number, drawing coordinates, EPLAN object reference).
- **TranslationEngine**: Interface representing translation services (LLM, DeepL, etc.) with authentication and prompt customization settings.
- **GlossaryDatabase**: Represents custom dictionaries loaded by the user (supports CSV, Excel, TMX, TBX).
- **BackupPoint**: Represents the transaction checkpoint or file backup created before translation.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: EPLAN UI remains responsive during bulk translations, with the cancellation taking action within 1 second of user clicking the cancel button.
- **SC-002**: 100% of untranslatable variables matching the regex protection rules are kept in their original form.
- **SC-003**: 100% of projects translated can be restored to their original state using the Undo action.
- **SC-004**: Double-clicking a log entry navigates and highlights the target element in EPLAN in under 500 milliseconds.

## Assumptions

- EPLAN API assembly references are present on the developer machine and EPLAN is installed.
- Accessing external API services requires stable network connections, while offline LLM deployments require a reachable local server or local model endpoints.
- User has read/write permissions for the target EPLAN project files.
