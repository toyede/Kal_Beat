using System;
using System.Collections.Generic;
using KalBeat.Combat;
using UnityEditor;
using UnityEngine;
using KalBeat.Charting;

namespace KalBeat.Charting.Editor
{
    public sealed class ChartEditorWindow : EditorWindow
    {
        private const float LeftPanelWidth = 240f;
        private const float RightPanelWidth = 300f;
        private const float MetaPanelSpacing = 6f;
        private const float TimelineHeaderHeight = 24f;
        private const float LaneHeight = 54f;
        private const float DefaultPixelsPerBeat = 72f;
        private const float MinPixelsPerBeat = 24f;
        private const float MaxPixelsPerBeat = DefaultPixelsPerBeat * 6f;
        private const float MinTimelineWidth = 900f;
        private const float MinTimelineHeight = 320f;
        private const float EventBoxWidth = 18f;
        private const string MetronomeEventSearchName = "metronome";
        private const string EventPreviewEventSearchName = "finger_snap";
        private const float PreviewTriggerEpsilon = 0.0001f;
        private const int DefaultRangedPhraseCueCount = 8;
        private const int DefaultRangedAnchorCount = 8;
        private const float DefaultRangedPhraseResponseOffsetBeats = 4f;
        private const float DefaultRangedPhraseCueIntervalBeats = 0.5f;

        private static readonly Color BackgroundColor = new Color(0.13f, 0.13f, 0.14f, 1f);
        private static readonly Color HeaderColor = new Color(0.17f, 0.17f, 0.18f, 1f);
        private static readonly Color LaneEvenColor = new Color(0.19f, 0.19f, 0.2f, 1f);
        private static readonly Color LaneOddColor = new Color(0.16f, 0.16f, 0.17f, 1f);
        private static readonly Color GridMajorColor = new Color(0.45f, 0.45f, 0.48f, 0.7f);
        private static readonly Color GridMinorColor = new Color(0.3f, 0.3f, 0.33f, 0.35f);
        private static readonly Color CursorColor = new Color(0.95f, 0.7f, 0.15f, 1f);
        private static readonly Color SelectedOutlineColor = new Color(1f, 1f, 1f, 0.9f);

        private ChartData currentChart;
        private ChartEventData selectedEvent;
        private readonly FmodEditorPreviewBackend fmodPreviewBackend = new FmodEditorPreviewBackend();

        private Vector2 leftPanelScroll;
        private Vector2 timelineScroll;
        private Vector2 rightPanelScroll;

        private float cursorBeat;
        private float pixelsPerBeat = DefaultPixelsPerBeat;
        private float lastPreviewTickTimeSec;
        private float previewTimeSec;
        private float previewSpeed = 1f;
        private float previewMusicVolume = 1f;
        private float previewCueVolume = 1f;
        private float timelineViewportWidth;
        private float draggedBeat;
        private float dragBeatPointerOffset;
        private int selectedLane;
        private int draggedLane;
        private string metronomePreviewEventPath = string.Empty;
        private string eventPreviewEventPath = string.Empty;
        private TextAsset jsonImportAsset;
        private int selectedPatternPopupIndex;
        private int selectedRangedPatternPopupIndex;
        private int selectedRangedCounterPatternPopupIndex;
        private bool metronomeEnabled = true;
        private bool eventPreviewEnabled = true;
        private bool metaPanelExpanded = true;
        private bool snapEnabled = true;
        private bool isDraggingEvent;
        private bool previewPaused;
        private bool previewWasPlaying;
        private ChartEventData draggedEvent;

        private ChartValidationResult lastValidation;

        private sealed class AppliedPatternSyncGroup
        {
            public AppliedPatternSyncGroup(string instanceId)
            {
                InstanceId = instanceId;
                Events = new List<ChartEventData>();
            }

            public string InstanceId { get; }
            public List<ChartEventData> Events { get; }
        }

        private readonly struct AppliedPatternMetadata
        {
            public AppliedPatternMetadata(
                string instanceId,
                string assetGuid,
                string patternId,
                string signature,
                float timingBeat,
                int timingStepIndex,
                int stepCount)
            {
                InstanceId = instanceId;
                AssetGuid = assetGuid;
                PatternId = patternId;
                Signature = signature;
                TimingBeat = timingBeat;
                TimingStepIndex = timingStepIndex;
                StepCount = stepCount;
            }

            public string InstanceId { get; }
            public string AssetGuid { get; }
            public string PatternId { get; }
            public string Signature { get; }
            public float TimingBeat { get; }
            public int TimingStepIndex { get; }
            public int StepCount { get; }
        }

        [MenuItem("Tools/KalBeat/Chart Editor")]
        public static void OpenWindow()
        {
            var window = GetWindow<ChartEditorWindow>("KalBeat Chart");
            window.minSize = new Vector2(1240f, 720f);
            window.Show();
        }

        private void OnEnable()
        {
            minSize = new Vector2(1240f, 720f);
            wantsMouseMove = true;
            EditorApplication.update += OnEditorUpdate;
            TryAssignFromSelection();
            TryAutoAssignPreviewEvents();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            fmodPreviewBackend.Dispose();
        }

        private void OnSelectionChange()
        {
            TryAssignFromSelection();
            Repaint();
        }

        private void OnEditorUpdate()
        {
            if (currentChart == null)
            {
                return;
            }

            fmodPreviewBackend.SyncMainPlaybackState();
            bool isPlaying = IsPreviewPlaying();

            if (isPlaying)
            {
                float previousTimeSec = previewWasPlaying ? lastPreviewTickTimeSec : previewTimeSec;
                previewTimeSec = GetResolvedPreviewTime();
                TriggerPreviewAudioBetween(previousTimeSec, previewTimeSec);
                lastPreviewTickTimeSec = previewTimeSec;
                SyncCursorFromPreviewTime(previewTimeSec);
                EnsureCursorVisible();

                Repaint();
            }
            else if (previewWasPlaying)
            {
                previewTimeSec = GetResolvedPreviewTime();
                lastPreviewTickTimeSec = previewTimeSec;
                Repaint();
            }

            previewWasPlaying = isPlaying;
        }

        private void OnGUI()
        {
            if (currentChart == null)
            {
                TryAssignFromSelection();
            }

            lastValidation = currentChart != null ? ChartValidator.Validate(currentChart) : null;
            ClampEditorState();
            HandleKeyboardShortcuts();

            DrawToolbar();

            if (currentChart == null)
            {
                DrawEmptyState();
                return;
            }

            DrawMetaPanel();
            EditorGUILayout.Space(10f);

            EditorGUILayout.BeginHorizontal();
            DrawLeftPanel();
            DrawTimelinePanel();
            DrawRightPanel();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("New Chart", EditorStyles.toolbarButton, GUILayout.Width(84f)))
            {
                CreateNewChartAsset();
            }

            EditorGUI.BeginChangeCheck();
            var nextChart = (ChartData)EditorGUILayout.ObjectField(currentChart, typeof(ChartData), false, GUILayout.Width(300f));
            if (EditorGUI.EndChangeCheck())
            {
                AssignChart(nextChart);
            }

            if (GUILayout.Button("Use Selection", EditorStyles.toolbarButton, GUILayout.Width(96f)))
            {
                TryAssignFromSelection();
            }

            GUILayout.Space(12f);

            bool nextSnap = GUILayout.Toggle(snapEnabled, "Snap", EditorStyles.toolbarButton, GUILayout.Width(48f));
            if (nextSnap != snapEnabled)
            {
                snapEnabled = nextSnap;
                ClampEditorState();
            }

            using (new EditorGUI.DisabledScope(currentChart == null))
            {
                DrawToolbarGridButton(4, "Grid 4");
                DrawToolbarGridButton(8, "Grid 8");
            }

            GUILayout.Space(8f);

