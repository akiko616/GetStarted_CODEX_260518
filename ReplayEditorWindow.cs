#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using ReplaySystem.Compression;
using ReplaySystem.Core;
using ReplaySystem.Serialization;
using UnityEditor;
using UnityEngine;

namespace ReplaySystem.Editor
{
    /// <summary>리플레이 에디터 윈도우 (파일 분석, 미리보기, 압축 변환)</summary>
    public class ReplayEditorWindow : EditorWindow
    {
        #region Constants

        private const string WINDOW_TITLE = "Replay Editor";
        private const string REPLAY_EXTENSION = "rpl";

        #endregion

        #region Fields

        // 현재 로드된 리플레이
        private ReplayData loadedReplay;
        private string loadedFilePath;
        private long loadedFileSize;
        private ReCompressionType loadedCompression;

        // UI 상태
        private Vector2 scrollPosition;
        private int selectedTab;
        private readonly string[] tabNames = { "File Info", "Objects", "Events", "Chunks", "Convert" };

        // 접기/펼치기 상태
        private bool showMetadata = true;
        private bool showInitialState = true;
        private bool showChunkDetails;
        private bool showEventList;

        // 변환 옵션
        private ReCompressionType targetCompression = ReCompressionType.LZ4;
        private string convertOutputPath;
        private bool isConverting;
        private float convertProgress;

        // 검색/필터
        private string objectSearchFilter = "";
        private string eventSearchFilter = "";
        private ReplayEventType? eventTypeFilter;

        // 페이징
        private int objectPageIndex;
        private int eventPageIndex;
        private int chunkPageIndex;
        private const int ITEMS_PER_PAGE = 50;

        // 취소 토큰
        private CancellationTokenSource cts;

        #endregion

        #region Menu Items

        [MenuItem("Tools/Replay System/Replay Editor %#r")]
        public static void ShowWindow()
        {
            var window = GetWindow<ReplayEditorWindow>(WINDOW_TITLE);
            window.minSize = new Vector2(600, 500);
            window.Show();
        }

        [MenuItem("Assets/Open in Replay Editor", true)]
        private static bool ValidateOpenInReplayEditor()
        {
            var selected = Selection.activeObject;

            if (selected == null)
            {
                return false;
            }

            string path = AssetDatabase.GetAssetPath(selected);
            return path.EndsWith($".{REPLAY_EXTENSION}", StringComparison.OrdinalIgnoreCase);
        }

        [MenuItem("Assets/Open in Replay Editor")]
        private static void OpenInReplayEditor()
        {
            var selected = Selection.activeObject;
            string path = AssetDatabase.GetAssetPath(selected);
            string fullPath = Path.GetFullPath(path);

            var window = GetWindow<ReplayEditorWindow>(WINDOW_TITLE);
            window.LoadReplayFile(fullPath);
            window.Show();
        }

        #endregion

        #region Unity Callbacks

        private void OnEnable()
        {
            cts = new CancellationTokenSource();
        }

        private void OnDisable()
        {
            cts?.Cancel();
            cts?.Dispose();
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawTabs();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            switch (selectedTab)
            {
                case 0:
                    DrawFileInfoTab();
                    break;
                case 1:
                    DrawObjectsTab();
                    break;
                case 2:
                    DrawEventsTab();
                    break;
                case 3:
                    DrawChunksTab();
                    break;
                case 4:
                    DrawConvertTab();
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        #endregion

        #region Toolbar

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Open...", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                OpenReplayFile();
            }

            if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                if (!string.IsNullOrEmpty(loadedFilePath))
                {
                    LoadReplayFile(loadedFilePath);
                }
            }

            GUILayout.FlexibleSpace();

            if (loadedReplay != null)
            {
                EditorGUILayout.LabelField($"Loaded: {Path.GetFileName(loadedFilePath)}", EditorStyles.toolbarButton);
            }
            else
            {
                EditorGUILayout.LabelField("No file loaded", EditorStyles.toolbarButton);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawTabs()
        {
            EditorGUILayout.BeginHorizontal();
            selectedTab = GUILayout.Toolbar(selectedTab, tabNames);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);
        }

        #endregion

        #region File Info Tab

