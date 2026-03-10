using System;
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
        private float timelineViewportWidth;
        private float draggedBeat;
        private float dragBeatPointerOffset;
        private int selectedLane;
        private int draggedLane;
        private string metronomePreviewEventPath = string.Empty;
        private string eventPreviewEventPath = string.Empty;
        private bool metronomeEnabled = true;
        private bool eventPreviewEnabled = true;
        private bool snapEnabled = true;
        private bool isDraggingEvent;
        private bool previewPaused;
        private bool previewWasPlaying;
        private ChartEventData draggedEvent;

        private ChartValidationResult lastValidation;

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
            GUILayout.Label("Chart Meta", EditorStyles.boldLabel);

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

            GUILayout.Label("Event Palette", EditorStyles.boldLabel);
            GUILayout.Label("Space adds a None marker at the current cursor.", EditorStyles.wordWrappedLabel);
            GUILayout.Space(4f);

            DrawAddButton("Add None Marker", ChartEventType.None);
            DrawAddButton("Add AttackTiming", ChartEventType.AttackTiming);
            DrawAddButton("Add MonsterAction", ChartEventType.MonsterAction);
            DrawAddButton("Add CameraCue", ChartEventType.CameraCue);
            DrawAddButton("Add CameraMoving", ChartEventType.CameraMoving);

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

        private void DrawAddButton(string label, ChartEventType eventType)
        {
            if (GUILayout.Button(label, GUILayout.Height(26f)))
            {
                AddOrUpdateEventAtCursor(eventType);
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
                EditorGUI.DrawRect(eventRect, GetEventColor(eventData.EventType));

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
            ChartEventType nextType = (ChartEventType)EditorGUILayout.EnumPopup("Event Type", selectedEvent.EventType);

            MonsterActionKind nextMonsterActionKind = selectedEvent.MonsterActionKind;
            CameraCueKind nextCameraCueKind = selectedEvent.CameraCueKind;
            CameraMovingKind nextCameraMovingKind = selectedEvent.CameraMovingKind;

            if (nextType == ChartEventType.MonsterAction)
            {
                nextMonsterActionKind = (MonsterActionKind)EditorGUILayout.EnumPopup("Monster Action", selectedEvent.MonsterActionKind);
            }
            else if (nextType == ChartEventType.CameraCue)
            {
                nextCameraCueKind = (CameraCueKind)EditorGUILayout.EnumPopup("Camera Cue", selectedEvent.CameraCueKind);
            }
            else if (nextType == ChartEventType.CameraMoving)
            {
                nextCameraMovingKind = (CameraMovingKind)EditorGUILayout.EnumPopup("Camera Moving", selectedEvent.CameraMovingKind);
            }

            float nextNudgeMs = EditorGUILayout.FloatField("Nudge (ms)", selectedEvent.NudgeMs);

            if (EditorGUI.EndChangeCheck())
            {
                RecordChartChange("Edit Chart Event");
                selectedEvent.Beat = snapEnabled ? QuantizeBeat(nextBeat) : Mathf.Max(0f, nextBeat);
                selectedEvent.Lane = Mathf.Clamp(nextLane, 0, laneCount - 1);
                ApplyEventType(selectedEvent, nextType);
                selectedEvent.MonsterActionKind = nextMonsterActionKind;
                selectedEvent.CameraCueKind = nextCameraCueKind;
                selectedEvent.CameraMovingKind = nextCameraMovingKind;
                selectedEvent.NudgeMs = nextNudgeMs;
                cursorBeat = selectedEvent.Beat;
                selectedLane = selectedEvent.Lane;
                SortEvents();
                MarkChartDirty();
            }

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
                menu.AddItem(new GUIContent("Convert/None"), false, () => ConvertSelectedEvent(ChartEventType.None));
                menu.AddItem(new GUIContent("Convert/AttackTiming"), false, () => ConvertSelectedEvent(ChartEventType.AttackTiming));
                menu.AddItem(new GUIContent("Convert/MonsterAction"), false, () => ConvertSelectedEvent(ChartEventType.MonsterAction));
                menu.AddItem(new GUIContent("Convert/CameraCue"), false, () => ConvertSelectedEvent(ChartEventType.CameraCue));
                menu.AddItem(new GUIContent("Convert/CameraMoving"), false, () => ConvertSelectedEvent(ChartEventType.CameraMoving));
            }
            else
            {
                selectedEvent = null;
                cursorBeat = GetBeatFromPosition(localPosition.x);
                selectedLane = GetLaneFromPosition(localPosition.y);
                MovePreviewToCursorAndPause(wasPlaying);

                menu.AddItem(new GUIContent("Add/None"), false, () => AddOrUpdateEventAtCursor(ChartEventType.None));
                menu.AddItem(new GUIContent("Add/AttackTiming"), false, () => AddOrUpdateEventAtCursor(ChartEventType.AttackTiming));
                menu.AddItem(new GUIContent("Add/MonsterAction"), false, () => AddOrUpdateEventAtCursor(ChartEventType.MonsterAction));
                menu.AddItem(new GUIContent("Add/CameraCue"), false, () => AddOrUpdateEventAtCursor(ChartEventType.CameraCue));
                menu.AddItem(new GUIContent("Add/CameraMoving"), false, () => AddOrUpdateEventAtCursor(ChartEventType.CameraMoving));
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
                AddOrUpdateEventAtCursor(ChartEventType.None);
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

        private void AddOrUpdateEventAtCursor(ChartEventType eventType)
        {
            if (currentChart == null)
            {
                return;
            }

            float targetBeat = snapEnabled ? QuantizeBeat(cursorBeat) : cursorBeat;
            int targetLane = Mathf.Clamp(selectedLane, 0, Mathf.Max(0, currentChart.Meta.LaneCount - 1));

            RecordChartChange("Add Chart Event");

            ChartEventData eventData = FindEventInSlot(targetBeat, targetLane);
            if (eventData == null)
            {
                eventData = new ChartEventData
                {
                    Beat = targetBeat,
                    Lane = targetLane,
                    NudgeMs = 0f
                };
                currentChart.Events.Add(eventData);
            }

            ApplyEventType(eventData, eventType);
            selectedEvent = eventData;
            cursorBeat = eventData.Beat;
            selectedLane = eventData.Lane;

            SortEvents();
            MarkChartDirty();
        }

        private void ConvertSelectedEvent(ChartEventType eventType)
        {
            if (selectedEvent == null)
            {
                return;
            }

            RecordChartChange("Convert Chart Event");
            ApplyEventType(selectedEvent, eventType);
            SortEvents();
            MarkChartDirty();
        }

        private void ApplyEventType(ChartEventData eventData, ChartEventType eventType)
        {
            eventData.EventType = eventType;

            if (eventType != ChartEventType.MonsterAction)
            {
                eventData.MonsterActionKind = MonsterActionKind.None;
            }

            if (eventType != ChartEventType.CameraCue)
            {
                eventData.CameraCueKind = CameraCueKind.None;
            }

            if (eventType != ChartEventType.CameraMoving)
            {
                eventData.CameraMovingKind = CameraMovingKind.None;
            }
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

            ChartValidationResult validation = ChartValidator.Validate(currentChart);
            if (validation.HasError)
            {
                EditorUtility.DisplayDialog("Cannot Save Chart", "Chart has validation errors. Resolve errors before saving.", "OK");
                return;
            }

            EditorUtility.SetDirty(currentChart);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
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
            if (!fmodPreviewBackend.PlayMainEvent(currentChart.Meta.FmodEventPath, previewTimeSec))
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
                    fmodPreviewBackend.PlayOneShot(metronomePreviewEventPath);
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
                    fmodPreviewBackend.PlayOneShot(eventPreviewEventPath);
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

        private static Color GetEventColor(ChartEventType eventType)
        {
            switch (eventType)
            {
                case ChartEventType.AttackTiming:
                    return new Color(0.87f, 0.2f, 0.2f, 1f);
                case ChartEventType.MonsterAction:
                    return new Color(0.21f, 0.66f, 0.31f, 1f);
                case ChartEventType.CameraCue:
                    return new Color(0.15f, 0.55f, 0.9f, 1f);
                case ChartEventType.CameraMoving:
                    return new Color(0.72f, 0.46f, 0.14f, 1f);
                default:
                    return new Color(0.55f, 0.55f, 0.58f, 1f);
            }
        }

        private static string GetEventShortLabel(ChartEventData eventData)
        {
            switch (eventData.EventType)
            {
                case ChartEventType.AttackTiming:
                    return "AT";
                case ChartEventType.MonsterAction:
                    return "MA";
                case ChartEventType.CameraCue:
                    return "CQ";
                case ChartEventType.CameraMoving:
                    return "CM";
                default:
                    return "N";
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

                return left.EventType.CompareTo(right.EventType);
            });
        }

        private void RecordChartChange(string undoLabel)
        {
            if (currentChart != null)
            {
                Undo.RecordObject(currentChart, undoLabel);
            }
        }

        private void MarkChartDirty()
        {
            if (currentChart != null)
            {
                EditorUtility.SetDirty(currentChart);
                lastValidation = ChartValidator.Validate(currentChart);
                Repaint();
            }
        }
    }
}
