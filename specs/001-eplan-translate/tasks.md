# Tasks: EPLAN Translation Add-in

**Input**: Design documents from `specs/001-eplan-translate/`

**Prerequisites**: plan.md (required), spec.md (required for user stories)

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [ ] T001 [P] Setup settings models `Models/Configuration.cs` and encrypted JSON manager `SettingsManager.cs`
- [ ] T002 Configure EPLAN Ribbon UI entry point `TranslationAddIn.cs` and basic actions in `Translate.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T003 Implement `TranslationService.cs` REST clients for Deepseek/OpenAI, DeepL, and Google Translate
- [ ] T004 Implement `EplanTextExtractor.cs` core traversing routine (Pages, Functions, DT, PLC descriptions) to collect texts

**Checkpoint**: Foundation ready - user story implementation can now begin

---

## Phase 3: User Story 1 - Translate EPLAN Project Content (Priority: P1) 🎯 MVP

**Goal**: Extract, regex-protect, translate, and write back EPLAN texts

**Independent Test**: Verify Chinese texts are successfully translated to English on a sample drawing.

### Implementation for User Story 1

- [ ] T005 [P] [US1] Create `Models/TranslationItem.cs` to hold text element properties (Source, Translated, ID, PropId, Type)
- [ ] T006 [US1] Implement regular expression tokenization and token restoration in `FilterService.cs`
- [ ] T007 [US1] Implement properties write-back under EPLAN `Transaction` block in `EplanTextExtractor.cs`
- [ ] T008 [US1] Integrate "Append Mode" vs "Override Mode" write-back with customizable separator
- [ ] T009 [P] [US1] Create WPF UI sidebar control `UI/MainWindow.xaml` and target language selection list
- [ ] T010 [US1] Integrate extraction, translation pipeline, and sidebar UI with async task progress and cancellation support

**Checkpoint**: User Story 1 (MVP) is fully functional and testable independently.

---

## Phase 4: User Story 2 - Safe Rollback and Backup (Priority: P1)

**Goal**: Project auto-backup (.zw1) and one-click restore

**Independent Test**: Perform translation, verify backup file creation, execute undo and confirm drawing text reverts.

### Implementation for User Story 2

- [ ] T011 [US2] Implement EPLAN `Backup` wrapper service in `BackupService.cs` (backup to `.zw1` under AppData)
- [ ] T012 [US2] Integrate automatic backup trigger in translation pipeline inside `UI/MainWindow.xaml.cs`
- [ ] T013 [US2] Implement EPLAN Revert/Undo action in `Translate.cs` linking to restore backup in `BackupService.cs`

**Checkpoint**: Backup and Revert functionalities are active.

---

## Phase 5: User Story 3 - Glossary and Terminology Management (Priority: P2)

**Goal**: Load CSV/Excel/TMX/TBX glossary files and match terms before API calls

**Independent Test**: Import term CSV mapping and translate, confirming matched terms use dictionary mapping.

### Implementation for User Story 3

- [ ] T014 [US3] Implement CSV, Excel (xlsx), TMX, and TBX readers in `GlossaryManager.cs`
- [ ] T015 [US3] Create `UI/SettingsWindow.xaml` popup modal for API Keys, Glossary files, Regex rules, and Modes configuration
- [ ] T016 [US3] Integrate glossary lookup phase as the first lookup in the translation service pipeline

**Checkpoint**: Glossary database integration is functional.

---

## Phase 6: User Story 4 - Drawing Co-location / Interactive Calibration (Priority: P2)

**Goal**: Double-click log entry to zoom and focus corresponding EPLAN schematic element

**Independent Test**: Double-click record in the proofreading grid, check if EPLAN active sheet shifts and centers the element.

### Implementation for User Story 4

- [ ] T017 [US4] Create proofreading data grid in `UI/ProofreadingPanel.xaml` showing original text, translation, status, and engine
- [ ] T018 [US4] Implement `UI/DrawingLocator.cs` using EPLAN SelectionSet API to select and center drawing objects
- [ ] T019 [US4] Bind DataGrid row double-click event in `UI/ProofreadingPanel.xaml.cs` to invoke `DrawingLocator` navigation

**Checkpoint**: Interactive review panel and drawing linkage is active.

---

## Phase 7: User Story 5 - Batch Translation and Logs (Priority: P3)

**Goal**: Queue multiple projects for translation and export report logs

**Independent Test**: Translate three EPLAN projects in batch, check generated CSV/HTML report.

### Implementation for User Story 5

- [ ] T020 [US5] Implement multi-project translation queue runner
- [ ] T021 [US5] Implement HTML and CSV translation log report generator

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Security, error handling, performance improvements

- [ ] T022 [P] Secure API keys locally with DPAPI encryption in `SettingsManager.cs`
- [ ] T023 Tolerant error management (skip locks, check write permissions, log exceptions)
- [ ] T024 Perform final compilation and validation of the assemblies