        private void DrawFileInfoTab()
        {
            if (loadedReplay == null)
            {
                DrawNoFileMessage();
                return;
            }

            // 파일 정보
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("File Information", EditorStyles.boldLabel);

            DrawInfoRow("Path", loadedFilePath);
            DrawInfoRow("Size", FormatFileSize(loadedFileSize));
            DrawInfoRow("Compression", loadedCompression.ToString());

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // 메타데이터
            showMetadata = EditorGUILayout.Foldout(showMetadata, "Metadata", true, EditorStyles.foldoutHeader);

            if (showMetadata)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                var meta = loadedReplay.Metadata;

                DrawInfoRow("Recorded At", meta.RecordedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                DrawInfoRow("Duration", $"{meta.Duration:F2} seconds");
                DrawInfoRow("Frame Rate", $"{meta.FrameRate} fps");
                DrawInfoRow("Scene Name", meta.SceneName ?? "N/A");
                DrawInfoRow("Tracked Objects", meta.TrackedObjectCount.ToString());
                DrawInfoRow("Chunk Count", meta.ChunkCount.ToString());
                DrawInfoRow("Frames Per Chunk", meta.FramesPerChunk.ToString());
                DrawInfoRow("Keyframe Interval", meta.KeyframeInterval.ToString());
                DrawInfoRow("Has Physics", meta.HasPhysicsData.ToString());
                DrawInfoRow("Has Network", meta.HasNetworkData.ToString());

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(10);

            // 초기 상태 요약
            showInitialState = EditorGUILayout.Foldout(showInitialState, "Initial State Summary", true, EditorStyles.foldoutHeader);

            if (showInitialState && loadedReplay.InitialState != null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                var initial = loadedReplay.InitialState;

                DrawInfoRow("Object Count", initial.Objects?.Count.ToString() ?? "0");
                DrawInfoRow("Dynamic Prefabs", initial.DynamicObjectPrefabs?.Count.ToString() ?? "0");

                if (initial.DynamicObjectPrefabs?.Count > 0)
                {
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Registered Prefabs:", EditorStyles.miniLabel);

                    foreach (var prefab in initial.DynamicObjectPrefabs)
                    {
                        EditorGUILayout.LabelField($"  • {prefab.PrefabPath} (Hash: {prefab.PrefabHash})", EditorStyles.miniLabel);
                    }
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(10);

            // 통계 요약
            DrawStatisticsSummary();
        }

        private void DrawStatisticsSummary()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Statistics", EditorStyles.boldLabel);

            var meta = loadedReplay.Metadata;
            int totalFrames = meta.Duration * meta.FrameRate > 0
                ? Mathf.CeilToInt(meta.Duration * meta.FrameRate)
                : 0;

            DrawInfoRow("Total Frames (estimated)", totalFrames.ToString());
            DrawInfoRow("Total Events", loadedReplay.Events?.Count.ToString() ?? "0");

            float avgBytesPerSecond = meta.Duration > 0 ? loadedFileSize / meta.Duration : 0;
            DrawInfoRow("Avg Data Rate", $"{avgBytesPerSecond / 1024:F1} KB/s");

            if (meta.TrackedObjectCount > 0)
            {
                float bytesPerObjectPerSecond = avgBytesPerSecond / meta.TrackedObjectCount;
                DrawInfoRow("Per Object", $"{bytesPerObjectPerSecond:F1} bytes/obj/s");
            }

            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Objects Tab

        private void DrawObjectsTab()
        {
            if (loadedReplay?.InitialState?.Objects == null)
            {
                DrawNoFileMessage();
                return;
            }

            var objects = loadedReplay.InitialState.Objects;

            // 검색 필터
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Search:", GUILayout.Width(50));
            objectSearchFilter = EditorGUILayout.TextField(objectSearchFilter);

            if (GUILayout.Button("Clear", GUILayout.Width(50)))
            {
                objectSearchFilter = "";
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // 필터링된 오브젝트
            var filteredObjects = FilterObjects(objects);

            // 페이징
            int totalPages = Mathf.CeilToInt((float)filteredObjects.Count / ITEMS_PER_PAGE);
            objectPageIndex = Mathf.Clamp(objectPageIndex, 0, Mathf.Max(0, totalPages - 1));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Objects: {filteredObjects.Count} total", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            DrawPagination(ref objectPageIndex, totalPages);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // 헤더
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("ID", GUILayout.Width(60));
            EditorGUILayout.LabelField("Prefab Name", GUILayout.Width(150));
            EditorGUILayout.LabelField("Position", GUILayout.Width(180));
            EditorGUILayout.LabelField("RB", GUILayout.Width(30));
            EditorGUILayout.LabelField("Path");
            EditorGUILayout.EndHorizontal();

            // 아이템
            int startIndex = objectPageIndex * ITEMS_PER_PAGE;
            int endIndex = Mathf.Min(startIndex + ITEMS_PER_PAGE, filteredObjects.Count);

            for (int i = startIndex; i < endIndex; i++)
            {
                var obj = filteredObjects[i];
                DrawObjectRow(obj);
            }
        }

        private List<ObjectState> FilterObjects(List<ObjectState> objects)
        {
            if (string.IsNullOrEmpty(objectSearchFilter))
            {
                return objects;
            }

            var result = new List<ObjectState>();
            string filter = objectSearchFilter.ToLowerInvariant();

            foreach (var obj in objects)
            {
                if (obj.PrefabName?.ToLowerInvariant().Contains(filter) == true ||
                    obj.HierarchyPath?.ToLowerInvariant().Contains(filter) == true ||
                    obj.Id.ToString().Contains(filter))
                {
                    result.Add(obj);
                }
            }

            return result;
        }

        private void DrawObjectRow(ObjectState obj)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField(obj.Id.ToString(), GUILayout.Width(60));
            EditorGUILayout.LabelField(obj.PrefabName ?? "N/A", GUILayout.Width(150));

            var pos = obj.Transform.GetPosition();
            EditorGUILayout.LabelField($"({pos.x:F1}, {pos.y:F1}, {pos.z:F1})", GUILayout.Width(180));

            EditorGUILayout.LabelField(obj.HasRigidbody ? "✓" : "", GUILayout.Width(30));
            EditorGUILayout.LabelField(obj.HierarchyPath ?? "N/A", EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region Events Tab

        private void DrawEventsTab()
        {
            if (loadedReplay?.Events == null)
            {
                DrawNoFileMessage();
                return;
            }

            var events = loadedReplay.Events;

            // 검색 필터
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Search:", GUILayout.Width(50));
            eventSearchFilter = EditorGUILayout.TextField(eventSearchFilter, GUILayout.Width(200));

            EditorGUILayout.LabelField("Type:", GUILayout.Width(35));
            var typeOptions = new[] { "All", "MethodCall", "Instantiate", "Destroy", "Animation", "Audio", "Particle", "NetworkRpc", "NetworkVarChange", "OwnershipChange", "Custom" };
            int currentTypeIndex = eventTypeFilter.HasValue ? (int)eventTypeFilter.Value + 1 : 0;
            int newTypeIndex = EditorGUILayout.Popup(currentTypeIndex, typeOptions, GUILayout.Width(120));

            if (newTypeIndex == 0)
            {
                eventTypeFilter = null;
            }
            else
            {
                eventTypeFilter = (ReplayEventType)(newTypeIndex - 1);
            }

            if (GUILayout.Button("Clear", GUILayout.Width(50)))
            {
                eventSearchFilter = "";
                eventTypeFilter = null;
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // 필터링된 이벤트
            var filteredEvents = FilterEvents(events);

            // 페이징
            int totalPages = Mathf.CeilToInt((float)filteredEvents.Count / ITEMS_PER_PAGE);
            eventPageIndex = Mathf.Clamp(eventPageIndex, 0, Mathf.Max(0, totalPages - 1));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Events: {filteredEvents.Count} total", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            DrawPagination(ref eventPageIndex, totalPages);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // 헤더
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("Time", GUILayout.Width(70));
            EditorGUILayout.LabelField("Frame", GUILayout.Width(60));
            EditorGUILayout.LabelField("Type", GUILayout.Width(100));
            EditorGUILayout.LabelField("Target", GUILayout.Width(70));
            EditorGUILayout.LabelField("Name");
            EditorGUILayout.EndHorizontal();

            // 아이템
            int startIndex = eventPageIndex * ITEMS_PER_PAGE;
            int endIndex = Mathf.Min(startIndex + ITEMS_PER_PAGE, filteredEvents.Count);

            for (int i = startIndex; i < endIndex; i++)
            {
                var evt = filteredEvents[i];
                DrawEventRow(evt);
            }
        }

        private List<ReplayEvent> FilterEvents(List<ReplayEvent> events)
        {
            var result = new List<ReplayEvent>();
            string filter = eventSearchFilter?.ToLowerInvariant();

            foreach (var evt in events)
            {
                // 타입 필터
                if (eventTypeFilter.HasValue && evt.EventType != eventTypeFilter.Value)
                {
                    continue;
                }

                // 텍스트 필터
                if (!string.IsNullOrEmpty(filter))
                {
                    if (evt.EventName?.ToLowerInvariant().Contains(filter) != true &&
                        evt.TargetObjectId.ToString().Contains(filter) != true)
                    {
                        continue;
                    }
                }

                result.Add(evt);
            }

            return result;
        }

        private void DrawEventRow(ReplayEvent evt)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField($"{evt.Time:F2}s", GUILayout.Width(70));
            EditorGUILayout.LabelField(evt.FrameIndex.ToString(), GUILayout.Width(60));
            EditorGUILayout.LabelField(evt.EventType.ToString(), GUILayout.Width(100));
            EditorGUILayout.LabelField(evt.TargetObjectId.ToString(), GUILayout.Width(70));
            EditorGUILayout.LabelField(evt.EventName ?? "N/A", EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region Chunks Tab

        private void DrawChunksTab()
        {
            if (loadedReplay?.ChunkHeaders == null)
            {
                DrawNoFileMessage();
                return;
            }

            var headers = loadedReplay.ChunkHeaders;

            // 페이징
            int totalPages = Mathf.CeilToInt((float)headers.Count / ITEMS_PER_PAGE);
            chunkPageIndex = Mathf.Clamp(chunkPageIndex, 0, Mathf.Max(0, totalPages - 1));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Chunks: {headers.Count} total", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            DrawPagination(ref chunkPageIndex, totalPages);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // 헤더
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("Index", GUILayout.Width(50));
            EditorGUILayout.LabelField("Frames", GUILayout.Width(80));
            EditorGUILayout.LabelField("Time Range", GUILayout.Width(120));
            EditorGUILayout.LabelField("Offset", GUILayout.Width(100));
            EditorGUILayout.LabelField("Size", GUILayout.Width(80));
            EditorGUILayout.LabelField("Keyframe", GUILayout.Width(70));
            EditorGUILayout.EndHorizontal();

            // 아이템
            int startIndex = chunkPageIndex * ITEMS_PER_PAGE;
            int endIndex = Mathf.Min(startIndex + ITEMS_PER_PAGE, headers.Count);

            for (int i = startIndex; i < endIndex; i++)
            {
                var header = headers[i];
                DrawChunkRow(header);
            }

            EditorGUILayout.Space(10);

            // 청크 크기 분포 시각화
            DrawChunkSizeDistribution(headers);
        }

        private void DrawChunkRow(ReplayChunkHeader header)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField(header.Index.ToString(), GUILayout.Width(50));
            EditorGUILayout.LabelField($"{header.StartFrame}-{header.EndFrame}", GUILayout.Width(80));
            EditorGUILayout.LabelField($"{header.StartTime:F2}s - {header.EndTime:F2}s", GUILayout.Width(120));
            EditorGUILayout.LabelField($"0x{header.FileOffset:X}", GUILayout.Width(100));
            EditorGUILayout.LabelField(FormatFileSize(header.CompressedSize), GUILayout.Width(80));
            EditorGUILayout.LabelField(header.ContainsKeyframe ? "✓" : "", GUILayout.Width(70));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawChunkSizeDistribution(List<ReplayChunkHeader> headers)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Chunk Size Distribution", EditorStyles.boldLabel);

            if (headers.Count > 0)
            {
                int maxSize = 0;
                int minSize = int.MaxValue;
                long totalSize = 0;

                foreach (var h in headers)
                {
                    maxSize = Mathf.Max(maxSize, h.CompressedSize);
                    minSize = Mathf.Min(minSize, h.CompressedSize);
                    totalSize += h.CompressedSize;
                }

                float avgSize = (float)totalSize / headers.Count;

                DrawInfoRow("Min Size", FormatFileSize(minSize));
                DrawInfoRow("Max Size", FormatFileSize(maxSize));
                DrawInfoRow("Avg Size", FormatFileSize((long)avgSize));
                DrawInfoRow("Total Size", FormatFileSize(totalSize));

                EditorGUILayout.Space(5);

                // 간단한 바 차트
                Rect barRect = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));
                barRect.x += 10;
                barRect.width -= 20;

                if (Event.current.type == EventType.Repaint)
                {
                    float barWidth = barRect.width / Mathf.Min(headers.Count, 100);

                    for (int i = 0; i < Mathf.Min(headers.Count, 100); i++)
                    {
                        float normalizedHeight = (float)headers[i].CompressedSize / maxSize;
                        float barHeight = normalizedHeight * barRect.height;

                        Rect bar = new Rect(
                            barRect.x + i * barWidth,
                            barRect.y + barRect.height - barHeight,
                            barWidth - 1,
                            barHeight);

                        EditorGUI.DrawRect(bar, headers[i].ContainsKeyframe ? Color.green : Color.cyan);
                    }
                }
            }

            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Convert Tab

        private void DrawConvertTab()
        {
            if (loadedReplay == null)
            {
                DrawNoFileMessage();
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Compression Conversion", EditorStyles.boldLabel);

            EditorGUILayout.Space(10);

            // 현재 압축 정보
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Current Compression:", GUILayout.Width(140));
            EditorGUILayout.LabelField(loadedCompression.ToString(), EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Current Size:", GUILayout.Width(140));
            EditorGUILayout.LabelField(FormatFileSize(loadedFileSize), EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // 변환 옵션
            EditorGUILayout.LabelField("Convert To:", EditorStyles.miniBoldLabel);

            EditorGUILayout.BeginHorizontal();

            bool wasLZ4 = targetCompression == ReCompressionType.LZ4;
            bool wasGZip = targetCompression == ReCompressionType.GZip;
            bool wasNone = targetCompression == ReCompressionType.None;

            if (GUILayout.Toggle(wasNone, "None", EditorStyles.radioButton))
            {
                targetCompression = ReCompressionType.None;
            }
            if (GUILayout.Toggle(wasGZip, "GZip", EditorStyles.radioButton))
            {
                targetCompression = ReCompressionType.GZip;
            }
            if (GUILayout.Toggle(wasLZ4, "LZ4", EditorStyles.radioButton))
            {
                targetCompression = ReCompressionType.LZ4;
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // 출력 경로
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Output Path:", GUILayout.Width(80));
            convertOutputPath = EditorGUILayout.TextField(convertOutputPath);

            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                string dir = string.IsNullOrEmpty(convertOutputPath)
                    ? Path.GetDirectoryName(loadedFilePath)
                    : Path.GetDirectoryName(convertOutputPath);

                string defaultName = $"{Path.GetFileNameWithoutExtension(loadedFilePath)}_{targetCompression}.{REPLAY_EXTENSION}";
                string path = EditorUtility.SaveFilePanel("Save Converted Replay", dir, defaultName, REPLAY_EXTENSION);

                if (!string.IsNullOrEmpty(path))
                {
                    convertOutputPath = path;
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // 변환 버튼
            EditorGUI.BeginDisabledGroup(isConverting || string.IsNullOrEmpty(convertOutputPath));

            if (GUILayout.Button(isConverting ? "Converting..." : "Convert", GUILayout.Height(30)))
            {
                ConvertReplayAsync().Forget();
            }

            EditorGUI.EndDisabledGroup();

            // 진행률
            if (isConverting)
            {
                EditorGUILayout.Space(5);
                EditorGUI.ProgressBar(GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true)), convertProgress, $"Converting... {convertProgress * 100:F0}%");
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // 압축 비교 정보
            DrawCompressionComparison();
        }

        private void DrawCompressionComparison()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Compression Comparison", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "• None: 압축 없음, 가장 빠른 로드/저장\n" +
                "• GZip: 높은 압축률, 느린 속도 (기본값)\n" +
                "• LZ4: 빠른 압축/해제, 중간 압축률\n\n" +
                "LZ4는 실시간 재생에 권장됩니다.",
                MessageType.Info);

            EditorGUILayout.EndVertical();
        }

        private async UniTaskVoid ConvertReplayAsync()
        {
            if (loadedReplay == null || string.IsNullOrEmpty(convertOutputPath))
            {
                return;
            }

            isConverting = true;
            convertProgress = 0f;

            try
            {
                convertProgress = 0.3f;
                Repaint();

                var result = await ReplaySerializer.SaveAsync(loadedReplay, convertOutputPath, targetCompression, cts.Token);

                convertProgress = 1f;
                Repaint();

                if (result.Success)
                {
                    EditorUtility.DisplayDialog("Conversion Complete",
                        $"Replay converted successfully!\n\nOutput: {convertOutputPath}\nSize: {FormatFileSize(result.FileSize)}\nCompression Ratio: {result.CompressionRatio:P1}",
                        "OK");

                    // 변환된 파일 로드 옵션
                    if (EditorUtility.DisplayDialog("Load Converted File?", "Do you want to load the converted file?", "Yes", "No"))
                    {
                        LoadReplayFile(convertOutputPath);
                    }
                }
                else
                {
                    EditorUtility.DisplayDialog("Conversion Failed", $"Failed to convert replay:\n{result.ErrorMessage}", "OK");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ReplayEditorWindow] Conversion error: {e.Message}");
                EditorUtility.DisplayDialog("Error", $"Conversion failed: {e.Message}", "OK");
            }
            finally
            {
                isConverting = false;
                Repaint();
            }
        }

        #endregion

        #region Helper Methods

        private void DrawNoFileMessage()
        {
            EditorGUILayout.BeginVertical();
            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginVertical(GUILayout.Width(300));
            EditorGUILayout.LabelField("No Replay File Loaded", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.Space(10);

            if (GUILayout.Button("Open Replay File...", GUILayout.Height(40)))
            {
                OpenReplayFile();
            }

            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
        }

        private void DrawInfoRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label + ":", GUILayout.Width(140));
            EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPagination(ref int pageIndex, int totalPages)
        {
            EditorGUI.BeginDisabledGroup(pageIndex <= 0);
            if (GUILayout.Button("◀", GUILayout.Width(30)))
            {
                pageIndex--;
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.LabelField($"{pageIndex + 1} / {Mathf.Max(1, totalPages)}", GUILayout.Width(60));

            EditorGUI.BeginDisabledGroup(pageIndex >= totalPages - 1);
            if (GUILayout.Button("▶", GUILayout.Width(30)))
            {
                pageIndex++;
            }
            EditorGUI.EndDisabledGroup();
        }

        private void OpenReplayFile()
        {
            string path = EditorUtility.OpenFilePanel("Open Replay File", Application.dataPath, REPLAY_EXTENSION);

            if (!string.IsNullOrEmpty(path))
            {
                LoadReplayFile(path);
            }
        }

        private void LoadReplayFile(string path)
        {
            LoadReplayFileAsync(path).Forget();
        }

        private async UniTaskVoid LoadReplayFileAsync(string path)
        {
            try
            {
                var fileInfo = new FileInfo(path);
                loadedFileSize = fileInfo.Length;

                var result = await ReplayDeserializer.LoadAsync(path, cts.Token);

                if (result.Success)
                {
                    loadedReplay = result.Data;
                    loadedFilePath = path;
                    loadedCompression = result.DetectedCompression;

                    // 초기 변환 경로 설정
                    convertOutputPath = Path.Combine(
                        Path.GetDirectoryName(path),
                        $"{Path.GetFileNameWithoutExtension(path)}_converted.{REPLAY_EXTENSION}");

                    objectPageIndex = 0;
                    eventPageIndex = 0;
                    chunkPageIndex = 0;

                    Debug.Log($"[ReplayEditorWindow] Loaded: {path}");
                }
                else
                {
                    EditorUtility.DisplayDialog("Load Failed", $"Failed to load replay:\n{result.ErrorMessage}", "OK");
                }

                Repaint();
            }
            catch (Exception e)
            {
                Debug.LogError($"[ReplayEditorWindow] Load error: {e.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to load replay: {e.Message}", "OK");
            }
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;

            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }

            return $"{size:F2} {sizes[order]}";
        }

        #endregion
    }
}
#endif