            using (new EditorGUI.DisabledScope(!CanPreviewAudio()))
            {
                if (GUILayout.Button(IsPreviewPlaying() ? "Pause" : "Play", EditorStyles.toolbarButton, GUILayout.Width(54f)))
                {
                    TogglePreviewPlayPause();
                }

                if (GUILayout.Button("Stop", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                {
                    StopPreviewAudio(true);
                }
            }

            GUILayout.FlexibleSpace();

            if (currentChart != null && lastValidation != null)
            {
                GUILayout.Label($"Cursor {cursorBeat:0.###} / Lane {selectedLane + 1}", EditorStyles.miniLabel, GUILayout.Width(160f));

                if (CanPreviewAudio())
                {
                    GUILayout.Label($"{previewTimeSec:0.00}s / {GetPreviewDurationSec():0.00}s", EditorStyles.miniLabel, GUILayout.Width(130f));
                }

                GUILayout.Label($"Zoom {(pixelsPerBeat / DefaultPixelsPerBeat) * 100f:0}%", EditorStyles.miniLabel, GUILayout.Width(80f));

                string validationText = lastValidation.HasError
                    ? $"{lastValidation.ErrorCount} Error / {lastValidation.WarningCount} Warning"
                    : $"{lastValidation.WarningCount} Warning";
                GUILayout.Label(validationText, EditorStyles.miniLabel, GUILayout.Width(140f));
            }

            using (new EditorGUI.DisabledScope(currentChart == null || (lastValidation != null && lastValidation.HasError)))
            {
                if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                {
                    SaveCurrentChart();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbarGridButton(int gridDivision, string label)
        {
            bool isActive = currentChart != null && currentChart.Meta.GridDivision == gridDivision;
            bool pressed = GUILayout.Toggle(isActive, label, EditorStyles.toolbarButton, GUILayout.Width(64f));
            if (pressed && !isActive)
            {
                RecordChartChange("Change Grid Division");
                currentChart.Meta.GridDivision = gridDivision;
                ClampEditorState();
                MarkChartDirty();
            }
        }

        private void DrawEmptyState()
        {
            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Chart asset is not selected.", EditorStyles.boldLabel);
            GUILayout.Label("Create a new chart or select an existing ChartData asset.", EditorStyles.wordWrappedLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Create Chart", GUILayout.Height(28f)))
            {
                CreateNewChartAsset();
            }

            if (GUILayout.Button("Use Selected Asset", GUILayout.Height(28f)))
            {
                TryAssignFromSelection();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();
        }

        private void DrawMetaPanel()
        {
            var meta = currentChart.Meta;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            metaPanelExpanded = EditorGUILayout.Foldout(metaPanelExpanded, "Chart Meta", true, EditorStyles.foldoutHeader);

            if (!metaPanelExpanded)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUI.BeginChangeCheck();

            string nextSongId = EditorGUILayout.TextField("Song Id", meta.SongId);
            string nextSongName = EditorGUILayout.TextField("Song Name", meta.SongName);
            string nextFmodEventPath = EditorGUILayout.TextField("FMOD Event Path", meta.FmodEventPath);

            GUILayout.Space(MetaPanelSpacing);

            EditorGUILayout.BeginHorizontal();
            float nextBpm = EditorGUILayout.FloatField("BPM", meta.Bpm);
            float nextOffsetSec = EditorGUILayout.FloatField("Offset Sec", meta.OffsetSec);
            float nextSongLengthSec = EditorGUILayout.FloatField("Song Length", meta.SongLengthSec);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            int nextGridDivision = EditorGUILayout.IntPopup("Grid", meta.GridDivision, new[] { "4", "8" }, new[] { 4, 8 });
            int nextLaneCount = EditorGUILayout.IntField("Lane Count", meta.LaneCount);
            int nextMaxEventsPerSlot = EditorGUILayout.IntField("Max Events", meta.MaxEventsPerSlot);
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                RecordChartChange("Edit Chart Meta");
                bool fmodEventPathChanged = !string.Equals(meta.FmodEventPath, nextFmodEventPath, StringComparison.Ordinal);
                meta.SongId = nextSongId;
                meta.SongName = nextSongName;
                meta.FmodEventPath = nextFmodEventPath;
                meta.Bpm = nextBpm;
                meta.OffsetSec = nextOffsetSec;
                meta.SongLengthSec = nextSongLengthSec;
                meta.GridDivision = nextGridDivision;
                meta.LaneCount = nextLaneCount;
                meta.MaxEventsPerSlot = nextMaxEventsPerSlot;
                ClampEditorState();
                MarkChartDirty();

                if (fmodEventPathChanged)
                {
                    StopPreviewAudio(true);
                }
            }

            GUILayout.Label($"Timeline Beats: {GetTotalBeats():0.##}", EditorStyles.miniLabel);

            DrawPreviewControls();
            EditorGUILayout.EndVertical();
        }

        private void DrawLaneBindings(ChartMeta meta)
        {
            if (meta == null)
            {
                return;
            }

            meta.SyncLaneBindingsWithLaneCount();

            EditorGUILayout.HelpBox("Lane binding is fixed for the whole chart. The current editor supports None and Monster Definition bindings.", MessageType.Info);

            MonsterDefinition[] monsterDefinitions = LoadMonsterDefinitions();
            string[] popupOptions = BuildLaneBindingPopupOptions(monsterDefinitions);
            int[] nextSelections = new int[meta.LaneBindings.Count];

            EditorGUI.BeginChangeCheck();

            for (int i = 0; i < meta.LaneBindings.Count; i++)
            {
                LaneBindingData binding = meta.LaneBindings[i] ?? new LaneBindingData();
                meta.LaneBindings[i] = binding;

                int currentSelection = GetLaneBindingSelectionIndex(binding, monsterDefinitions);
                nextSelections[i] = EditorGUILayout.Popup($"Lane {i + 1}", currentSelection, popupOptions);
            }

            if (EditorGUI.EndChangeCheck())
            {
                RecordChartChange("Edit Lane Bindings");

                for (int i = 0; i < meta.LaneBindings.Count; i++)
                {
                    ApplyLaneBindingSelection(meta.LaneBindings[i], nextSelections[i], monsterDefinitions);
                }

                MarkChartDirty();
            }

            if (monsterDefinitions.Length == 0)
            {
                EditorGUILayout.HelpBox("No Monster Definition assets were found. Create one first to assign a monster to a lane.", MessageType.Warning);
            }
        }

        private void DrawPreviewControls()
        {
            EditorGUILayout.Space(4f);
            GUILayout.Label("Audio Preview", EditorStyles.boldLabel);

            if (string.IsNullOrWhiteSpace(currentChart.Meta.FmodEventPath))
            {
                EditorGUILayout.HelpBox("Assign FMOD Event Path to enable preview playback.", MessageType.Info);
                return;
            }

            if (!CanPreviewAudio())
            {
                EditorGUILayout.HelpBox("FMOD preview event could not be resolved. Save and build banks in FMOD Studio, then refresh in Unity.", MessageType.Warning);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(IsPreviewPlaying() ? "Pause" : "Play", GUILayout.Width(68f)))
            {
                TogglePreviewPlayPause();
            }

            if (GUILayout.Button("Stop", GUILayout.Width(60f)))
            {
                StopPreviewAudio(true);
            }

            if (GUILayout.Button("Jump To Cursor", GUILayout.Width(110f)))
            {
                SeekPreviewToCursor(IsPreviewPlaying());
            }

            GUILayout.Label($"{previewTimeSec:0.00}s / {GetPreviewDurationSec():0.00}s", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            float nextPreviewSpeed = EditorGUILayout.FloatField("Speed", previewSpeed);
            if (EditorGUI.EndChangeCheck())
            {
                previewSpeed = Mathf.Clamp(nextPreviewSpeed, 0.01f, 4f);
                fmodPreviewBackend.SetMainSpeed(previewSpeed);
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            float nextPreviewMusicVolume = EditorGUILayout.FloatField("Music Volume", previewMusicVolume);
            float nextPreviewCueVolume = EditorGUILayout.FloatField("Cue Volume", previewCueVolume);
            if (EditorGUI.EndChangeCheck())
            {
                previewMusicVolume = Mathf.Clamp(nextPreviewMusicVolume, 0f, 4f);
                previewCueVolume = Mathf.Clamp(nextPreviewCueVolume, 0f, 4f);
                fmodPreviewBackend.SetMainVolume(previewMusicVolume);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            float nextPreviewTime = GUILayout.HorizontalSlider(previewTimeSec, 0f, GetPreviewDurationSec());
            if (EditorGUI.EndChangeCheck())
            {
                SetPreviewTime(nextPreviewTime, IsPreviewPlaying());
            }

            EditorGUILayout.Space(4f);
            GUILayout.Label("Preview Cues", EditorStyles.boldLabel);

            metronomeEnabled = EditorGUILayout.ToggleLeft("Enable Metronome", metronomeEnabled);
            metronomePreviewEventPath = EditorGUILayout.TextField("Metronome Event", metronomePreviewEventPath);

            eventPreviewEnabled = EditorGUILayout.ToggleLeft("Enable Event Preview SFX", eventPreviewEnabled);
            eventPreviewEventPath = EditorGUILayout.TextField("Event SFX Event", eventPreviewEventPath);
        }

        private void DrawLeftPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(LeftPanelWidth), GUILayout.ExpandHeight(true));
            leftPanelScroll = EditorGUILayout.BeginScrollView(leftPanelScroll, GUILayout.ExpandHeight(true));

            GUILayout.Label("Lane Bindings", EditorStyles.boldLabel);
            DrawLaneBindings(currentChart.Meta);
            GUILayout.Space(12f);

            GUILayout.Label("Event Palette", EditorStyles.boldLabel);
            GUILayout.Label("Space adds a None marker at the current cursor.", EditorStyles.wordWrappedLabel);
            GUILayout.Space(4f);

            MonsterDefinition selectedLaneMonsterDefinition = GetMonsterDefinitionForLane(selectedLane);
            CombatEventKind[] allowedPaletteKinds = GetAllowedCombatEventKinds(selectedLaneMonsterDefinition);

            DrawAddButton("Add None Marker", CombatEventKind.None, allowedPaletteKinds);
            DrawAddButton("Add Melee Counterable", CombatEventKind.MeleeThreatCounterable, allowedPaletteKinds);
            DrawAddButton("Add Melee Dodge", CombatEventKind.MeleeThreatDodgeOnly, allowedPaletteKinds);
            DrawAddButton("Add Player Dash", CombatEventKind.PlayerDashAttack, allowedPaletteKinds);
            DrawAddButton("Add Player Ranged", CombatEventKind.PlayerRangedAttack, allowedPaletteKinds);
            DrawAddButton("Add Ranged Counterable", CombatEventKind.RangedThreatCounterable, allowedPaletteKinds);
            DrawAddButton("Add Ranged Dodge", CombatEventKind.RangedThreatDodgeOnly, allowedPaletteKinds);

            GUILayout.Space(8f);
            GUILayout.Label("Ranged Phrase", EditorStyles.boldLabel);
            PatternDefinition[] rangedPatternDefinitions = GetSupportedPatterns(
                selectedLaneMonsterDefinition,
                CombatEventKind.RangedThreatDodgeOnly);
            if (rangedPatternDefinitions.Length > 0)
            {
                selectedRangedPatternPopupIndex = Mathf.Clamp(
                    selectedRangedPatternPopupIndex,
                    0,
                    rangedPatternDefinitions.Length - 1);
                selectedRangedPatternPopupIndex = EditorGUILayout.Popup(
                    "Ranged Pattern",
                    selectedRangedPatternPopupIndex,
                    BuildPatternPopupOptions(rangedPatternDefinitions));
                PatternDefinition selectedRangedPatternDefinition = rangedPatternDefinitions[selectedRangedPatternPopupIndex];
                EditorGUILayout.LabelField(
                    "Rule",
                    $"Move {selectedRangedPatternDefinition.RangedDodgeMoveUnits:0.###}u / Band {selectedRangedPatternDefinition.RangedResponseBandUnits:0.###}u");
            }
            else
            {
                EditorGUILayout.HelpBox("No ranged dodge pattern is linked to this lane monster.", MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(!ContainsCombatEventKind(allowedPaletteKinds, CombatEventKind.RangedThreatDodgeOnly) ||
                                               rangedPatternDefinitions.Length == 0))
            {
                if (GUILayout.Button("Generate 1-Bar Dodge Call", GUILayout.Height(26f)))
                {
                    GenerateRangedDodgePhraseAtCursor(rangedPatternDefinitions[selectedRangedPatternPopupIndex]);
                }
            }

            GUILayout.Space(8f);
            GUILayout.Label("Ranged Counter Phrase", EditorStyles.boldLabel);
            PatternDefinition[] rangedCounterPatternDefinitions = GetSupportedPatterns(
                selectedLaneMonsterDefinition,
                CombatEventKind.RangedThreatCounterable);
            if (rangedCounterPatternDefinitions.Length > 0)
            {
                selectedRangedCounterPatternPopupIndex = Mathf.Clamp(
                    selectedRangedCounterPatternPopupIndex,
                    0,
                    rangedCounterPatternDefinitions.Length - 1);
                selectedRangedCounterPatternPopupIndex = EditorGUILayout.Popup(
                    "Counter Pattern",
                    selectedRangedCounterPatternPopupIndex,
                    BuildPatternPopupOptions(rangedCounterPatternDefinitions));
                PatternDefinition selectedCounterPatternDefinition = rangedCounterPatternDefinitions[selectedRangedCounterPatternPopupIndex];
                EditorGUILayout.LabelField(
                    "Projectile",
                    $"{selectedCounterPatternDefinition.ProjectileTravelUnits:0.###}u / {selectedCounterPatternDefinition.ProjectileTravelDurationSec:0.###}s");
            }
            else
            {
                EditorGUILayout.HelpBox("No ranged counter pattern is linked to this lane monster.", MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(!ContainsCombatEventKind(allowedPaletteKinds, CombatEventKind.RangedThreatCounterable) ||
                                               rangedCounterPatternDefinitions.Length == 0))
            {
                if (GUILayout.Button("Generate 1-Bar Counter Call", GUILayout.Height(26f)))
                {
                    GenerateRangedCounterPhraseAtCursor(rangedCounterPatternDefinitions[selectedRangedCounterPatternPopupIndex]);
                }
            }

            GUILayout.Space(8f);

            using (new EditorGUI.DisabledScope(selectedEvent == null))
            {
                if (GUILayout.Button("Delete Selected", GUILayout.Height(24f)))
                {
                    DeleteSelectedEvent();
                }
            }

            using (new EditorGUI.DisabledScope(currentChart == null || currentChart.Events == null || currentChart.Events.Count == 0))
            {
                if (GUILayout.Button("Clear All Events", GUILayout.Height(24f)))
                {
                    ClearAllEvents();
                }
            }

            GUILayout.Space(12f);
            GUILayout.Label("JSON Import", EditorStyles.boldLabel);
            jsonImportAsset = (TextAsset)EditorGUILayout.ObjectField("JSON Chart", jsonImportAsset, typeof(TextAsset), false);
            EditorGUILayout.HelpBox("Import overwrites current events and updates BPM, Offset Sec, Song Length and Lane Count from JSON. Song Id, Song Name, FMOD Event Path and Grid stay unchanged.", MessageType.Info);

            using (new EditorGUI.DisabledScope(currentChart == null || jsonImportAsset == null))
            {
                if (GUILayout.Button("Import Into Current Chart", GUILayout.Height(24f)))
                {
                    ImportJsonIntoCurrentChart();
                }
            }

            GUILayout.Space(12f);
            GUILayout.Label("Validation", EditorStyles.boldLabel);

            if (lastValidation == null || lastValidation.Issues.Count == 0)
            {
                EditorGUILayout.HelpBox("No validation issues.", MessageType.Info);
            }
            else
            {
                for (int i = 0; i < lastValidation.Issues.Count; i++)
                {
                    ChartValidationIssue issue = lastValidation.Issues[i];
                    MessageType messageType = issue.Severity == ChartValidationSeverity.Error ? MessageType.Error : MessageType.Warning;
                    string label = issue.EventIndex >= 0
                        ? $"[{issue.Code}] Event {issue.EventIndex}: {issue.Message}"
                        : $"[{issue.Code}] {issue.Message}";
                    EditorGUILayout.HelpBox(label, messageType);
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawAddButton(string label, CombatEventKind eventKind, CombatEventKind[] allowedEventKinds)
        {
            if (!ContainsCombatEventKind(allowedEventKinds, eventKind))
            {
                return;
            }

            if (GUILayout.Button(label, GUILayout.Height(26f)))
            {
                AddOrUpdateEventAtCursor(eventKind);
            }
        }

        private void DrawTimelinePanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            Rect viewportRect = GUILayoutUtility.GetRect(64f, 100000f, MinTimelineHeight, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUI.Box(viewportRect, GUIContent.none, EditorStyles.helpBox);

            Rect innerRect = new Rect(viewportRect.x + 1f, viewportRect.y + 1f, viewportRect.width - 2f, viewportRect.height - 2f);
            timelineViewportWidth = innerRect.width;

            float totalBeats = GetTotalBeats();
            int laneCount = Mathf.Max(1, currentChart.Meta.LaneCount);
            float contentWidth = Mathf.Max(MinTimelineWidth, totalBeats * pixelsPerBeat + 80f);
            float contentHeight = TimelineHeaderHeight + laneCount * LaneHeight;
            Rect contentRect = new Rect(0f, 0f, contentWidth, contentHeight);

            timelineScroll = GUI.BeginScrollView(innerRect, timelineScroll, contentRect);
            DrawTimelineContent(contentRect, laneCount, totalBeats);
            GUI.EndScrollView();

            HandleTimelineInput(innerRect);
            EditorGUILayout.EndVertical();
        }

        private void DrawTimelineContent(Rect contentRect, int laneCount, float totalBeats)
        {
            EditorGUI.DrawRect(contentRect, BackgroundColor);
            EditorGUI.DrawRect(new Rect(0f, 0f, contentRect.width, TimelineHeaderHeight), HeaderColor);

            for (int lane = 0; lane < laneCount; lane++)
            {
                float laneY = TimelineHeaderHeight + lane * LaneHeight;
                Color laneColor = lane % 2 == 0 ? LaneEvenColor : LaneOddColor;
                EditorGUI.DrawRect(new Rect(0f, laneY, contentRect.width, LaneHeight), laneColor);
                EditorGUI.DrawRect(new Rect(0f, laneY, contentRect.width, 1f), GridMajorColor);
                GUI.Label(new Rect(8f, laneY + 4f, 60f, 18f), $"Lane {lane + 1}", EditorStyles.miniBoldLabel);
            }

            DrawTimelineGrid(totalBeats, laneCount);
            DrawCursorLine(laneCount);
            DrawEvents();
        }

        private void DrawTimelineGrid(float totalBeats, int laneCount)
        {
            int gridDivision = Mathf.Max(1, currentChart.Meta.GridDivision);
            int totalDivisions = Mathf.CeilToInt(totalBeats * gridDivision);
            float gridHeight = TimelineHeaderHeight + laneCount * LaneHeight;

            for (int i = 0; i <= totalDivisions; i++)
            {
                bool isMajor = i % gridDivision == 0;
                float x = i * (pixelsPerBeat / gridDivision);
                EditorGUI.DrawRect(new Rect(x, 0f, isMajor ? 2f : 1f, gridHeight), isMajor ? GridMajorColor : GridMinorColor);

                if (isMajor)
                {
                    int beatIndex = i / gridDivision;
                    GUI.Label(new Rect(x + 4f, 2f, 44f, 18f), beatIndex.ToString(), EditorStyles.miniLabel);
                }
            }
        }

        private void DrawCursorLine(int laneCount)
        {
            float x = cursorBeat * pixelsPerBeat;
            float height = TimelineHeaderHeight + laneCount * LaneHeight;
            EditorGUI.DrawRect(new Rect(x, 0f, 2f, height), CursorColor);
        }

        private void DrawEvents()
        {
            if (currentChart == null || currentChart.Events == null)
            {
                return;
            }

            for (int i = 0; i < currentChart.Events.Count; i++)
            {
                ChartEventData eventData = currentChart.Events[i];
                if (eventData == null)
                {
                    continue;
                }

                Rect eventRect = GetEventRect(GetDisplayedBeat(eventData), GetDisplayedLane(eventData));
                EditorGUI.DrawRect(eventRect, GetEventColor(eventData));

                if (ReferenceEquals(selectedEvent, eventData))
                {
                    DrawOutline(eventRect, SelectedOutlineColor, 2f);
                }

                GUI.Label(eventRect, GetEventShortLabel(eventData), EditorStyles.whiteMiniLabel);
            }
        }

        private void DrawRightPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(RightPanelWidth), GUILayout.ExpandHeight(true));
            rightPanelScroll = EditorGUILayout.BeginScrollView(rightPanelScroll, GUILayout.ExpandHeight(true));

            GUILayout.Label("Properties", EditorStyles.boldLabel);

            if (selectedEvent == null || currentChart == null || !currentChart.Events.Contains(selectedEvent))
            {
                EditorGUILayout.HelpBox("Select an event to edit its properties.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
                return;
            }

            int laneCount = Mathf.Max(1, currentChart.Meta.LaneCount);

            EditorGUI.BeginChangeCheck();

            float nextBeat = EditorGUILayout.FloatField("Beat", selectedEvent.Beat);
            int nextLane = EditorGUILayout.IntSlider("Lane", selectedEvent.Lane, 0, laneCount - 1);
            MonsterDefinition nextLaneMonsterDefinition = GetMonsterDefinitionForLane(nextLane);
            CombatEventKind[] allowedEventKinds = GetAllowedCombatEventKinds(nextLaneMonsterDefinition);
            string[] allowedEventKindLabels = BuildCombatEventKindLabels(allowedEventKinds);
            int selectedKindIndex = FindCombatEventKindIndex(allowedEventKinds, selectedEvent.Kind);
            selectedKindIndex = EditorGUILayout.Popup("Kind", selectedKindIndex, allowedEventKindLabels);
            CombatEventKind nextKind = allowedEventKinds[selectedKindIndex];
            CombatEventPhase nextPhase = (CombatEventPhase)EditorGUILayout.EnumPopup("Phase", selectedEvent.Phase);
            int nextStepIndex = EditorGUILayout.IntField("Step Index", selectedEvent.StepIndex);
            ReadyCueType nextReadyCueType = selectedEvent.ReadyCueType;
            MonsterDefinition laneMonsterDefinition = nextLaneMonsterDefinition;
            if (nextPhase == CombatEventPhase.Ready)
            {
                if (nextKind == CombatEventKind.PlayerDashAttack)
                {
                    EditorGUILayout.LabelField("Ready Cue", "Mark");
                    nextReadyCueType = ReadyCueType.None;
                }
                else if (IsRangedPhraseKind(nextKind))
                {
                    EditorGUILayout.LabelField(
                        "Ready Cue",
                        GetRangedReadyCueLabel(nextKind));
                    nextReadyCueType = ReadyCueType.None;
                }
                else
                {
                    ReadyCueType[] readyCueOptions = GetAllowedReadyCueTypes(laneMonsterDefinition);
                    string[] readyCueLabels = BuildReadyCueLabels(readyCueOptions);
                    int readyCueIndex = FindReadyCueIndex(readyCueOptions, selectedEvent.ReadyCueType);
                    readyCueIndex = EditorGUILayout.Popup("Ready Cue", readyCueIndex, readyCueLabels);
                    nextReadyCueType = readyCueOptions[readyCueIndex];
                }
            }

            bool shouldShowDirectionHint = ShouldShowDirectionHint(nextKind, nextPhase, nextReadyCueType);
            PlayerRelativeDirection nextDirectionHint = shouldShowDirectionHint
                ? selectedEvent.DirectionHint
                : PlayerRelativeDirection.None;
            if (shouldShowDirectionHint)
            {
                nextDirectionHint = (PlayerRelativeDirection)EditorGUILayout.EnumPopup("Direction (Player View)", selectedEvent.DirectionHint);
                EditorGUILayout.HelpBox("Left/Right is always interpreted from the player viewpoint.", MessageType.Info);
            }
            float nextNudgeMs = EditorGUILayout.FloatField("Nudge (ms)", selectedEvent.NudgeMs);
            string nextRangedPhraseId = selectedEvent.RangedPhraseId;
            int nextRangedCueIndex = selectedEvent.RangedCueIndex;
            int nextRangedAnchorIndex = selectedEvent.RangedAnchorIndex;

            if (IsRangedPhraseKind(nextKind))
            {
                GUILayout.Space(6f);
                GUILayout.Label("Ranged Call/Response", EditorStyles.boldLabel);
                nextRangedPhraseId = EditorGUILayout.TextField("Phrase Id", selectedEvent.RangedPhraseId);
                nextRangedCueIndex = EditorGUILayout.IntField("Cue Index", selectedEvent.RangedCueIndex);
                nextRangedAnchorIndex = EditorGUILayout.IntField("Anchor Index", selectedEvent.RangedAnchorIndex);
                EditorGUILayout.HelpBox("Ready and Timing events are paired by Phrase Id + Cue Index. Anchor Index selects the monster ranged anchor.", MessageType.Info);
            }

            if (EditorGUI.EndChangeCheck())
            {
                bool isRangedPhraseEdit = IsRangedPhraseKind(selectedEvent.Kind) &&
                                          selectedEvent.Kind == nextKind;
                bool shouldClearPatternLink = isRangedPhraseEdit
                    ? nextLane != selectedEvent.Lane
                    : nextKind != selectedEvent.Kind ||
                      nextPhase != selectedEvent.Phase ||
                      nextStepIndex != selectedEvent.StepIndex ||
                      (nextPhase == CombatEventPhase.Ready && nextReadyCueType != selectedEvent.ReadyCueType) ||
                      nextDirectionHint != selectedEvent.DirectionHint ||
                      nextRangedPhraseId != selectedEvent.RangedPhraseId ||
                      nextRangedCueIndex != selectedEvent.RangedCueIndex ||
                      nextRangedAnchorIndex != selectedEvent.RangedAnchorIndex;

                RecordChartChange("Edit Chart Event");
                selectedEvent.Beat = snapEnabled ? QuantizeBeat(nextBeat) : Mathf.Max(0f, nextBeat);
                selectedEvent.Lane = Mathf.Clamp(nextLane, 0, laneCount - 1);
                ApplyEventKind(selectedEvent, nextKind);
                selectedEvent.Phase = nextPhase;
                selectedEvent.StepIndex = Mathf.Max(0, nextStepIndex);
                selectedEvent.ReadyCueType = nextPhase == CombatEventPhase.Ready ? nextReadyCueType : ReadyCueType.None;
                selectedEvent.DirectionHint =
                    nextKind != CombatEventKind.None && ShouldShowDirectionHint(nextKind, nextPhase, nextReadyCueType)
                        ? nextDirectionHint
                        : PlayerRelativeDirection.None;
                selectedEvent.NudgeMs = nextNudgeMs;
                if (IsRangedPhraseKind(selectedEvent.Kind))
                {
                    selectedEvent.RangedPhraseId = nextRangedPhraseId ?? string.Empty;
                    selectedEvent.RangedCueIndex = Mathf.Max(-1, nextRangedCueIndex);
                    selectedEvent.RangedAnchorIndex = Mathf.Max(-1, nextRangedAnchorIndex);
                }
                else
                {
                    ClearRangedMetadata(selectedEvent);
                }

                if (shouldClearPatternLink)
                {
                    ClearAppliedPatternLink(selectedEvent);
                }

                cursorBeat = selectedEvent.Beat;
                selectedLane = selectedEvent.Lane;
                SortEvents();
                MarkChartDirty();
            }

            GUILayout.Space(8f);
            DrawPatternPanel();
            GUILayout.Space(8f);
            DrawRangedCueReplacementPanel();
            GUILayout.Space(8f);

            if (GUILayout.Button("Move Event To Cursor"))
            {
                RecordChartChange("Move Chart Event");
                selectedEvent.Beat = snapEnabled ? QuantizeBeat(cursorBeat) : cursorBeat;
                selectedEvent.Lane = Mathf.Clamp(selectedLane, 0, laneCount - 1);
                SortEvents();
                MarkChartDirty();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawPatternPanel()
        {
            GUILayout.Label("Pattern", EditorStyles.boldLabel);

            if (selectedEvent == null || currentChart == null)
            {
                return;
            }

            if (selectedEvent.Phase != CombatEventPhase.Timing)
            {
                EditorGUILayout.HelpBox("Pattern generation uses the selected Timing event as the anchor.", MessageType.Info);
                return;
            }

            LaneBindingData binding = GetLaneBindingForLane(selectedEvent.Lane);
            if (binding == null || binding.BindingType != LaneBindingType.Monster || string.IsNullOrWhiteSpace(binding.BindingId))
            {
                EditorGUILayout.HelpBox("Bind a monster to this lane before applying a pattern.", MessageType.Info);
                return;
            }

            MonsterDefinition monsterDefinition = FindMonsterDefinitionById(binding.BindingId);
            if (monsterDefinition == null)
            {
                EditorGUILayout.HelpBox("The bound monster definition could not be found.", MessageType.Warning);
                return;
            }

            PatternDefinition[] supportedPatterns = GetSupportedPatterns(monsterDefinition);
            if (supportedPatterns.Length == 0)
            {
                EditorGUILayout.HelpBox("This monster does not expose any supported patterns yet.", MessageType.Warning);
                return;
            }

            selectedPatternPopupIndex = Mathf.Clamp(selectedPatternPopupIndex, 0, supportedPatterns.Length - 1);
            string[] popupOptions = BuildPatternPopupOptions(supportedPatterns);
            selectedPatternPopupIndex = EditorGUILayout.Popup("Pattern", selectedPatternPopupIndex, popupOptions);

            PatternDefinition selectedPatternDefinition = supportedPatterns[selectedPatternPopupIndex];
            EditorGUILayout.LabelField("Lane Monster", GetMonsterDisplayLabel(monsterDefinition));
            EditorGUILayout.LabelField("Combat Kind", selectedPatternDefinition.CombatKind.ToString());
            EditorGUILayout.HelpBox(BuildPatternDescription(selectedPatternDefinition), MessageType.None);

            if (IsRangedPhraseKind(selectedPatternDefinition.CombatKind))
            {
                EditorGUILayout.HelpBox("Ranged phrase patterns are rule presets. Use the Ranged Phrase generator instead of applying this as fixed steps.", MessageType.Info);
                return;
            }

            if (GUILayout.Button("Apply Pattern To Selected Timing"))
            {
                ApplyPatternToSelectedTiming(selectedPatternDefinition);
            }
        }

        private void DrawRangedCueReplacementPanel()
        {
            if (selectedEvent == null || currentChart == null || !IsRangedPhraseKind(selectedEvent.Kind))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedEvent.RangedPhraseId) || selectedEvent.RangedCueIndex < 0)
            {
                return;
            }

            GUILayout.Label("Ranged Cue", EditorStyles.boldLabel);
            if (selectedEvent.Kind == CombatEventKind.PlayerRangedAttack)
            {
                if (GUILayout.Button("Restore Cue To Phrase Threat"))
                {
                    RestoreSelectedRangedCueToPhraseThreat();
                }

                return;
            }

            if (GUILayout.Button("Convert Cue To Player Ranged"))
            {
                ConvertSelectedRangedCueToPlayerRangedAttack();
            }
        }

        private void HandleTimelineInput(Rect viewportRect)
        {
            Event currentEvent = Event.current;
            Vector2 localPosition = currentEvent.mousePosition - viewportRect.position + timelineScroll;

            if (isDraggingEvent)
            {
                if (currentEvent.type == EventType.MouseDrag && currentEvent.button == 0)
                {
                    UpdateDraggedEvent(localPosition);
                    currentEvent.Use();
                    return;
                }

                if (currentEvent.type == EventType.MouseUp && currentEvent.button == 0)
                {
                    CompleteDraggedEvent();
                    currentEvent.Use();
                    return;
                }
            }

            if (!viewportRect.Contains(currentEvent.mousePosition))
            {
                return;
            }

            if (currentEvent.type == EventType.ScrollWheel && currentEvent.control)
            {
                HandleTimelineZoom(currentEvent, viewportRect);
                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
            {
                HandleLeftTimelineClick(localPosition);
                currentEvent.Use();
            }
            else if (currentEvent.type == EventType.MouseDown && currentEvent.button == 1)
            {
                HandleRightTimelineClick(localPosition);
                currentEvent.Use();
            }
        }

        private void HandleLeftTimelineClick(Vector2 localPosition)
        {
            bool wasPlaying = IsPreviewPlaying();
            if (TryGetEventAtPosition(localPosition, out ChartEventData hitEvent))
            {
                selectedEvent = hitEvent;
                cursorBeat = hitEvent.Beat;
                selectedLane = hitEvent.Lane;
                MovePreviewToCursorAndPause(wasPlaying);
                BeginDraggedEvent(hitEvent, localPosition);
                Repaint();
                return;
            }

            selectedEvent = null;
            cursorBeat = GetBeatFromPosition(localPosition.x);
            selectedLane = GetLaneFromPosition(localPosition.y);
            MovePreviewToCursorAndPause(wasPlaying);
            Repaint();
        }

        private void HandleRightTimelineClick(Vector2 localPosition)
        {
            bool wasPlaying = IsPreviewPlaying();
            GenericMenu menu = new GenericMenu();

            if (TryGetEventAtPosition(localPosition, out ChartEventData hitEvent))
            {
                selectedEvent = hitEvent;
                cursorBeat = hitEvent.Beat;
                selectedLane = hitEvent.Lane;
                MovePreviewToCursorAndPause(wasPlaying);

                menu.AddItem(new GUIContent("Delete"), false, DeleteSelectedEvent);
                menu.AddSeparator(string.Empty);
                AddConvertMenuItems(menu);
            }
            else
            {
                selectedEvent = null;
                cursorBeat = GetBeatFromPosition(localPosition.x);
                selectedLane = GetLaneFromPosition(localPosition.y);
                MovePreviewToCursorAndPause(wasPlaying);

                AddCreateMenuItems(menu);
            }

            menu.ShowAsContext();
        }

        private void HandleKeyboardShortcuts()
        {
            if (currentChart == null || EditorGUIUtility.editingTextField)
            {
                return;
            }

            Event currentEvent = Event.current;
            if (currentEvent.type != EventType.KeyDown)
            {
                return;
            }

            if (currentEvent.keyCode == KeyCode.Space)
            {
                AddOrUpdateEventAtCursor(CombatEventKind.None);
                currentEvent.Use();
            }
            else if (currentEvent.keyCode == KeyCode.Delete || currentEvent.keyCode == KeyCode.Backspace)
            {
                DeleteSelectedEvent();
                currentEvent.Use();
            }
            else if ((currentEvent.control || currentEvent.command) && currentEvent.keyCode == KeyCode.S)
            {
                SaveCurrentChart();
                currentEvent.Use();
            }
        }

        private void AddOrUpdateEventAtCursor(CombatEventKind eventKind)
        {
            if (currentChart == null)
            {
                return;
            }

            float targetBeat = snapEnabled ? QuantizeBeat(cursorBeat) : cursorBeat;
            int targetLane = Mathf.Clamp(selectedLane, 0, Mathf.Max(0, currentChart.Meta.LaneCount - 1));

            RecordChartChange("Add Chart Event");

            ChartEventData eventData = FindEventInSlot(targetBeat, targetLane);
            CombatEventKind previousKind = eventData != null ? eventData.Kind : CombatEventKind.None;
            if (eventData == null)
            {
                eventData = new ChartEventData
                {
                    Beat = targetBeat,
                    Lane = targetLane,
                    Phase = CombatEventPhase.Timing,
                    StepIndex = 0,
                    ReadyCueType = ReadyCueType.None,
                    DirectionHint = PlayerRelativeDirection.None,
                    NudgeMs = 0f
                };
                currentChart.Events.Add(eventData);
            }

            ApplyEventKind(eventData, eventKind);
            if (eventKind == CombatEventKind.None || previousKind != eventKind)
            {
                ClearAppliedPatternLink(eventData);
            }

            selectedEvent = eventData;
            cursorBeat = eventData.Beat;
            selectedLane = eventData.Lane;

            SortEvents();
            MarkChartDirty();
        }

        private void ConvertSelectedEvent(CombatEventKind eventKind)
        {
            if (selectedEvent == null)
            {
                return;
            }

            RecordChartChange("Convert Chart Event");
            CombatEventKind previousKind = selectedEvent.Kind;
            ApplyEventKind(selectedEvent, eventKind);
            if (eventKind == CombatEventKind.None || previousKind != eventKind)
            {
                ClearAppliedPatternLink(selectedEvent);
            }

            SortEvents();
            MarkChartDirty();
        }

        private void ConvertSelectedRangedCueToPlayerRangedAttack()
        {
            if (!TryGetSelectedRangedCuePair(out ChartEventData readyEvent, out ChartEventData timingEvent, out string errorMessage))
            {
                EditorUtility.DisplayDialog("Player Ranged Cue Failed", errorMessage, "OK");
                return;
            }

            if (HasOtherPlayerRangedCueInPhrase(selectedEvent))
            {
                EditorUtility.DisplayDialog(
                    "Player Ranged Cue Failed",
                    "This ranged phrase already has a PlayerRangedAttack cue. Only one is allowed for now.",
                    "OK");
                return;
            }

            RecordChartChange("Convert Ranged Cue To Player Ranged");
            ApplyRangedCueKind(readyEvent, CombatEventKind.PlayerRangedAttack);
            ApplyRangedCueKind(timingEvent, CombatEventKind.PlayerRangedAttack);
            SortEvents();
            MarkChartDirty();
        }

        private void RestoreSelectedRangedCueToPhraseThreat()
        {
            if (!TryGetSelectedRangedCuePair(out ChartEventData readyEvent, out ChartEventData timingEvent, out string errorMessage))
            {
                EditorUtility.DisplayDialog("Restore Ranged Cue Failed", errorMessage, "OK");
                return;
            }

            if (!TryResolveLinkedRangedThreatKind(selectedEvent, out CombatEventKind threatKind, out errorMessage))
            {
                EditorUtility.DisplayDialog("Restore Ranged Cue Failed", errorMessage, "OK");
                return;
            }

            RecordChartChange("Restore Ranged Cue To Phrase Threat");
            ApplyRangedCueKind(readyEvent, threatKind);
            ApplyRangedCueKind(timingEvent, threatKind);
            SortEvents();
            MarkChartDirty();
        }

        private bool TryGetSelectedRangedCuePair(
            out ChartEventData readyEvent,
            out ChartEventData timingEvent,
            out string errorMessage)
        {
            readyEvent = null;
            timingEvent = null;
            errorMessage = string.Empty;

            if (currentChart == null || currentChart.Events == null || selectedEvent == null)
            {
                errorMessage = "Select a ranged cue event first.";
                return false;
            }

            if (!IsRangedPhraseKind(selectedEvent.Kind) ||
                string.IsNullOrWhiteSpace(selectedEvent.RangedPhraseId) ||
                selectedEvent.RangedCueIndex < 0)
            {
                errorMessage = "Selected event is not part of a ranged phrase cue.";
                return false;
            }

            for (int i = 0; i < currentChart.Events.Count; i++)
            {
                ChartEventData eventData = currentChart.Events[i];
                if (eventData == null ||
                    !IsRangedPhraseKind(eventData.Kind) ||
                    eventData.Lane != selectedEvent.Lane ||
                    eventData.RangedCueIndex != selectedEvent.RangedCueIndex ||
                    !string.Equals(eventData.RangedPhraseId, selectedEvent.RangedPhraseId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (eventData.Phase == CombatEventPhase.Ready)
                {
                    if (readyEvent != null)
                    {
                        errorMessage = "Selected ranged cue has duplicate Ready events.";
                        return false;
                    }

                    readyEvent = eventData;
                }
                else if (eventData.Phase == CombatEventPhase.Timing)
                {
                    if (timingEvent != null)
                    {
                        errorMessage = "Selected ranged cue has duplicate Timing events.";
                        return false;
                    }

                    timingEvent = eventData;
                }
            }

            if (readyEvent == null || timingEvent == null)
            {
                errorMessage = "Selected ranged cue must have exactly one Ready event and one Timing event.";
                return false;
            }

            return true;
        }

        private bool HasOtherPlayerRangedCueInPhrase(ChartEventData referenceEvent)
        {
            if (currentChart == null || currentChart.Events == null || referenceEvent == null)
            {
                return false;
            }

            for (int i = 0; i < currentChart.Events.Count; i++)
            {
                ChartEventData eventData = currentChart.Events[i];
                if (eventData == null ||
                    eventData.Kind != CombatEventKind.PlayerRangedAttack ||
                    eventData.Phase != CombatEventPhase.Ready ||
                    eventData.Lane != referenceEvent.Lane ||
                    eventData.RangedCueIndex == referenceEvent.RangedCueIndex ||
                    !string.Equals(eventData.RangedPhraseId, referenceEvent.RangedPhraseId, StringComparison.Ordinal))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private bool TryResolveLinkedRangedThreatKind(
            ChartEventData eventData,
            out CombatEventKind threatKind,
            out string errorMessage)
        {
            threatKind = CombatEventKind.None;
            errorMessage = string.Empty;

            PatternDefinition patternDefinition = FindPatternDefinitionByLink(
                eventData.AppliedPatternAssetGuid,
                eventData.AppliedPatternId);
            if (patternDefinition == null || !IsRangedThreatPatternKind(patternDefinition.CombatKind))
            {
                errorMessage = "Selected player ranged cue must keep a link to its original ranged phrase pattern.";
                return false;
            }

            threatKind = patternDefinition.CombatKind;
            return true;
        }

        private static void ApplyRangedCueKind(ChartEventData eventData, CombatEventKind eventKind)
        {
            if (eventData == null)
            {
                return;
            }

            eventData.Kind = eventKind;
            eventData.ReadyCueType = ReadyCueType.None;
            eventData.MovementResolveMode = MovementResolveMode.None;
            eventData.MoveUnits = 0f;
            eventData.TargetBandUnits = 0f;
            eventData.MovementDirectionHint = MovementDirectionHint.None;
            eventData.PlayerKnockbackUnits = 0f;
            eventData.MonsterKnockbackUnits = 0f;

            if (eventData.Phase == CombatEventPhase.Timing &&
                eventKind == CombatEventKind.RangedThreatDodgeOnly)
            {
                eventData.DirectionHint = eventData.DirectionHint == PlayerRelativeDirection.None
                    ? (eventData.RangedCueIndex % 2 == 0 ? PlayerRelativeDirection.Left : PlayerRelativeDirection.Right)
                    : eventData.DirectionHint;
                return;
            }

            eventData.DirectionHint = PlayerRelativeDirection.None;
        }

        private void GenerateRangedDodgePhraseAtCursor(PatternDefinition patternDefinition)
        {
            if (currentChart == null)
            {
                return;
            }

            if (patternDefinition == null || patternDefinition.CombatKind != CombatEventKind.RangedThreatDodgeOnly)
            {
                EditorUtility.DisplayDialog("Ranged Phrase Failed", "Select a ranged dodge pattern first.", "OK");
                return;
            }

            int targetLane = Mathf.Clamp(selectedLane, 0, Mathf.Max(0, currentChart.Meta.LaneCount - 1));
            MonsterDefinition laneMonsterDefinition = GetMonsterDefinitionForLane(targetLane);
            CombatEventKind[] allowedEventKinds = GetAllowedCombatEventKinds(laneMonsterDefinition);
            if (!ContainsCombatEventKind(allowedEventKinds, CombatEventKind.RangedThreatDodgeOnly))
            {
                EditorUtility.DisplayDialog("Ranged Phrase Failed", "Bind a monster that supports ranged dodge editing to this lane first.", "OK");
                return;
            }

            int cueCount = Mathf.Clamp(patternDefinition.RangedCueCount, 1, 32);
            int anchorCount = Mathf.Clamp(patternDefinition.RangedAnchorCount, 1, 32);
            float cueIntervalBeats = Mathf.Max(0.001f, patternDefinition.RangedCueIntervalBeats);
            float responseOffsetBeats = Mathf.Max(0.001f, patternDefinition.RangedResponseOffsetBeats);
            float startBeat = snapEnabled ? QuantizeBeat(cursorBeat) : Mathf.Max(0f, cursorBeat);
            for (int i = 0; i < cueCount; i++)
            {
                float callBeat = startBeat + i * cueIntervalBeats;
                float responseBeat = callBeat + responseOffsetBeats;
                if (HasBlockingEventAtSlot(callBeat, targetLane) || HasBlockingEventAtSlot(responseBeat, targetLane))
                {
                    EditorUtility.DisplayDialog(
                        "Ranged Phrase Failed",
                        $"A real event already exists in the generated phrase area near beat {callBeat:0.###}. Remove it or move the cursor.",
                        "OK");
                    return;
                }
            }

            RecordChartChange("Generate Ranged Dodge Phrase");
            string phraseId = Guid.NewGuid().ToString("N");
            ChartEventData firstTimingEvent = null;
            AppliedPatternMetadata appliedPatternMetadata = BuildRangedAppliedPatternMetadata(
                patternDefinition,
                phraseId,
                startBeat + responseOffsetBeats,
                cueCount * 2);

            for (int i = 0; i < cueCount; i++)
            {
                float callBeat = startBeat + i * cueIntervalBeats;
                float responseBeat = callBeat + responseOffsetBeats;
                int anchorIndex = i % anchorCount;

                ChartEventData callEvent = FindOrCreateEventInSlot(callBeat, targetLane);
                ApplyRangedDodgeEvent(
                    callEvent,
                    appliedPatternMetadata,
                    phraseId,
                    i,
                    anchorIndex,
                    callBeat,
                    targetLane,
                    i,
                    CombatEventPhase.Ready,
                    PlayerRelativeDirection.None,
                    patternDefinition.EntryBandUnits);

                ChartEventData timingEvent = FindOrCreateEventInSlot(responseBeat, targetLane);
                ApplyRangedDodgeEvent(
                    timingEvent,
                    appliedPatternMetadata,
                    phraseId,
                    i,
                    anchorIndex,
                    responseBeat,
                    targetLane,
                    cueCount + i,
                    CombatEventPhase.Timing,
                    i % 2 == 0 ? PlayerRelativeDirection.Left : PlayerRelativeDirection.Right,
                    patternDefinition.EntryBandUnits);

                firstTimingEvent ??= timingEvent;
            }

            selectedEvent = firstTimingEvent;
            cursorBeat = firstTimingEvent != null ? firstTimingEvent.Beat : startBeat;
            selectedLane = targetLane;
            SortEvents();
            MarkChartDirty();
            ShowNotification(new GUIContent("Generated ranged dodge call/response phrase."));
        }

        private void GenerateRangedCounterPhraseAtCursor(PatternDefinition patternDefinition)
        {
            if (currentChart == null)
            {
                return;
            }

            if (patternDefinition == null || patternDefinition.CombatKind != CombatEventKind.RangedThreatCounterable)
            {
                EditorUtility.DisplayDialog("Ranged Counter Failed", "Select a ranged counter pattern first.", "OK");
                return;
            }

            int targetLane = Mathf.Clamp(selectedLane, 0, Mathf.Max(0, currentChart.Meta.LaneCount - 1));
            MonsterDefinition laneMonsterDefinition = GetMonsterDefinitionForLane(targetLane);
            CombatEventKind[] allowedEventKinds = GetAllowedCombatEventKinds(laneMonsterDefinition);
            if (!ContainsCombatEventKind(allowedEventKinds, CombatEventKind.RangedThreatCounterable))
            {
                EditorUtility.DisplayDialog("Ranged Counter Failed", "Bind a monster that supports ranged counter editing to this lane first.", "OK");
                return;
            }

            int cueCount = Mathf.Clamp(patternDefinition.RangedCueCount, 1, 32);
            int anchorCount = Mathf.Clamp(patternDefinition.RangedAnchorCount, 1, 32);
            float cueIntervalBeats = Mathf.Max(0.001f, patternDefinition.RangedCueIntervalBeats);
            float responseOffsetBeats = Mathf.Max(0.001f, patternDefinition.RangedResponseOffsetBeats);
            float startBeat = snapEnabled ? QuantizeBeat(cursorBeat) : Mathf.Max(0f, cursorBeat);
            for (int i = 0; i < cueCount; i++)
            {
                float callBeat = startBeat + i * cueIntervalBeats;
                float responseBeat = callBeat + responseOffsetBeats;
                if (HasBlockingEventAtSlot(callBeat, targetLane) || HasBlockingEventAtSlot(responseBeat, targetLane))
                {
                    EditorUtility.DisplayDialog(
                        "Ranged Counter Failed",
                        $"A real event already exists in the generated phrase area near beat {callBeat:0.###}. Remove it or move the cursor.",
                        "OK");
                    return;
                }
            }

            RecordChartChange("Generate Ranged Counter Phrase");
            string phraseId = Guid.NewGuid().ToString("N");
            ChartEventData firstTimingEvent = null;
            AppliedPatternMetadata appliedPatternMetadata = BuildRangedAppliedPatternMetadata(
                patternDefinition,
                phraseId,
                startBeat + responseOffsetBeats,
                cueCount * 2);

            for (int i = 0; i < cueCount; i++)
            {
                float callBeat = startBeat + i * cueIntervalBeats;
                float responseBeat = callBeat + responseOffsetBeats;
                int anchorIndex = i % anchorCount;

                ChartEventData callEvent = FindOrCreateEventInSlot(callBeat, targetLane);
                ApplyRangedPhraseEvent(
                    callEvent,
                    CombatEventKind.RangedThreatCounterable,
                    appliedPatternMetadata,
                    phraseId,
                    i,
                    anchorIndex,
                    callBeat,
                    targetLane,
                    i,
                    CombatEventPhase.Ready,
                    PlayerRelativeDirection.None,
                    patternDefinition.EntryBandUnits);

                ChartEventData timingEvent = FindOrCreateEventInSlot(responseBeat, targetLane);
                ApplyRangedPhraseEvent(
                    timingEvent,
                    CombatEventKind.RangedThreatCounterable,
                    appliedPatternMetadata,
                    phraseId,
                    i,
                    anchorIndex,
                    responseBeat,
                    targetLane,
                    cueCount + i,
                    CombatEventPhase.Timing,
                    PlayerRelativeDirection.None,
                    patternDefinition.EntryBandUnits);

                firstTimingEvent ??= timingEvent;
            }

            selectedEvent = firstTimingEvent;
            cursorBeat = firstTimingEvent != null ? firstTimingEvent.Beat : startBeat;
            selectedLane = targetLane;
            SortEvents();
            MarkChartDirty();
            ShowNotification(new GUIContent("Generated ranged counter call/response phrase."));
        }

        private bool HasBlockingEventAtSlot(float beat, int lane)
        {
            ChartEventData eventData = FindEventInSlot(beat, lane);
            return eventData != null && eventData.Kind != CombatEventKind.None;
        }

        private ChartEventData FindOrCreateEventInSlot(float beat, int lane)
        {
            ChartEventData eventData = FindEventInSlot(beat, lane);
            if (eventData != null)
            {
                return eventData;
            }

            eventData = new ChartEventData();
            currentChart.Events.Add(eventData);
            return eventData;
        }

        private static void ApplyRangedDodgeEvent(
            ChartEventData eventData,
            AppliedPatternMetadata appliedPatternMetadata,
            string phraseId,
            int cueIndex,
            int anchorIndex,
            float beat,
            int lane,
            int stepIndex,
            CombatEventPhase phase,
            PlayerRelativeDirection directionHint,
            float entryBandUnits,
            float preservedNudgeMs = 0f)
        {
            ApplyRangedPhraseEvent(
                eventData,
                CombatEventKind.RangedThreatDodgeOnly,
                appliedPatternMetadata,
                phraseId,
                cueIndex,
                anchorIndex,
                beat,
                lane,
                stepIndex,
                phase,
                directionHint,
                entryBandUnits,
                preservedNudgeMs);
        }

        private static void ApplyRangedPhraseEvent(
            ChartEventData eventData,
            CombatEventKind eventKind,
            AppliedPatternMetadata appliedPatternMetadata,
            string phraseId,
            int cueIndex,
            int anchorIndex,
            float beat,
            int lane,
            int stepIndex,
            CombatEventPhase phase,
            PlayerRelativeDirection directionHint,
            float entryBandUnits,
            float preservedNudgeMs = 0f)
        {
            eventData.Beat = beat;
            eventData.Lane = lane;
            eventData.Kind = eventKind;
            eventData.Phase = phase;
            eventData.StepIndex = stepIndex;
            eventData.ReadyCueType = ReadyCueType.None;
            eventData.DirectionHint = phase == CombatEventPhase.Timing ? directionHint : PlayerRelativeDirection.None;
            eventData.NudgeMs = preservedNudgeMs;
            eventData.MovementResolveMode = MovementResolveMode.None;
            eventData.MoveUnits = 0f;
            eventData.TargetBandUnits = 0f;
            eventData.MovementDirectionHint = MovementDirectionHint.None;
            eventData.EntryBandUnits = Mathf.Max(0f, entryBandUnits);
            eventData.PlayerKnockbackUnits = 0f;
            eventData.MonsterKnockbackUnits = 0f;
            eventData.RangedPhraseId = phraseId ?? string.Empty;
            eventData.RangedCueIndex = cueIndex;
            eventData.RangedAnchorIndex = anchorIndex;
            ApplyAppliedPatternMetadata(eventData, appliedPatternMetadata);
        }

        private void ApplyEventKind(ChartEventData eventData, CombatEventKind eventKind)
        {
            eventData.Kind = eventKind;

            if (eventKind == CombatEventKind.None)
            {
                eventData.Phase = CombatEventPhase.Timing;
                eventData.StepIndex = 0;
                eventData.ReadyCueType = ReadyCueType.None;
                eventData.DirectionHint = PlayerRelativeDirection.None;
                eventData.MovementResolveMode = MovementResolveMode.None;
                eventData.MoveUnits = 0f;
                eventData.TargetBandUnits = 0f;
                eventData.MovementDirectionHint = MovementDirectionHint.None;
                eventData.EntryBandUnits = 0f;
                eventData.PlayerKnockbackUnits = 0f;
                eventData.MonsterKnockbackUnits = 0f;
                ClearRangedMetadata(eventData);
                ClearAppliedPatternLink(eventData);
                return;
            }

            if (!IsRangedPhraseKind(eventKind))
            {
                ClearRangedMetadata(eventData);
            }

            if (eventKind != CombatEventKind.MeleeThreatCounterable &&
                eventKind != CombatEventKind.PlayerDashAttack)
            {
                eventData.PlayerKnockbackUnits = 0f;
                eventData.MonsterKnockbackUnits = 0f;
            }
        }

        private void ApplyPatternToSelectedTiming(PatternDefinition patternDefinition)
        {
            if (currentChart == null || selectedEvent == null || patternDefinition == null)
            {
                return;
            }

            if (selectedEvent.Phase != CombatEventPhase.Timing)
            {
                EditorUtility.DisplayDialog("Pattern Apply Failed", "Select a Timing event first.", "OK");
                return;
            }

            if (!TryBuildPatternSteps(patternDefinition, out List<PatternStepDefinition> orderedSteps, out int timingStepIndex, out string errorMessage))
            {
                EditorUtility.DisplayDialog("Pattern Apply Failed", errorMessage, "OK");
                return;
            }

            int targetLane = selectedEvent.Lane;
            float targetTimingBeat = selectedEvent.Beat;
            List<ChartEventData> targetEvents = new List<ChartEventData>(orderedSteps.Count);

            for (int i = 0; i < orderedSteps.Count; i++)
            {
                PatternStepDefinition stepDefinition = orderedSteps[i];
                float targetBeat = targetTimingBeat + stepDefinition.BeatOffsetFromTiming;
                if (targetBeat < 0f)
                {
                    EditorUtility.DisplayDialog("Pattern Apply Failed", "Pattern steps cannot be placed before beat 0.", "OK");
                    return;
                }

                if (i == timingStepIndex)
                {
                    targetEvents.Add(selectedEvent);
                    continue;
                }

                ChartEventData existingEvent = FindEventInSlot(targetBeat, targetLane);
                if (existingEvent == null)
                {
                    targetEvents.Add(null);
                    continue;
                }

                if (existingEvent.Kind == CombatEventKind.None)
                {
                    targetEvents.Add(existingEvent);
                    continue;
                }

                EditorUtility.DisplayDialog(
                    "Pattern Apply Failed",
                    $"A real event already exists at beat {targetBeat:0.###} on lane {targetLane + 1}. Remove it before applying this pattern.",
                    "OK");
                return;
            }

            RecordChartChange("Apply Monster Pattern");
            AppliedPatternMetadata appliedPatternMetadata = BuildAppliedPatternMetadata(
                patternDefinition,
                targetTimingBeat,
                timingStepIndex,
                orderedSteps.Count);

            for (int i = 0; i < orderedSteps.Count; i++)
            {
                PatternStepDefinition stepDefinition = orderedSteps[i];
                float targetBeat = targetTimingBeat + stepDefinition.BeatOffsetFromTiming;
                ChartEventData targetEvent = targetEvents[i];

                if (targetEvent == null)
                {
                    targetEvent = new ChartEventData();
                    currentChart.Events.Add(targetEvent);
                }

                ApplyPatternStepToEvent(
                    targetEvent,
                    patternDefinition,
                    stepDefinition,
                    targetBeat,
                    targetLane,
                    i,
                    targetEvent.NudgeMs,
                    appliedPatternMetadata);
            }

            selectedEvent = targetEvents[timingStepIndex];
            SortEvents();
            MarkChartDirty();
            ShowNotification(new GUIContent($"Applied pattern '{GetPatternDisplayLabel(patternDefinition)}'."));
        }

        private void DeleteSelectedEvent()
        {
            if (currentChart == null || selectedEvent == null)
            {
                return;
            }

            ClearDraggedEventState();

            if (!currentChart.Events.Contains(selectedEvent))
            {
                selectedEvent = null;
                return;
            }

            RecordChartChange("Delete Chart Event");
            currentChart.Events.Remove(selectedEvent);
            selectedEvent = null;
            MarkChartDirty();
        }

        private void ClearAllEvents()
        {
            if (currentChart == null || currentChart.Events == null || currentChart.Events.Count == 0)
            {
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Clear All Events",
                "Delete all events in the current chart?",
                "Delete All",
                "Cancel");

            if (!confirmed)
            {
                return;
            }

            RecordChartChange("Clear Chart Events");
            currentChart.Events.Clear();
            selectedEvent = null;
            ClearDraggedEventState();
            MarkChartDirty();
        }

        private void SaveCurrentChart()
        {
            if (currentChart == null)
            {
                return;
            }

            if (!TryAutoSyncAppliedPatterns(out int syncedPatternGroupCount, out string autoSyncErrorMessage))
            {
                EditorUtility.DisplayDialog("Cannot Save Chart", autoSyncErrorMessage, "OK");
                return;
            }

            ChartValidationResult validation = ChartValidator.Validate(currentChart);
            if (validation.HasError)
            {
                EditorUtility.DisplayDialog("Cannot Save Chart", "Chart has validation errors. Resolve errors before saving.", "OK");
                return;
            }

            if (!TryBuildResolvedTimelineBuildContext(out ChartResolvedTimelineBuildContext buildContext, out string buildContextErrorMessage))
            {
                EditorUtility.DisplayDialog("Cannot Save Chart", buildContextErrorMessage, "OK");
                EditorUtility.SetDirty(currentChart);
                return;
            }

            if (!ChartResolvedTimelineBuilder.TryGenerate(currentChart, FindMonsterDefinitionById, out string errorMessage, buildContext))
            {
                EditorUtility.DisplayDialog("Cannot Save Chart", errorMessage, "OK");
                EditorUtility.SetDirty(currentChart);
                return;
            }

            EditorUtility.SetDirty(currentChart);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (syncedPatternGroupCount > 0)
            {
                ShowNotification(new GUIContent($"Auto-synced {syncedPatternGroupCount} pattern instance(s) before save."));
            }

            if (buildContext != null && buildContext.Warnings.Count > 0)
            {
                EditorUtility.DisplayDialog(
                    "Chart Saved With Warnings",
                    string.Join("\n", buildContext.Warnings),
                    "OK");
            }
        }

        private bool TryBuildResolvedTimelineBuildContext(
            out ChartResolvedTimelineBuildContext buildContext,
            out string errorMessage)
        {
            buildContext = null;
            errorMessage = string.Empty;

            bool requiresRangedCounter = ChartContainsEventKind(CombatEventKind.RangedThreatCounterable);
            bool requiresPlayerRanged = ChartContainsEventKind(CombatEventKind.PlayerRangedAttack);
            if (!requiresRangedCounter && !requiresPlayerRanged)
            {
                return true;
            }

            CombatDuelRoot duelRoot = UnityEngine.Object.FindFirstObjectByType<CombatDuelRoot>();
            if (duelRoot == null)
            {
                errorMessage = "Ranged attacks require CombatDuelRoot in the active scene.";
                return false;
            }

            if (duelRoot.PlayerActor == null)
            {
                errorMessage = "Ranged attacks require PlayerCombatActor on CombatDuelRoot.";
                return false;
            }

            if (duelRoot.MonsterActor == null)
            {
                errorMessage = "Ranged attacks require MonsterCombatActor on CombatDuelRoot.";
                return false;
            }

            if (requiresPlayerRanged)
            {
                Transform shootPoint = FindChildByName(duelRoot.PlayerActor.transform, "ShootPoint");
                if (shootPoint == null)
                {
                    errorMessage = "PlayerRangedAttack requires ShootPoint under the player.";
                    return false;
                }

                Transform hitPoint = FindChildByName(duelRoot.MonsterActor.transform, "HitPoint");
                if (hitPoint == null)
                {
                    errorMessage = "PlayerRangedAttack requires HitPoint under the monster.";
                    return false;
                }

                Transform attackMarkAnchor = FindChildByName(duelRoot.MonsterActor.transform, "AttackMarkAnchor");
                if (attackMarkAnchor == null)
                {
                    errorMessage = "PlayerRangedAttack requires AttackMarkAnchor under the monster.";
                    return false;
                }
            }

            if (!requiresRangedCounter)
            {
                return true;
            }

            Transform playerHitAnchor = FindChildByName(duelRoot.PlayerActor.transform, "PlayerHitAnchor");
            if (playerHitAnchor == null)
            {
                errorMessage = "RangedThreatCounterable requires PlayerHitAnchor under the player.";
                return false;
            }

            float safeUnitSize = Mathf.Max(0.0001f, duelRoot.UnitSize);

            Transform rangedAnchorRoot = duelRoot.MonsterActor.transform.Find("RangedAnchorRoot");
            if (rangedAnchorRoot == null)
            {
                errorMessage = "RangedThreatCounterable requires RangedAnchorRoot under the monster.";
                return false;
            }

            if (rangedAnchorRoot.childCount <= 0)
            {
                errorMessage = "RangedThreatCounterable requires at least one ranged anchor under RangedAnchorRoot.";
                return false;
            }

            List<Transform> anchors = new List<Transform>(rangedAnchorRoot.childCount);
            for (int i = 0; i < rangedAnchorRoot.childCount; i++)
            {
                Transform child = rangedAnchorRoot.GetChild(i);
                if (child != null)
                {
                    anchors.Add(child);
                }
            }

            anchors.Sort(CompareTransformsByName);
            List<Vector3> anchorOffsets = new List<Vector3>(anchors.Count);
            for (int i = 0; i < anchors.Count; i++)
            {
                Vector3 localOffset = duelRoot.MonsterActor.transform.InverseTransformPoint(anchors[i].position) / safeUnitSize;
                anchorOffsets.Add(localOffset);
            }

            CombatJudgementSystem judgementSystem = UnityEngine.Object.FindFirstObjectByType<CombatJudgementSystem>();
            if (judgementSystem == null)
            {
                errorMessage = "RangedThreatCounterable requires CombatJudgementSystem in the active scene.";
                return false;
            }

            buildContext = new ChartResolvedTimelineBuildContext
            {
                UnitSize = safeUnitSize,
                GoodWindowSec = Mathf.Max(0f, judgementSystem.GoodWindowMs * 0.001f),
                HasPlayerHitAnchor = true,
                PlayerHitAnchorLocalOffsetUnits =
                    duelRoot.PlayerActor.transform.InverseTransformPoint(playerHitAnchor.position) / safeUnitSize
            };
            buildContext.SetRangedAnchorLocalOffsets(anchorOffsets);
            return true;
        }

        private bool ChartContainsEventKind(CombatEventKind eventKind)
        {
            if (currentChart == null || currentChart.Events == null)
            {
                return false;
            }

            for (int i = 0; i < currentChart.Events.Count; i++)
            {
                ChartEventData eventData = currentChart.Events[i];
                if (eventData != null && eventData.Kind == eventKind)
                {
                    return true;
                }
            }

            return false;
        }

        private static Transform FindChildByName(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName))
            {
                return null;
            }

            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] != null && string.Equals(children[i].name, childName, StringComparison.Ordinal))
                {
                    return children[i];
                }
            }

            return null;
        }

        private static int CompareTransformsByName(Transform left, Transform right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            return string.Compare(left.name, right.name, StringComparison.Ordinal);
        }

        private void ImportJsonIntoCurrentChart()
        {
            if (currentChart == null || jsonImportAsset == null)
            {
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Import JSON Chart",
                "This will overwrite the current chart events and update BPM, Offset Sec, Song Length and Lane Count.\n\nSong Id, Song Name, FMOD Event Path and Grid will stay unchanged.",
                "Import",
                "Cancel");

            if (!confirmed)
            {
                return;
            }

            if (!JsonChartImportUtility.TryCreateImportData(jsonImportAsset, out JsonChartImportUtility.JsonChartImportData importData, out string errorMessage))
            {
                EditorUtility.DisplayDialog("Import Failed", errorMessage, "OK");
                return;
            }

            StopPreviewAudio(true);
            RecordChartChange("Import JSON Chart");

            ChartMeta meta = currentChart.Meta;
            meta.Bpm = importData.Bpm;
            meta.OffsetSec = importData.OffsetSec;
            meta.SongLengthSec = importData.SongLengthSec;
            meta.LaneCount = importData.LaneCount;
            meta.MaxEventsPerSlot = Mathf.Max(4, meta.MaxEventsPerSlot);

            currentChart.Events.Clear();
            currentChart.Events.AddRange(importData.Events);

            selectedEvent = null;
            cursorBeat = 0f;
            selectedLane = 0;

            SortEvents();
            ClampEditorState();
            MarkChartDirty();

            ShowNotification(new GUIContent($"Imported {importData.Events.Count} events."));
        }

        private void CreateNewChartAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Chart Data",
                "NewChartData",
                "asset",
                "Choose a save path for the chart asset.",
                "Assets/Data/Charts");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var chart = CreateInstance<ChartData>();
            AssetDatabase.CreateAsset(chart, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            AssignChart(chart);
            Selection.activeObject = chart;
            EditorGUIUtility.PingObject(chart);
        }

        private void AssignChart(ChartData chart)
        {
            StopPreviewAudio(false);
            currentChart = chart;
            selectedEvent = null;
            ClearDraggedEventState();
            leftPanelScroll = Vector2.zero;
            timelineScroll = Vector2.zero;
            rightPanelScroll = Vector2.zero;
            cursorBeat = 0f;
            previewTimeSec = 0f;
            lastPreviewTickTimeSec = 0f;
            selectedLane = 0;
            TryAutoAssignPreviewEvents();
            Repaint();
        }

        private void TryAssignFromSelection()
        {
            if (Selection.activeObject is ChartData chart && !ReferenceEquals(currentChart, chart))
            {
                AssignChart(chart);
            }
        }

        private void TryAutoAssignPreviewEvents()
        {
            if (string.IsNullOrWhiteSpace(metronomePreviewEventPath))
            {
                metronomePreviewEventPath = fmodPreviewBackend.FindEventPathByName(MetronomeEventSearchName);
            }

            if (string.IsNullOrWhiteSpace(eventPreviewEventPath))
            {
                eventPreviewEventPath = fmodPreviewBackend.FindEventPathByName(EventPreviewEventSearchName);
            }
        }

        private void ClampEditorState()
        {
            if (currentChart == null)
            {
                cursorBeat = 0f;
                selectedLane = 0;
                selectedEvent = null;
                return;
            }

            cursorBeat = Mathf.Clamp(snapEnabled ? QuantizeBeat(cursorBeat) : cursorBeat, 0f, GetTotalBeats());
            selectedLane = Mathf.Clamp(selectedLane, 0, Mathf.Max(0, currentChart.Meta.LaneCount - 1));

            if (selectedEvent != null && !currentChart.Events.Contains(selectedEvent))
            {
                selectedEvent = null;
            }

            if (draggedEvent != null && !currentChart.Events.Contains(draggedEvent))
            {
                ClearDraggedEventState();
            }
        }

        private float GetTotalBeats()
        {
            if (currentChart == null)
            {
                return 16f;
            }

            float bpm = currentChart.Meta.Bpm;
            float songLengthSec = currentChart.Meta.SongLengthSec;
            if (bpm <= 0f || songLengthSec <= 0f)
            {
                return 16f;
            }

            return Mathf.Max(16f, Mathf.Ceil(songLengthSec * bpm / 60f));
        }

        private float QuantizeBeat(float beat)
        {
            if (currentChart == null)
            {
                return Mathf.Max(0f, beat);
            }

            int gridDivision = Mathf.Max(1, currentChart.Meta.GridDivision);
            return Mathf.Max(0f, Mathf.Round(beat * gridDivision) / gridDivision);
        }

        private float GetBeatFromPosition(float positionX)
        {
            float beat = Mathf.Max(0f, positionX / pixelsPerBeat);
            return snapEnabled ? QuantizeBeat(beat) : beat;
        }

        private bool CanPreviewAudio()
        {
            return currentChart != null &&
                   !string.IsNullOrWhiteSpace(currentChart.Meta.FmodEventPath) &&
                   fmodPreviewBackend.CanPreviewEvent(currentChart.Meta.FmodEventPath);
        }

        private bool IsPreviewPlaying()
        {
            return CanPreviewAudio() && fmodPreviewBackend.IsMainPlaying(currentChart.Meta.FmodEventPath);
        }

        private void TogglePreviewPlayPause()
        {
            if (!CanPreviewAudio())
            {
                return;
            }

            if (IsPreviewPlaying())
            {
                PausePreviewAudio();
            }
            else
            {
                PlayPreviewAudio();
            }
        }

        private void PlayPreviewAudio()
        {
            if (!CanPreviewAudio())
            {
                return;
            }

            previewTimeSec = Mathf.Clamp(previewTimeSec, 0f, GetPreviewDurationSec());
            if (!fmodPreviewBackend.PlayMainEvent(currentChart.Meta.FmodEventPath, previewTimeSec, previewSpeed, previewMusicVolume))
            {
                return;
            }

            lastPreviewTickTimeSec = previewTimeSec;
            previewPaused = false;
            previewWasPlaying = true;
        }

        private void PausePreviewAudio()
        {
            previewTimeSec = GetResolvedPreviewTime();
            lastPreviewTickTimeSec = previewTimeSec;
            fmodPreviewBackend.PauseMainEvent();
            previewPaused = true;
            previewWasPlaying = false;
        }

        private void StopPreviewAudio(bool resetToStart)
        {
            fmodPreviewBackend.StopMainEvent();
            previewPaused = false;
            previewWasPlaying = false;

            if (resetToStart)
            {
                SetPreviewTime(0f, false);
            }
            else
            {
                lastPreviewTickTimeSec = previewTimeSec;
            }
        }

        private void SeekPreviewToCursor(bool keepPlaying)
        {
            SetPreviewTime(GetPreviewTimeFromBeat(cursorBeat), keepPlaying);
        }

        private void MovePreviewToCursorAndPause(bool wasPlaying)
        {
            if (wasPlaying)
            {
                PausePreviewAudio();
            }

            SeekPreviewToCursor(false);
            previewPaused = true;
        }

        private void SetPreviewTime(float timeSeconds, bool keepPlaying)
        {
            if (currentChart == null)
            {
                previewTimeSec = 0f;
                return;
            }

            previewTimeSec = Mathf.Clamp(timeSeconds, 0f, GetPreviewDurationSec());
            lastPreviewTickTimeSec = previewTimeSec;
            SyncCursorFromPreviewTime(previewTimeSec);
            fmodPreviewBackend.SeekMainEvent(currentChart.Meta.FmodEventPath, previewTimeSec, keepPlaying);

            if (keepPlaying)
            {
                previewPaused = false;
            }
        }

        private void TriggerPreviewAudioBetween(float previousTimeSec, float currentTimeSec)
        {
            if (currentChart == null || currentTimeSec <= previousTimeSec + PreviewTriggerEpsilon)
            {
                return;
            }

            TriggerMetronomeBetween(previousTimeSec, currentTimeSec);
            TriggerEventPreviewBetween(previousTimeSec, currentTimeSec);
        }

        private void TriggerMetronomeBetween(float previousTimeSec, float currentTimeSec)
        {
            if (!metronomeEnabled || string.IsNullOrWhiteSpace(metronomePreviewEventPath) || currentChart == null)
            {
                return;
            }

            float bpm = currentChart.Meta.Bpm;
            if (bpm <= 0f)
            {
                return;
            }

            float beatIntervalSec = 60f / bpm;
            float offsetSec = currentChart.Meta.OffsetSec;
            if (currentTimeSec + PreviewTriggerEpsilon < offsetSec)
            {
                return;
            }

            float scanStartSec = Mathf.Max(previousTimeSec, offsetSec);
            int firstTickIndex = Mathf.Max(0, Mathf.CeilToInt(((scanStartSec - offsetSec) - PreviewTriggerEpsilon) / beatIntervalSec));

            for (int tickIndex = firstTickIndex; ; tickIndex++)
            {
                float tickTimeSec = offsetSec + (tickIndex * beatIntervalSec);
                if (tickTimeSec > currentTimeSec + PreviewTriggerEpsilon)
                {
                    break;
                }

                if (tickTimeSec > previousTimeSec + PreviewTriggerEpsilon)
                {
                    fmodPreviewBackend.PlayOneShot(metronomePreviewEventPath, previewCueVolume);
                }
            }
        }

        private void TriggerEventPreviewBetween(float previousTimeSec, float currentTimeSec)
        {
            if (!eventPreviewEnabled || string.IsNullOrWhiteSpace(eventPreviewEventPath) || currentChart == null || currentChart.Events == null)
            {
                return;
            }

            for (int i = 0; i < currentChart.Events.Count; i++)
            {
                ChartEventData eventData = currentChart.Events[i];
                if (eventData == null)
                {
                    continue;
                }

                float eventTimeSec = GetPreviewTimeFromBeat(eventData.Beat) + (eventData.NudgeMs * 0.001f);
                if (eventTimeSec > previousTimeSec + PreviewTriggerEpsilon && eventTimeSec <= currentTimeSec + PreviewTriggerEpsilon)
                {
                    fmodPreviewBackend.PlayOneShot(eventPreviewEventPath, previewCueVolume);
                }
            }
        }

        private float GetResolvedPreviewTime()
        {
            if (currentChart == null)
            {
                return 0f;
            }

            if (fmodPreviewBackend.HasMainInstance(currentChart.Meta.FmodEventPath))
            {
                return Mathf.Clamp(fmodPreviewBackend.GetMainTimelinePositionSec(), 0f, GetPreviewDurationSec());
            }

            return Mathf.Clamp(previewTimeSec, 0f, GetPreviewDurationSec());
        }

        private float GetPreviewDurationSec()
        {
            if (currentChart == null)
            {
                return 0f;
            }

            return Mathf.Max(0f, currentChart.Meta.SongLengthSec);
        }

        private void SyncCursorFromPreviewTime(float timeSeconds)
        {
            cursorBeat = Mathf.Clamp(GetBeatFromPreviewTime(timeSeconds), 0f, GetTotalBeats());
        }

        private float GetPreviewTimeFromBeat(float beat)
        {
            if (currentChart == null)
            {
                return 0f;
            }

            float bpm = currentChart.Meta.Bpm;
            if (bpm <= 0f)
            {
                return 0f;
            }

            return Mathf.Max(0f, currentChart.Meta.OffsetSec + (beat * 60f / bpm));
        }

        private float GetBeatFromPreviewTime(float timeSeconds)
        {
            if (currentChart == null)
            {
                return 0f;
            }

            float bpm = currentChart.Meta.Bpm;
            if (bpm <= 0f)
            {
                return 0f;
            }

            return Mathf.Max(0f, (timeSeconds - currentChart.Meta.OffsetSec) * bpm / 60f);
        }

        private void EnsureCursorVisible()
        {
            if (timelineViewportWidth <= 0f)
            {
                return;
            }

            float cursorPositionX = cursorBeat * pixelsPerBeat;
            float leftThreshold = timelineScroll.x + (timelineViewportWidth * 0.25f);
            float rightThreshold = timelineScroll.x + (timelineViewportWidth * 0.75f);

            if (cursorPositionX < leftThreshold)
            {
                timelineScroll.x = cursorPositionX - (timelineViewportWidth * 0.25f);
            }
            else if (cursorPositionX > rightThreshold)
            {
                timelineScroll.x = cursorPositionX - (timelineViewportWidth * 0.75f);
            }

            float contentWidth = Mathf.Max(MinTimelineWidth, GetTotalBeats() * pixelsPerBeat + 80f);
            float maxScrollX = Mathf.Max(0f, contentWidth - timelineViewportWidth);
            timelineScroll.x = Mathf.Clamp(timelineScroll.x, 0f, maxScrollX);
        }

        private void HandleTimelineZoom(Event currentEvent, Rect viewportRect)
        {
            float viewportLocalX = currentEvent.mousePosition.x - viewportRect.x;
            float anchorBeat = Mathf.Max(0f, (timelineScroll.x + viewportLocalX) / pixelsPerBeat);
            float zoomFactor = 1f - (currentEvent.delta.y * 0.03f);
            float nextPixelsPerBeat = Mathf.Clamp(pixelsPerBeat * zoomFactor, MinPixelsPerBeat, MaxPixelsPerBeat);

            if (Mathf.Approximately(nextPixelsPerBeat, pixelsPerBeat))
            {
                return;
            }

            pixelsPerBeat = nextPixelsPerBeat;
            timelineScroll.x = Mathf.Max(0f, (anchorBeat * pixelsPerBeat) - viewportLocalX);
            ClampTimelineScroll();
            Repaint();
        }

        private void ClampTimelineScroll()
        {
            float contentWidth = Mathf.Max(MinTimelineWidth, GetTotalBeats() * pixelsPerBeat + 80f);
            float maxScrollX = Mathf.Max(0f, contentWidth - timelineViewportWidth);
            timelineScroll.x = Mathf.Clamp(timelineScroll.x, 0f, maxScrollX);
            timelineScroll.y = Mathf.Max(0f, timelineScroll.y);
        }

        private void BeginDraggedEvent(ChartEventData eventData, Vector2 localPosition)
        {
            if (eventData == null)
            {
                return;
            }

            draggedEvent = eventData;
            draggedBeat = eventData.Beat;
            draggedLane = eventData.Lane;
            dragBeatPointerOffset = Mathf.Max(0f, localPosition.x / pixelsPerBeat) - eventData.Beat;
            isDraggingEvent = true;
        }

        private void UpdateDraggedEvent(Vector2 localPosition)
        {
            if (!isDraggingEvent || draggedEvent == null || currentChart == null)
            {
                return;
            }

            float rawBeat = Mathf.Max(0f, (localPosition.x / pixelsPerBeat) - dragBeatPointerOffset);
            draggedBeat = snapEnabled ? QuantizeBeat(rawBeat) : rawBeat;
            draggedLane = GetLaneFromPosition(localPosition.y);
            cursorBeat = draggedBeat;
            selectedLane = draggedLane;
            Repaint();
        }

        private void CompleteDraggedEvent()
        {
            if (!isDraggingEvent || draggedEvent == null || currentChart == null)
            {
                ClearDraggedEventState();
                return;
            }

            bool changed = !Mathf.Approximately(draggedEvent.Beat, draggedBeat) || draggedEvent.Lane != draggedLane;
            if (changed)
            {
                RecordChartChange("Drag Chart Event");
                draggedEvent.Beat = draggedBeat;
                draggedEvent.Lane = draggedLane;
                SortEvents();
                MarkChartDirty();
            }

            cursorBeat = draggedBeat;
            selectedLane = draggedLane;
            selectedEvent = draggedEvent;
            ClearDraggedEventState();
            Repaint();
        }

        private void ClearDraggedEventState()
        {
            isDraggingEvent = false;
            draggedEvent = null;
            draggedBeat = 0f;
            draggedLane = 0;
            dragBeatPointerOffset = 0f;
        }

        private int GetLaneFromPosition(float positionY)
        {
            if (positionY <= TimelineHeaderHeight)
            {
                return selectedLane;
            }

            int lane = Mathf.FloorToInt((positionY - TimelineHeaderHeight) / LaneHeight);
            return Mathf.Clamp(lane, 0, Mathf.Max(0, currentChart.Meta.LaneCount - 1));
        }

        private bool TryGetEventAtPosition(Vector2 localPosition, out ChartEventData hitEvent)
        {
            if (currentChart != null)
            {
                for (int i = currentChart.Events.Count - 1; i >= 0; i--)
                {
                    ChartEventData eventData = currentChart.Events[i];
                    if (eventData != null && GetEventRect(eventData).Contains(localPosition))
                    {
                        hitEvent = eventData;
                        return true;
                    }
                }
            }

            hitEvent = null;
            return false;
        }

        private ChartEventData FindEventInSlot(float beat, int lane)
        {
            if (currentChart == null)
            {
                return null;
            }

            int targetSlot = GetSlotIndex(beat);

            for (int i = 0; i < currentChart.Events.Count; i++)
            {
                ChartEventData eventData = currentChart.Events[i];
                if (eventData != null && eventData.Lane == lane && GetSlotIndex(eventData.Beat) == targetSlot)
                {
                    return eventData;
                }
            }

            return null;
        }

        private LaneBindingData GetLaneBindingForLane(int lane)
        {
            if (currentChart == null || currentChart.Meta == null)
            {
                return null;
            }

            List<LaneBindingData> laneBindings = currentChart.Meta.LaneBindings;
            if (laneBindings == null || lane < 0 || lane >= laneBindings.Count)
            {
                return null;
            }

            return laneBindings[lane];
        }

        private int GetSlotIndex(float beat)
        {
            int gridDivision = currentChart != null ? Mathf.Max(1, currentChart.Meta.GridDivision) : 1;
            double scaled = beat * gridDivision;
            return (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
        }

        private Rect GetEventRect(ChartEventData eventData)
        {
            return GetEventRect(eventData.Beat, eventData.Lane);
        }

        private Rect GetEventRect(float beat, int lane)
        {
            float x = beat * pixelsPerBeat - (EventBoxWidth * 0.5f);
            float y = TimelineHeaderHeight + lane * LaneHeight + 18f;
            return new Rect(x, y, EventBoxWidth, LaneHeight - 24f);
        }

        private float GetDisplayedBeat(ChartEventData eventData)
        {
            if (isDraggingEvent && ReferenceEquals(draggedEvent, eventData))
            {
                return draggedBeat;
            }

            return eventData.Beat;
        }

        private int GetDisplayedLane(ChartEventData eventData)
        {
            if (isDraggingEvent && ReferenceEquals(draggedEvent, eventData))
            {
                return draggedLane;
            }

            return eventData.Lane;
        }

        private static Color GetEventColor(ChartEventData eventData)
        {
            Color baseColor;
            switch (eventData.Kind)
            {
                case CombatEventKind.MeleeThreatCounterable:
                    baseColor = new Color(0.81f, 0.24f, 0.24f, 1f);
                    break;
                case CombatEventKind.MeleeThreatDodgeOnly:
                    baseColor = new Color(0.88f, 0.48f, 0.16f, 1f);
                    break;
                case CombatEventKind.PlayerDashAttack:
                    baseColor = new Color(0.2f, 0.66f, 0.3f, 1f);
                    break;
                case CombatEventKind.PlayerRangedAttack:
                    baseColor = new Color(0.17f, 0.6f, 0.78f, 1f);
                    break;
                case CombatEventKind.RangedThreatCounterable:
                    baseColor = new Color(0.56f, 0.39f, 0.82f, 1f);
                    break;
                case CombatEventKind.RangedThreatDodgeOnly:
                    baseColor = new Color(0.76f, 0.26f, 0.63f, 1f);
                    break;
                default:
                    baseColor = new Color(0.55f, 0.55f, 0.58f, 1f);
                    break;
            }

            if (eventData.Phase == CombatEventPhase.Ready)
            {
                return new Color(baseColor.r * 0.75f, baseColor.g * 0.75f, baseColor.b * 0.75f, baseColor.a);
            }

            return baseColor;
        }

        private static string GetEventShortLabel(ChartEventData eventData)
        {
            string prefix;
            switch (eventData.Kind)
            {
                case CombatEventKind.MeleeThreatCounterable:
                    prefix = "MC";
                    break;
                case CombatEventKind.MeleeThreatDodgeOnly:
                    prefix = "MD";
                    break;
                case CombatEventKind.PlayerDashAttack:
                    prefix = "PD";
                    break;
                case CombatEventKind.PlayerRangedAttack:
                    prefix = "PR";
                    break;
                case CombatEventKind.RangedThreatCounterable:
                    prefix = "RC";
                    break;
                case CombatEventKind.RangedThreatDodgeOnly:
                    prefix = "RD";
                    break;
                default:
                    return "N";
            }

            if (eventData.Phase == CombatEventPhase.Timing)
            {
                return $"{prefix}T";
            }

            switch (eventData.ReadyCueType)
            {
                case ReadyCueType.Move:
                    return $"{prefix}M";
                case ReadyCueType.Pose:
                    return $"{prefix}P";
                default:
                    return $"{prefix}R";
            }
        }

        private static void DrawOutline(Rect rect, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), color);
        }

        private void SortEvents()
        {
            currentChart.Events.Sort((left, right) =>
            {
                if (ReferenceEquals(left, right))
                {
                    return 0;
                }

                if (left == null)
                {
                    return 1;
                }

                if (right == null)
                {
                    return -1;
                }

                int beatCompare = left.Beat.CompareTo(right.Beat);
                if (beatCompare != 0)
                {
                    return beatCompare;
                }

                int laneCompare = left.Lane.CompareTo(right.Lane);
                if (laneCompare != 0)
                {
                    return laneCompare;
                }

                int kindCompare = left.Kind.CompareTo(right.Kind);
                if (kindCompare != 0)
                {
                    return kindCompare;
                }

                int phaseCompare = left.Phase.CompareTo(right.Phase);
                if (phaseCompare != 0)
                {
                    return phaseCompare;
                }

                return left.StepIndex.CompareTo(right.StepIndex);
            });
        }

        private void AddCreateMenuItems(GenericMenu menu)
        {
            menu.AddItem(new GUIContent("Add/None"), false, () => AddOrUpdateEventAtCursor(CombatEventKind.None));
            menu.AddItem(new GUIContent("Add/Melee Counterable"), false, () => AddOrUpdateEventAtCursor(CombatEventKind.MeleeThreatCounterable));
            menu.AddItem(new GUIContent("Add/Melee Dodge"), false, () => AddOrUpdateEventAtCursor(CombatEventKind.MeleeThreatDodgeOnly));
            menu.AddItem(new GUIContent("Add/Player Dash"), false, () => AddOrUpdateEventAtCursor(CombatEventKind.PlayerDashAttack));
            menu.AddItem(new GUIContent("Add/Player Ranged"), false, () => AddOrUpdateEventAtCursor(CombatEventKind.PlayerRangedAttack));
            menu.AddItem(new GUIContent("Add/Ranged Counterable"), false, () => AddOrUpdateEventAtCursor(CombatEventKind.RangedThreatCounterable));
            menu.AddItem(new GUIContent("Add/Ranged Dodge"), false, () => AddOrUpdateEventAtCursor(CombatEventKind.RangedThreatDodgeOnly));
        }

        private void AddConvertMenuItems(GenericMenu menu)
        {
            menu.AddItem(new GUIContent("Convert/None"), false, () => ConvertSelectedEvent(CombatEventKind.None));
            menu.AddItem(new GUIContent("Convert/Melee Counterable"), false, () => ConvertSelectedEvent(CombatEventKind.MeleeThreatCounterable));
            menu.AddItem(new GUIContent("Convert/Melee Dodge"), false, () => ConvertSelectedEvent(CombatEventKind.MeleeThreatDodgeOnly));
            menu.AddItem(new GUIContent("Convert/Player Dash"), false, () => ConvertSelectedEvent(CombatEventKind.PlayerDashAttack));
            menu.AddItem(new GUIContent("Convert/Player Ranged"), false, () =>
            {
                if (selectedEvent != null && IsRangedPhraseKind(selectedEvent.Kind))
                {
                    ConvertSelectedRangedCueToPlayerRangedAttack();
                    return;
                }

                ConvertSelectedEvent(CombatEventKind.PlayerRangedAttack);
            });
            menu.AddItem(new GUIContent("Convert/Ranged Counterable"), false, () => ConvertSelectedEvent(CombatEventKind.RangedThreatCounterable));
            menu.AddItem(new GUIContent("Convert/Ranged Dodge"), false, () => ConvertSelectedEvent(CombatEventKind.RangedThreatDodgeOnly));
        }

        private static MonsterDefinition[] LoadMonsterDefinitions()
        {
            string[] guids = AssetDatabase.FindAssets("t:MonsterDefinition");
            List<MonsterDefinition> monsterDefinitions = new List<MonsterDefinition>(guids.Length);

            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                MonsterDefinition monsterDefinition = AssetDatabase.LoadAssetAtPath<MonsterDefinition>(assetPath);
                if (monsterDefinition != null)
                {
                    monsterDefinitions.Add(monsterDefinition);
                }
            }

            monsterDefinitions.Sort((left, right) =>
                string.Compare(GetMonsterDisplayLabel(left), GetMonsterDisplayLabel(right), StringComparison.OrdinalIgnoreCase));

            return monsterDefinitions.ToArray();
        }

        private static PatternDefinition[] GetSupportedPatterns(MonsterDefinition monsterDefinition)
        {
            if (monsterDefinition == null || monsterDefinition.SupportedPatterns == null || monsterDefinition.SupportedPatterns.Count == 0)
            {
                return Array.Empty<PatternDefinition>();
            }

            List<PatternDefinition> supportedPatterns = new List<PatternDefinition>(monsterDefinition.SupportedPatterns.Count);
            for (int i = 0; i < monsterDefinition.SupportedPatterns.Count; i++)
            {
                PatternDefinition patternDefinition = monsterDefinition.SupportedPatterns[i];
                if (patternDefinition != null && monsterDefinition.SupportsPattern(patternDefinition))
                {
                    supportedPatterns.Add(patternDefinition);
                }
            }

            supportedPatterns.Sort((left, right) =>
                string.Compare(GetPatternDisplayLabel(left), GetPatternDisplayLabel(right), StringComparison.OrdinalIgnoreCase));

            return supportedPatterns.ToArray();
        }

        private static PatternDefinition[] GetSupportedPatterns(MonsterDefinition monsterDefinition, CombatEventKind combatEventKind)
        {
            PatternDefinition[] supportedPatterns = GetSupportedPatterns(monsterDefinition);
            if (supportedPatterns.Length == 0)
            {
                return supportedPatterns;
            }

            List<PatternDefinition> filteredPatterns = new List<PatternDefinition>(supportedPatterns.Length);
            for (int i = 0; i < supportedPatterns.Length; i++)
            {
                PatternDefinition patternDefinition = supportedPatterns[i];
                if (patternDefinition != null && patternDefinition.CombatKind == combatEventKind)
                {
                    filteredPatterns.Add(patternDefinition);
                }
            }

            return filteredPatterns.ToArray();
        }

        private static string[] BuildLaneBindingPopupOptions(MonsterDefinition[] monsterDefinitions)
        {
            string[] options = new string[monsterDefinitions.Length + 1];
            options[0] = "None";

            for (int i = 0; i < monsterDefinitions.Length; i++)
            {
                options[i + 1] = GetMonsterDisplayLabel(monsterDefinitions[i]);
            }

            return options;
        }

        private static string[] BuildPatternPopupOptions(PatternDefinition[] patternDefinitions)
        {
            string[] options = new string[patternDefinitions.Length];

            for (int i = 0; i < patternDefinitions.Length; i++)
            {
                options[i] = GetPatternDisplayLabel(patternDefinitions[i]);
            }

            return options;
        }

        private static int GetLaneBindingSelectionIndex(LaneBindingData binding, MonsterDefinition[] monsterDefinitions)
        {
            if (binding == null || binding.BindingType != LaneBindingType.Monster || string.IsNullOrWhiteSpace(binding.BindingId))
            {
                return 0;
            }

            for (int i = 0; i < monsterDefinitions.Length; i++)
            {
                if (string.Equals(monsterDefinitions[i].MonsterId, binding.BindingId, StringComparison.Ordinal))
                {
                    return i + 1;
                }
            }

            return 0;
        }

        private static void ApplyLaneBindingSelection(LaneBindingData binding, int selectionIndex, MonsterDefinition[] monsterDefinitions)
        {
            if (binding == null)
            {
                return;
            }

            if (selectionIndex <= 0 || selectionIndex > monsterDefinitions.Length)
            {
                binding.BindingType = LaneBindingType.None;
                binding.BindingId = string.Empty;
                return;
            }

            MonsterDefinition selectedMonster = monsterDefinitions[selectionIndex - 1];
            binding.BindingType = LaneBindingType.Monster;
            binding.BindingId = selectedMonster != null ? selectedMonster.MonsterId : string.Empty;
        }

        private static string GetMonsterDisplayLabel(MonsterDefinition monsterDefinition)
        {
            if (monsterDefinition == null)
            {
                return "Missing Monster";
            }

            if (!string.IsNullOrWhiteSpace(monsterDefinition.DisplayName))
            {
                return monsterDefinition.DisplayName;
            }

            if (!string.IsNullOrWhiteSpace(monsterDefinition.MonsterId))
            {
                return monsterDefinition.MonsterId;
            }

            return monsterDefinition.name;
        }

        private static string GetPatternDisplayLabel(PatternDefinition patternDefinition)
        {
            if (patternDefinition == null)
            {
                return "Missing Pattern";
            }

            if (!string.IsNullOrWhiteSpace(patternDefinition.DisplayName))
            {
                return patternDefinition.DisplayName;
            }

            if (!string.IsNullOrWhiteSpace(patternDefinition.PatternId))
            {
                return patternDefinition.PatternId;
            }

            return patternDefinition.name;
        }

        private MonsterDefinition FindMonsterDefinitionById(string monsterId)
        {
            if (string.IsNullOrWhiteSpace(monsterId))
            {
                return null;
            }

            MonsterDefinition[] monsterDefinitions = LoadMonsterDefinitions();
            for (int i = 0; i < monsterDefinitions.Length; i++)
            {
                MonsterDefinition monsterDefinition = monsterDefinitions[i];
                if (monsterDefinition != null && string.Equals(monsterDefinition.MonsterId, monsterId, StringComparison.Ordinal))
                {
                    return monsterDefinition;
                }
            }

            return null;
        }

        private MonsterDefinition GetMonsterDefinitionForLane(int lane)
        {
            if (currentChart == null || currentChart.Meta == null)
            {
                return null;
            }

            List<LaneBindingData> laneBindings = currentChart.Meta.LaneBindings;
            if (laneBindings == null || lane < 0 || lane >= laneBindings.Count)
            {
                return null;
            }

            LaneBindingData binding = laneBindings[lane];
            if (binding == null || binding.BindingType != LaneBindingType.Monster || string.IsNullOrWhiteSpace(binding.BindingId))
            {
                return null;
            }

            return FindMonsterDefinitionById(binding.BindingId);
        }

        private static ReadyCueType[] GetAllowedReadyCueTypes(MonsterDefinition monsterDefinition)
        {
            if (monsterDefinition != null && !monsterDefinition.SupportsReadyMove)
            {
                return new[] { ReadyCueType.None, ReadyCueType.Pose };
            }

            return new[] { ReadyCueType.None, ReadyCueType.Pose, ReadyCueType.Move };
        }

        private static CombatEventKind[] GetAllowedCombatEventKinds(MonsterDefinition monsterDefinition)
        {
            if (monsterDefinition == null || monsterDefinition.SupportedPatterns == null || monsterDefinition.SupportedPatterns.Count == 0)
            {
                return new[] { CombatEventKind.None };
            }

            List<CombatEventKind> combatEventKinds = new List<CombatEventKind> { CombatEventKind.None };
            bool supportsRangedPhrase = false;
            for (int i = 0; i < monsterDefinition.SupportedPatterns.Count; i++)
            {
                PatternDefinition patternDefinition = monsterDefinition.SupportedPatterns[i];
                if (patternDefinition == null || !monsterDefinition.SupportsPattern(patternDefinition))
                {
                    continue;
                }

                if (!combatEventKinds.Contains(patternDefinition.CombatKind))
                {
                    combatEventKinds.Add(patternDefinition.CombatKind);
                }

                if (IsRangedThreatPatternKind(patternDefinition.CombatKind))
                {
                    supportsRangedPhrase = true;
                }
            }

            if (supportsRangedPhrase && !combatEventKinds.Contains(CombatEventKind.PlayerRangedAttack))
            {
                combatEventKinds.Add(CombatEventKind.PlayerRangedAttack);
            }

            return combatEventKinds.ToArray();
        }

        private static string[] BuildCombatEventKindLabels(CombatEventKind[] combatEventKinds)
        {
            string[] labels = new string[combatEventKinds.Length];
            for (int i = 0; i < combatEventKinds.Length; i++)
            {
                labels[i] = combatEventKinds[i].ToString();
            }

            return labels;
        }

        private static int FindCombatEventKindIndex(CombatEventKind[] combatEventKinds, CombatEventKind selectedCombatEventKind)
        {
            for (int i = 0; i < combatEventKinds.Length; i++)
            {
                if (combatEventKinds[i] == selectedCombatEventKind)
                {
                    return i;
                }
            }

            return 0;
        }

        private static bool ContainsCombatEventKind(CombatEventKind[] combatEventKinds, CombatEventKind targetCombatEventKind)
        {
            for (int i = 0; i < combatEventKinds.Length; i++)
            {
                if (combatEventKinds[i] == targetCombatEventKind)
                {
                    return true;
                }
            }

            return false;
        }

        private static string[] BuildReadyCueLabels(ReadyCueType[] readyCueTypes)
        {
            string[] labels = new string[readyCueTypes.Length];
            for (int i = 0; i < readyCueTypes.Length; i++)
            {
                labels[i] = readyCueTypes[i].ToString();
            }

            return labels;
        }

        private static int FindReadyCueIndex(ReadyCueType[] readyCueTypes, ReadyCueType selectedReadyCueType)
        {
            for (int i = 0; i < readyCueTypes.Length; i++)
            {
                if (readyCueTypes[i] == selectedReadyCueType)
                {
                    return i;
                }
            }

            return 0;
        }

        private static bool ShouldShowDirectionHint(
            CombatEventKind combatEventKind,
            CombatEventPhase combatEventPhase,
            ReadyCueType readyCueType)
        {
            if (combatEventKind == CombatEventKind.None)
            {
                return false;
            }

            return combatEventPhase == CombatEventPhase.Timing &&
                   (combatEventKind == CombatEventKind.MeleeThreatDodgeOnly ||
                    combatEventKind == CombatEventKind.RangedThreatDodgeOnly);
        }

        private static bool IsRangedPhraseKind(CombatEventKind combatEventKind)
        {
            return combatEventKind == CombatEventKind.RangedThreatDodgeOnly ||
                   combatEventKind == CombatEventKind.RangedThreatCounterable ||
                   combatEventKind == CombatEventKind.PlayerRangedAttack;
        }

        private static bool IsRangedThreatPatternKind(CombatEventKind combatEventKind)
        {
            return combatEventKind == CombatEventKind.RangedThreatDodgeOnly ||
                   combatEventKind == CombatEventKind.RangedThreatCounterable;
        }

        private static string GetRangedReadyCueLabel(CombatEventKind combatEventKind)
        {
            switch (combatEventKind)
            {
                case CombatEventKind.RangedThreatCounterable:
                    return "Projectile Call";
                case CombatEventKind.PlayerRangedAttack:
                    return "Player Attack Call";
                default:
                    return "Magic Circle Call";
            }
        }

        private static string BuildPatternDescription(PatternDefinition patternDefinition)
        {
            if (patternDefinition == null)
            {
                return "No pattern steps are defined.";
            }

            string readyCueText = string.IsNullOrWhiteSpace(patternDefinition.ReadyCueEventPath)
                ? "Ready Cue: Missing"
                : $"Ready Cue: {patternDefinition.ReadyCueEventPath}";

            if (patternDefinition.CombatKind == CombatEventKind.RangedThreatDodgeOnly)
            {
                string autoMoveText = patternDefinition.AutoMovePlayerToResponseBand
                    ? "auto response band"
                    : "keep current band";
                return
                    $"{readyCueText}\n" +
                    $"Ranged Dodge [Cues {patternDefinition.RangedCueCount}, Interval {patternDefinition.RangedCueIntervalBeats:0.###} beat, Response +{patternDefinition.RangedResponseOffsetBeats:0.###} beat, Move {patternDefinition.RangedDodgeMoveUnits:0.###}u, Band {patternDefinition.RangedResponseBandUnits:0.###}u, {autoMoveText}]";
            }

            if (patternDefinition.CombatKind == CombatEventKind.RangedThreatCounterable)
            {
                string autoMoveText = patternDefinition.AutoMovePlayerToResponseBand
                    ? "auto response band"
                    : "keep current band";
                string prefabText = patternDefinition.ProjectilePrefab != null
                    ? patternDefinition.ProjectilePrefab.name
                    : "Missing Prefab";
                return
                    $"{readyCueText}\n" +
                    $"Ranged Counter [Cues {patternDefinition.RangedCueCount}, Interval {patternDefinition.RangedCueIntervalBeats:0.###} beat, Response +{patternDefinition.RangedResponseOffsetBeats:0.###} beat, Band {patternDefinition.RangedResponseBandUnits:0.###}u, {autoMoveText}, Projectile {prefabText}, Speed {patternDefinition.ProjectileTravelUnits:0.###}u / {patternDefinition.ProjectileTravelDurationSec:0.###}s]";
            }

            if (patternDefinition.Steps == null || patternDefinition.Steps.Count == 0)
            {
                return $"{readyCueText}\nNo pattern steps are defined.";
            }

            List<PatternStepDefinition> orderedSteps = new List<PatternStepDefinition>(patternDefinition.Steps.Count);
            for (int i = 0; i < patternDefinition.Steps.Count; i++)
            {
                PatternStepDefinition stepDefinition = patternDefinition.Steps[i];
                if (stepDefinition != null)
                {
                    orderedSteps.Add(stepDefinition);
                }
            }

            orderedSteps.Sort(ComparePatternSteps);
            List<string> stepTexts = new List<string>(orderedSteps.Count);

            for (int i = 0; i < orderedSteps.Count; i++)
            {
                PatternStepDefinition stepDefinition = orderedSteps[i];
                if (stepDefinition.Phase == CombatEventPhase.Timing)
                {
                    string timingText;
                    if (patternDefinition.CombatKind == CombatEventKind.MeleeThreatCounterable ||
                        patternDefinition.CombatKind == CombatEventKind.PlayerDashAttack)
                    {
                        timingText =
                            $"Timing [Approach Band {stepDefinition.TargetBandUnits:0.###}, P Knockback {stepDefinition.PlayerKnockbackUnits:0.###}, M Knockback {stepDefinition.MonsterKnockbackUnits:0.###}]";
                    }
                    else
                    {
                        timingText = stepDefinition.DirectionHint == PlayerRelativeDirection.None
                            ? "Timing"
                            : $"Timing ({stepDefinition.DirectionHint})";
                    }

                    stepTexts.Add(timingText);
                    continue;
                }

                string cueText = patternDefinition.CombatKind == CombatEventKind.PlayerDashAttack
                    ? "Ready Mark"
                    : stepDefinition.ReadyCueType == ReadyCueType.None
                    ? "Ready"
                    : stepDefinition.ReadyCueType.ToString();

                if (patternDefinition.CombatKind != CombatEventKind.PlayerDashAttack &&
                    stepDefinition.ReadyCueType == ReadyCueType.Move)
                {
                    string moveDirectionText = stepDefinition.MovementDirectionHint == MovementDirectionHint.None
                        ? "None"
                        : stepDefinition.MovementDirectionHint.ToString();
                    cueText = $"{cueText} [{stepDefinition.MovementResolveMode}, {moveDirectionText}, {stepDefinition.MoveUnits:0.###}u -> Band {stepDefinition.TargetBandUnits:0.###}]";
                }
                else if (stepDefinition.DirectionHint != PlayerRelativeDirection.None)
                {
                    cueText = $"{cueText} ({stepDefinition.DirectionHint})";
                }

                stepTexts.Add(cueText);
            }

            return $"{readyCueText}\n{string.Join(" -> ", stepTexts)}";
        }

        private static bool TryBuildPatternSteps(
            PatternDefinition patternDefinition,
            out List<PatternStepDefinition> orderedSteps,
            out int timingStepIndex,
            out string errorMessage)
        {
            orderedSteps = new List<PatternStepDefinition>();
            timingStepIndex = -1;
            errorMessage = string.Empty;

            if (patternDefinition == null)
            {
                errorMessage = "Pattern definition is missing.";
                return false;
            }

            if (patternDefinition.Steps == null || patternDefinition.Steps.Count == 0)
            {
                errorMessage = "Pattern definition has no steps.";
                return false;
            }

            for (int i = 0; i < patternDefinition.Steps.Count; i++)
            {
                PatternStepDefinition stepDefinition = patternDefinition.Steps[i];
                if (stepDefinition != null)
                {
                    orderedSteps.Add(stepDefinition);
                }
            }

            if (orderedSteps.Count == 0)
            {
                errorMessage = "Pattern definition has no valid steps.";
                return false;
            }

            orderedSteps.Sort(ComparePatternSteps);

            for (int i = 0; i < orderedSteps.Count; i++)
            {
                PatternStepDefinition stepDefinition = orderedSteps[i];
                if (stepDefinition.BeatOffsetFromTiming > 0f)
                {
                    errorMessage = "Pattern steps cannot be placed after the final Timing step.";
                    return false;
                }

                if (stepDefinition.Phase != CombatEventPhase.Timing)
                {
                    continue;
                }

                if (Mathf.Approximately(stepDefinition.BeatOffsetFromTiming, 0f))
                {
                    if (timingStepIndex >= 0)
                    {
                        errorMessage = "Pattern definition can only contain one final Timing step at beat offset 0.";
                        return false;
                    }

                    timingStepIndex = i;
                }
            }

            if (timingStepIndex < 0)
            {
                errorMessage = "Pattern definition must contain one final Timing step at beat offset 0.";
                return false;
            }

            for (int i = 0; i < orderedSteps.Count; i++)
            {
                PatternStepDefinition stepDefinition = orderedSteps[i];
                if (i != timingStepIndex && stepDefinition.BeatOffsetFromTiming >= 0f)
                {
                    errorMessage = "Only the final Timing step can use beat offset 0. Other steps must be placed before it.";
                    return false;
                }

                if (stepDefinition.Phase == CombatEventPhase.Timing &&
                    (patternDefinition.CombatKind == CombatEventKind.MeleeThreatCounterable ||
                     patternDefinition.CombatKind == CombatEventKind.PlayerDashAttack) &&
                    stepDefinition.TargetBandUnits < 0f)
                {
                    errorMessage = "Timing step must set TargetBandUnits greater than or equal to 0.";
                    return false;
                }

                if (patternDefinition.CombatKind == CombatEventKind.PlayerDashAttack &&
                    stepDefinition.Phase == CombatEventPhase.Ready &&
                    stepDefinition.ReadyCueType != ReadyCueType.None)
                {
                    errorMessage = "PlayerDashAttack Ready step is always interpreted as Ready Mark.";
                    return false;
                }

                if (stepDefinition.Phase != CombatEventPhase.Ready || stepDefinition.ReadyCueType != ReadyCueType.Move)
                {
                    continue;
                }

                if (stepDefinition.MovementResolveMode == MovementResolveMode.None)
                {
                    errorMessage = "Ready Move step must set MovementResolveMode.";
                    return false;
                }

                if (stepDefinition.MoveUnits <= 0f)
                {
                    errorMessage = "Ready Move step must set MoveUnits greater than 0.";
                    return false;
                }

                if (stepDefinition.TargetBandUnits < 0f)
                {
                    errorMessage = "Ready Move step must set TargetBandUnits greater than or equal to 0.";
                    return false;
                }

                if (stepDefinition.MovementResolveMode == MovementResolveMode.SideMoveOnBand &&
                    stepDefinition.MovementDirectionHint == MovementDirectionHint.None)
                {
                    errorMessage = "SideMoveOnBand step must set MovementDirectionHint.";
                    return false;
                }
            }

            return true;
        }

        private static int ComparePatternSteps(PatternStepDefinition left, PatternStepDefinition right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            int beatCompare = left.BeatOffsetFromTiming.CompareTo(right.BeatOffsetFromTiming);
            if (beatCompare != 0)
            {
                return beatCompare;
            }

            return left.Phase.CompareTo(right.Phase);
        }

        private void RecordChartChange(string undoLabel)
        {
            if (currentChart != null)
            {
                Undo.RecordObject(currentChart, undoLabel);
            }
        }

        private AppliedPatternMetadata BuildAppliedPatternMetadata(
            PatternDefinition patternDefinition,
            float timingBeat,
            int timingStepIndex,
            int stepCount)
        {
            string assetPath = AssetDatabase.GetAssetPath(patternDefinition);
            string assetGuid = string.IsNullOrWhiteSpace(assetPath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(assetPath);

            return new AppliedPatternMetadata(
                Guid.NewGuid().ToString("N"),
                assetGuid,
                patternDefinition != null ? patternDefinition.PatternId : string.Empty,
                patternDefinition != null ? patternDefinition.BuildSyncSignature() : string.Empty,
                timingBeat,
                timingStepIndex,
                stepCount);
        }

        private AppliedPatternMetadata BuildRangedAppliedPatternMetadata(
            PatternDefinition patternDefinition,
            string phraseId,
            float firstTimingBeat,
            int stepCount)
        {
            string assetPath = AssetDatabase.GetAssetPath(patternDefinition);
            string assetGuid = string.IsNullOrWhiteSpace(assetPath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(assetPath);

            return new AppliedPatternMetadata(
                string.IsNullOrWhiteSpace(phraseId) ? Guid.NewGuid().ToString("N") : phraseId,
                assetGuid,
                patternDefinition != null ? patternDefinition.PatternId : string.Empty,
                patternDefinition != null ? patternDefinition.BuildSyncSignature() : string.Empty,
                firstTimingBeat,
                Mathf.Max(0, stepCount / 2),
                stepCount);
        }

        private static void ApplyPatternStepToEvent(
            ChartEventData targetEvent,
            PatternDefinition patternDefinition,
            PatternStepDefinition stepDefinition,
            float targetBeat,
            int targetLane,
            int stepIndex,
            float preservedNudgeMs,
            AppliedPatternMetadata metadata)
        {
            targetEvent.Beat = targetBeat;
            targetEvent.Lane = targetLane;
            targetEvent.Kind = patternDefinition.CombatKind;
            targetEvent.Phase = stepDefinition.Phase;
            targetEvent.StepIndex = stepIndex;
            targetEvent.ReadyCueType = stepDefinition.Phase == CombatEventPhase.Ready ? stepDefinition.ReadyCueType : ReadyCueType.None;
            targetEvent.DirectionHint = stepDefinition.DirectionHint;
            targetEvent.MovementResolveMode = stepDefinition.Phase == CombatEventPhase.Ready ? stepDefinition.MovementResolveMode : MovementResolveMode.None;
            targetEvent.MoveUnits = stepDefinition.Phase == CombatEventPhase.Ready ? stepDefinition.MoveUnits : 0f;
            targetEvent.TargetBandUnits = stepDefinition.TargetBandUnits;
            targetEvent.MovementDirectionHint = stepDefinition.Phase == CombatEventPhase.Ready ? stepDefinition.MovementDirectionHint : MovementDirectionHint.None;
            targetEvent.EntryBandUnits = patternDefinition.EntryBandUnits;
            targetEvent.PlayerKnockbackUnits = stepDefinition.Phase == CombatEventPhase.Timing ? stepDefinition.PlayerKnockbackUnits : 0f;
            targetEvent.MonsterKnockbackUnits = stepDefinition.Phase == CombatEventPhase.Timing ? stepDefinition.MonsterKnockbackUnits : 0f;
            targetEvent.NudgeMs = preservedNudgeMs;
            ApplyAppliedPatternMetadata(targetEvent, metadata);
        }

        private static void ApplyAppliedPatternMetadata(ChartEventData targetEvent, AppliedPatternMetadata metadata)
        {
            targetEvent.AppliedPatternInstanceId = metadata.InstanceId;
            targetEvent.AppliedPatternAssetGuid = metadata.AssetGuid;
            targetEvent.AppliedPatternId = metadata.PatternId ?? string.Empty;
            targetEvent.AppliedPatternSignature = metadata.Signature ?? string.Empty;
            targetEvent.AppliedPatternTimingBeat = metadata.TimingBeat;
            targetEvent.AppliedPatternTimingStepIndex = metadata.TimingStepIndex;
            targetEvent.AppliedPatternStepCount = metadata.StepCount;
        }

        private bool TryAutoSyncAppliedPatterns(out int syncedPatternGroupCount, out string errorMessage)
        {
            syncedPatternGroupCount = 0;
            errorMessage = string.Empty;

            if (currentChart == null || currentChart.Events == null || currentChart.Events.Count == 0)
            {
                return true;
            }

            Dictionary<string, AppliedPatternSyncGroup> groupsByInstanceId = new Dictionary<string, AppliedPatternSyncGroup>(StringComparer.Ordinal);
            Dictionary<string, AppliedPatternSyncGroup> rangedGroupsByPhraseId = new Dictionary<string, AppliedPatternSyncGroup>(StringComparer.Ordinal);
            for (int i = 0; i < currentChart.Events.Count; i++)
            {
                ChartEventData eventData = currentChart.Events[i];
                if (eventData == null)
                {
                    continue;
                }

                if (IsRangedPhraseKind(eventData.Kind) &&
                    HasAppliedPatternLink(eventData) &&
                    !string.IsNullOrWhiteSpace(eventData.RangedPhraseId))
                {
                    string rangedGroupKey = BuildRangedAppliedPatternGroupKey(eventData);
                    if (!rangedGroupsByPhraseId.TryGetValue(rangedGroupKey, out AppliedPatternSyncGroup rangedGroup))
                    {
                        rangedGroup = new AppliedPatternSyncGroup(rangedGroupKey);
                        rangedGroupsByPhraseId.Add(rangedGroupKey, rangedGroup);
                    }

                    rangedGroup.Events.Add(eventData);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(eventData.AppliedPatternInstanceId))
                {
                    continue;
                }

                if (!groupsByInstanceId.TryGetValue(eventData.AppliedPatternInstanceId, out AppliedPatternSyncGroup group))
                {
                    group = new AppliedPatternSyncGroup(eventData.AppliedPatternInstanceId);
                    groupsByInstanceId.Add(eventData.AppliedPatternInstanceId, group);
                }

                group.Events.Add(eventData);
            }

            if (groupsByInstanceId.Count == 0 && rangedGroupsByPhraseId.Count == 0)
            {
                return true;
            }

            bool hasSyncedChanges = false;
            bool undoRecorded = false;

            foreach (AppliedPatternSyncGroup group in groupsByInstanceId.Values)
            {
                if (!TrySyncAppliedPatternGroup(group, ref undoRecorded, out bool groupChanged, out errorMessage))
                {
                    return false;
                }

                if (groupChanged)
                {
                    syncedPatternGroupCount++;
                    hasSyncedChanges = true;
                }
            }

            foreach (AppliedPatternSyncGroup group in rangedGroupsByPhraseId.Values)
            {
                if (!TrySyncRangedAppliedPatternGroup(group, ref undoRecorded, out bool groupChanged, out errorMessage))
                {
                    return false;
                }

                if (groupChanged)
                {
                    syncedPatternGroupCount++;
                    hasSyncedChanges = true;
                }
            }

            if (hasSyncedChanges)
            {
                SortEvents();
                MarkChartDirty();
            }

            return true;
        }

        private bool TrySyncAppliedPatternGroup(
            AppliedPatternSyncGroup group,
            ref bool undoRecorded,
            out bool groupChanged,
            out string errorMessage)
        {
            groupChanged = false;
            errorMessage = string.Empty;

            if (group == null || group.Events.Count == 0)
            {
                return true;
            }

            ChartEventData referenceEvent = group.Events[0];
            PatternDefinition patternDefinition = FindPatternDefinitionByLink(referenceEvent.AppliedPatternAssetGuid, referenceEvent.AppliedPatternId);
            if (patternDefinition == null)
            {
                errorMessage =
                    $"Linked pattern '{referenceEvent.AppliedPatternId}' for this chart event group could not be found. Reapply the pattern manually before saving.";
                return false;
            }

            string currentPatternSignature = patternDefinition.BuildSyncSignature();
            if (string.Equals(referenceEvent.AppliedPatternSignature, currentPatternSignature, StringComparison.Ordinal))
            {
                return true;
            }

            if (!TryBuildPatternSteps(patternDefinition, out List<PatternStepDefinition> orderedSteps, out int timingStepIndex, out string patternErrorMessage))
            {
                errorMessage = $"Linked pattern '{GetPatternDisplayLabel(patternDefinition)}' is invalid: {patternErrorMessage}";
                return false;
            }

            int storedStepCount = Mathf.Max(referenceEvent.AppliedPatternStepCount, 0);
            if (storedStepCount <= 0)
            {
                errorMessage =
                    $"Linked pattern '{GetPatternDisplayLabel(patternDefinition)}' has no stored step count metadata. Reapply the pattern manually once, then save again.";
                return false;
            }

            if (orderedSteps.Count != storedStepCount)
            {
                errorMessage =
                    $"Linked pattern '{GetPatternDisplayLabel(patternDefinition)}' changed step count from {storedStepCount} to {orderedSteps.Count}. Reapply or manually update the chart before saving.";
                return false;
            }

            Dictionary<int, ChartEventData> eventsByStepIndex = new Dictionary<int, ChartEventData>();
            for (int i = 0; i < group.Events.Count; i++)
            {
                ChartEventData eventData = group.Events[i];
                if (eventData == null)
                {
                    continue;
                }

                if (eventData.StepIndex < 0 || eventData.StepIndex >= storedStepCount)
                {
                    errorMessage =
                        $"Linked pattern '{GetPatternDisplayLabel(patternDefinition)}' has an out-of-range chart step index ({eventData.StepIndex}). Fix the chart manually before saving.";
                    return false;
                }

                if (eventsByStepIndex.ContainsKey(eventData.StepIndex))
                {
                    errorMessage =
                        $"Linked pattern '{GetPatternDisplayLabel(patternDefinition)}' has duplicate chart step indices. Fix the chart manually before saving.";
                    return false;
                }

                eventsByStepIndex.Add(eventData.StepIndex, eventData);
            }

            if (eventsByStepIndex.Count != storedStepCount)
            {
                errorMessage =
                    $"Linked pattern '{GetPatternDisplayLabel(patternDefinition)}' is missing one or more chart steps. Reapply or manually update the chart before saving.";
                return false;
            }

            if (referenceEvent.AppliedPatternTimingStepIndex < 0 || referenceEvent.AppliedPatternTimingStepIndex >= storedStepCount)
            {
                errorMessage =
                    $"Linked pattern '{GetPatternDisplayLabel(patternDefinition)}' has an invalid stored timing step index. Reapply the pattern manually before saving.";
                return false;
            }

            if (timingStepIndex != referenceEvent.AppliedPatternTimingStepIndex)
            {
                errorMessage =
                    $"Linked pattern '{GetPatternDisplayLabel(patternDefinition)}' changed which step is the Timing anchor. Reapply or manually update the chart before saving.";
                return false;
            }

            if (!eventsByStepIndex.TryGetValue(referenceEvent.AppliedPatternTimingStepIndex, out ChartEventData timingEvent))
            {
                errorMessage =
                    $"Linked pattern '{GetPatternDisplayLabel(patternDefinition)}' is missing its Timing step in the chart. Reapply or manually update the chart before saving.";
                return false;
            }

            float timingBeat = timingEvent.Beat;
            if (timingBeat < 0f)
            {
                errorMessage =
                    $"Linked pattern '{GetPatternDisplayLabel(patternDefinition)}' resolved to a negative timing beat. Fix the chart manually before saving.";
                return false;
            }

            if (!undoRecorded)
            {
                RecordChartChange("Auto Sync Applied Patterns");
                undoRecorded = true;
            }

            string assetPath = AssetDatabase.GetAssetPath(patternDefinition);
            string assetGuid = string.IsNullOrWhiteSpace(assetPath)
                ? referenceEvent.AppliedPatternAssetGuid
                : AssetDatabase.AssetPathToGUID(assetPath);
            AppliedPatternMetadata metadata = new AppliedPatternMetadata(
                group.InstanceId,
                assetGuid,
                patternDefinition.PatternId,
                currentPatternSignature,
                timingBeat,
                timingStepIndex,
                orderedSteps.Count);

            for (int i = 0; i < orderedSteps.Count; i++)
            {
                PatternStepDefinition stepDefinition = orderedSteps[i];
                ChartEventData targetEvent = eventsByStepIndex[i];
                float targetBeat = targetEvent.Beat;
                int targetLane = targetEvent.Lane;
                if (targetBeat < 0f)
                {
                    errorMessage =
                        $"Linked pattern '{GetPatternDisplayLabel(patternDefinition)}' has step {i} placed before beat 0 in the chart. Fix the chart manually before saving.";
                    return false;
                }

                ApplyPatternStepToEvent(
                    targetEvent,
                    patternDefinition,
                    stepDefinition,
                    targetBeat,
                    targetLane,
                    i,
                    targetEvent.NudgeMs,
                    metadata);
            }

            groupChanged = true;
            return true;
        }

        private bool TrySyncRangedAppliedPatternGroup(
            AppliedPatternSyncGroup group,
            ref bool undoRecorded,
            out bool groupChanged,
            out string errorMessage)
        {
            groupChanged = false;
            errorMessage = string.Empty;

            if (group == null || group.Events.Count == 0)
            {
                return true;
            }

            ChartEventData referenceEvent = group.Events[0];
            if (referenceEvent == null ||
                !IsRangedPhraseKind(referenceEvent.Kind) ||
                string.IsNullOrWhiteSpace(referenceEvent.RangedPhraseId))
            {
                return true;
            }

            PatternDefinition patternDefinition = FindPatternDefinitionByLink(
                referenceEvent.AppliedPatternAssetGuid,
                referenceEvent.AppliedPatternId);
            if (patternDefinition == null)
            {
                errorMessage =
                    $"Linked ranged pattern '{referenceEvent.AppliedPatternId}' for this phrase could not be found. Reapply the phrase manually before saving.";
                return false;
            }

            if (!IsRangedThreatPatternKind(patternDefinition.CombatKind))
            {
                errorMessage =
                    $"Linked ranged pattern '{GetPatternDisplayLabel(patternDefinition)}' is not a ranged threat pattern. Reapply the phrase manually before saving.";
                return false;
            }

            int cueCount = Mathf.Clamp(patternDefinition.RangedCueCount, 1, 32);
            int anchorCount = Mathf.Clamp(patternDefinition.RangedAnchorCount, 1, 32);
            int expectedStepCount = cueCount * 2;
            int lane = referenceEvent.Lane;
            string phraseId = referenceEvent.RangedPhraseId;
            Dictionary<int, ChartEventData> eventsByStepIndex = new Dictionary<int, ChartEventData>();

            for (int i = 0; i < group.Events.Count; i++)
            {
                ChartEventData eventData = group.Events[i];
                if (eventData == null)
                {
                    continue;
                }

                if (!IsAllowedRangedPhraseEventKind(eventData.Kind, patternDefinition.CombatKind) ||
                    eventData.Lane != lane ||
                    !string.Equals(eventData.RangedPhraseId, phraseId, StringComparison.Ordinal))
                {
                    errorMessage =
                        $"Linked ranged pattern '{GetPatternDisplayLabel(patternDefinition)}' has mixed phrase metadata. Reapply the phrase manually before saving.";
                    return false;
                }

                if (eventData.StepIndex < 0 || eventData.StepIndex >= expectedStepCount)
                {
                    errorMessage =
                        $"Linked ranged pattern '{GetPatternDisplayLabel(patternDefinition)}' changed cue count. Reapply the phrase manually before saving.";
                    return false;
                }

                if (eventsByStepIndex.ContainsKey(eventData.StepIndex))
                {
                    errorMessage =
                        $"Linked ranged pattern '{GetPatternDisplayLabel(patternDefinition)}' has duplicate chart step indices. Fix the chart manually before saving.";
                    return false;
                }

                eventsByStepIndex.Add(eventData.StepIndex, eventData);
            }

            if (eventsByStepIndex.Count != expectedStepCount)
            {
                errorMessage =
                    $"Linked ranged pattern '{GetPatternDisplayLabel(patternDefinition)}' changed cue count. Reapply the phrase manually before saving.";
                return false;
            }

            if (!eventsByStepIndex.TryGetValue(0, out ChartEventData firstCallEvent))
            {
                errorMessage =
                    $"Linked ranged pattern '{GetPatternDisplayLabel(patternDefinition)}' is missing its first call event. Reapply the phrase manually before saving.";
                return false;
            }

            float startBeat = firstCallEvent.Beat;
            float cueIntervalBeats = Mathf.Max(0.001f, patternDefinition.RangedCueIntervalBeats);
            float responseOffsetBeats = Mathf.Max(0.001f, patternDefinition.RangedResponseOffsetBeats);
            string currentPatternSignature = patternDefinition.BuildSyncSignature();
            string assetPath = AssetDatabase.GetAssetPath(patternDefinition);
            string assetGuid = string.IsNullOrWhiteSpace(assetPath)
                ? referenceEvent.AppliedPatternAssetGuid
                : AssetDatabase.AssetPathToGUID(assetPath);
            string instanceId = !string.IsNullOrWhiteSpace(referenceEvent.AppliedPatternInstanceId)
                ? referenceEvent.AppliedPatternInstanceId
                : phraseId;
            AppliedPatternMetadata metadata = new AppliedPatternMetadata(
                instanceId,
                assetGuid,
                patternDefinition.PatternId,
                currentPatternSignature,
                startBeat + responseOffsetBeats,
                cueCount,
                expectedStepCount);

            bool requiresSync = false;
            for (int i = 0; i < cueCount; i++)
            {
                if (!eventsByStepIndex.TryGetValue(i, out ChartEventData callEvent) ||
                    !eventsByStepIndex.TryGetValue(cueCount + i, out ChartEventData timingEvent))
                {
                    errorMessage =
                        $"Linked ranged pattern '{GetPatternDisplayLabel(patternDefinition)}' is missing one or more call/response events. Reapply the phrase manually before saving.";
                    return false;
                }

                if (callEvent.Kind != timingEvent.Kind)
                {
                    errorMessage =
                        $"Linked ranged pattern '{GetPatternDisplayLabel(patternDefinition)}' has a cue whose call/response kinds do not match. Convert both events together before saving.";
                    return false;
                }

                float callBeat = startBeat + (i * cueIntervalBeats);
                float responseBeat = callBeat + responseOffsetBeats;
                int anchorIndex = i % anchorCount;
                bool hasBlockingCall = TryFindBlockingForeignEvent(callBeat, lane, group.Events, out ChartEventData blockingCallEvent);
                bool hasBlockingResponse = TryFindBlockingForeignEvent(responseBeat, lane, group.Events, out ChartEventData blockingResponseEvent);
                if (hasBlockingCall || hasBlockingResponse)
                {
                    ChartEventData blockingEvent = blockingCallEvent != null ? blockingCallEvent : blockingResponseEvent;
                    errorMessage =
                        $"Linked ranged pattern '{GetPatternDisplayLabel(patternDefinition)}' would overlap another event near beat {blockingEvent.Beat:0.###}. Move that event or reapply the phrase manually.";
                    return false;
                }

                if (!Mathf.Approximately(callEvent.Beat, callBeat) ||
                    !Mathf.Approximately(timingEvent.Beat, responseBeat) ||
                    callEvent.RangedAnchorIndex != anchorIndex ||
                    timingEvent.RangedAnchorIndex != anchorIndex ||
                    callEvent.RangedCueIndex != i ||
                    timingEvent.RangedCueIndex != i ||
                    !Mathf.Approximately(callEvent.EntryBandUnits, patternDefinition.EntryBandUnits) ||
                    !Mathf.Approximately(timingEvent.EntryBandUnits, patternDefinition.EntryBandUnits) ||
                    !string.Equals(callEvent.AppliedPatternInstanceId, instanceId, StringComparison.Ordinal) ||
                    !string.Equals(timingEvent.AppliedPatternInstanceId, instanceId, StringComparison.Ordinal) ||
                    !string.Equals(callEvent.AppliedPatternSignature, currentPatternSignature, StringComparison.Ordinal) ||
                    !string.Equals(timingEvent.AppliedPatternSignature, currentPatternSignature, StringComparison.Ordinal))
                {
                    requiresSync = true;
                }
            }

            if (!requiresSync)
            {
                return true;
            }

            if (!undoRecorded)
            {
                RecordChartChange("Auto Sync Ranged Phrase");
                undoRecorded = true;
            }

            for (int i = 0; i < cueCount; i++)
            {
                ChartEventData callEvent = eventsByStepIndex[i];
                ChartEventData timingEvent = eventsByStepIndex[cueCount + i];
                float callBeat = startBeat + (i * cueIntervalBeats);
                float responseBeat = callBeat + responseOffsetBeats;
                int anchorIndex = i % anchorCount;
                CombatEventKind callKind = callEvent.Kind == CombatEventKind.PlayerRangedAttack
                    ? CombatEventKind.PlayerRangedAttack
                    : patternDefinition.CombatKind;
                CombatEventKind timingKind = timingEvent.Kind == CombatEventKind.PlayerRangedAttack
                    ? CombatEventKind.PlayerRangedAttack
                    : patternDefinition.CombatKind;

                ApplyRangedPhraseEvent(
                    callEvent,
                    callKind,
                    metadata,
                    phraseId,
                    i,
                    anchorIndex,
                    callBeat,
                    lane,
                    i,
                    CombatEventPhase.Ready,
                    PlayerRelativeDirection.None,
                    patternDefinition.EntryBandUnits,
                    callEvent.NudgeMs);

                PlayerRelativeDirection timingDirection = timingKind == CombatEventKind.RangedThreatDodgeOnly
                    ? timingEvent.DirectionHint
                    : PlayerRelativeDirection.None;
                ApplyRangedPhraseEvent(
                    timingEvent,
                    timingKind,
                    metadata,
                    phraseId,
                    i,
                    anchorIndex,
                    responseBeat,
                    lane,
                    cueCount + i,
                    CombatEventPhase.Timing,
                    timingDirection,
                    patternDefinition.EntryBandUnits,
                    timingEvent.NudgeMs);
            }

            groupChanged = true;
            return true;
        }

        private static bool HasAppliedPatternLink(ChartEventData eventData)
        {
            return eventData != null &&
                   (!string.IsNullOrWhiteSpace(eventData.AppliedPatternAssetGuid) ||
                    !string.IsNullOrWhiteSpace(eventData.AppliedPatternId));
        }

        private static string BuildRangedAppliedPatternGroupKey(ChartEventData eventData)
        {
            return $"{eventData.Lane}:{eventData.AppliedPatternAssetGuid}:{eventData.AppliedPatternId}:{eventData.RangedPhraseId}";
        }

        private static bool IsAllowedRangedPhraseEventKind(CombatEventKind eventKind, CombatEventKind phraseThreatKind)
        {
            return eventKind == phraseThreatKind ||
                   eventKind == CombatEventKind.PlayerRangedAttack;
        }

        private bool TryFindBlockingForeignEvent(
            float beat,
            int lane,
            List<ChartEventData> ignoredGroupEvents,
            out ChartEventData blockingEvent)
        {
            blockingEvent = null;
            if (currentChart == null || currentChart.Events == null)
            {
                return false;
            }

            int targetSlotIndex = GetSlotIndex(beat);
            for (int i = 0; i < currentChart.Events.Count; i++)
            {
                ChartEventData eventData = currentChart.Events[i];
                if (eventData == null || ignoredGroupEvents.Contains(eventData))
                {
                    continue;
                }

                if (eventData.Lane != lane || GetSlotIndex(eventData.Beat) != targetSlotIndex)
                {
                    continue;
                }

                if (eventData.Kind == CombatEventKind.None)
                {
                    continue;
                }

                blockingEvent = eventData;
                return true;
            }

            return false;
        }

        private ChartEventData FindReusableNoneMarker(float beat, int lane, List<ChartEventData> ignoredGroupEvents)
        {
            if (currentChart == null || currentChart.Events == null)
            {
                return null;
            }

            int targetSlotIndex = GetSlotIndex(beat);
            for (int i = 0; i < currentChart.Events.Count; i++)
            {
                ChartEventData eventData = currentChart.Events[i];
                if (eventData == null || ignoredGroupEvents.Contains(eventData))
                {
                    continue;
                }

                if (eventData.Lane == lane &&
                    GetSlotIndex(eventData.Beat) == targetSlotIndex &&
                    eventData.Kind == CombatEventKind.None)
                {
                    return eventData;
                }
            }

            return null;
        }

        private PatternDefinition FindPatternDefinitionByLink(string assetGuid, string patternId)
        {
            if (!string.IsNullOrWhiteSpace(assetGuid))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                if (!string.IsNullOrWhiteSpace(assetPath))
                {
                    PatternDefinition patternDefinition = AssetDatabase.LoadAssetAtPath<PatternDefinition>(assetPath);
                    if (patternDefinition != null)
                    {
                        return patternDefinition;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(patternId))
            {
                return null;
            }

            string[] guids = AssetDatabase.FindAssets("t:PatternDefinition");
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                PatternDefinition patternDefinition = AssetDatabase.LoadAssetAtPath<PatternDefinition>(assetPath);
                if (patternDefinition != null &&
                    string.Equals(patternDefinition.PatternId, patternId, StringComparison.Ordinal))
                {
                    return patternDefinition;
                }
            }

            return null;
        }

        private static void ClearAppliedPatternLink(ChartEventData eventData)
        {
            if (eventData == null)
            {
                return;
            }

            eventData.AppliedPatternInstanceId = string.Empty;
            eventData.AppliedPatternAssetGuid = string.Empty;
            eventData.AppliedPatternId = string.Empty;
            eventData.AppliedPatternSignature = string.Empty;
            eventData.AppliedPatternTimingBeat = 0f;
            eventData.AppliedPatternTimingStepIndex = -1;
            eventData.AppliedPatternStepCount = 0;
        }

        private static void ClearRangedMetadata(ChartEventData eventData)
        {
            if (eventData == null)
            {
                return;
            }

            eventData.RangedPhraseId = string.Empty;
            eventData.RangedCueIndex = -1;
            eventData.RangedAnchorIndex = -1;
        }

        private void MarkChartDirty()
        {
            if (currentChart != null)
            {
                currentChart.InvalidateResolvedTimeline("Chart source data changed.");
                EditorUtility.SetDirty(currentChart);
                lastValidation = ChartValidator.Validate(currentChart);
                Repaint();
            }
        }
    }
}
