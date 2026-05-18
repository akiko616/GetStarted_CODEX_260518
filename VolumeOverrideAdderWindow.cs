using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Noweel.Editor
{
    /// <summary>
    /// 복수의 Volume Profile에 HDRP 볼륨 오버라이드를 일괄 추가하는 에디터 툴입니다.
    /// Volume Profile 에셋을 드래그앤드롭으로 등록하고 원하는 오버라이드를 선택한 뒤 일괄 추가 버튼을 누르세요.
    /// </summary>
    public class VolumeOverrideAdderWindow : EditorWindow
    {
        private readonly List<VolumeProfile> profiles = new List<VolumeProfile>();

        private bool addSSAO = true;
        private bool addSSGI = true;
        private bool addSSR = true;
        private bool addIndirectController = true;

        private Vector2 profileListScroll;
        private GUIStyle dropAreaStyle;
        private GUIStyle dropAreaDragStyle;
        private GUIStyle profileRowStyle;
        private bool stylesInitialized;

        [MenuItem("Tools/Volume Override 추가")]
        public static void ShowWindow()
        {
            var window = GetWindow<VolumeOverrideAdderWindow>("Volume Override 추가");
            window.minSize = new Vector2(380f, 460f);
        }

        private void OnGUI()
        {
            InitializeStyles();

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("HDRP Volume Override 일괄 추가 툴", EditorStyles.boldLabel);
            EditorGUILayout.Space(8f);

            DrawDropArea();
            EditorGUILayout.Space(6f);
            DrawProfileList();
            EditorGUILayout.Space(12f);
            DrawCheckboxes();
            EditorGUILayout.Space(12f);
            DrawApplyButton();
        }

        private void InitializeStyles()
        {
            if (stylesInitialized) return;

            dropAreaStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                wordWrap = true,
                normal = { textColor = new Color(0.5f, 0.5f, 0.5f) }
            };

            dropAreaDragStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                wordWrap = true,
                normal = { textColor = new Color(0.2f, 0.5f, 0.9f) }
            };

            profileRowStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(6, 6, 4, 4)
            };

            stylesInitialized = true;
        }

        private void DrawDropArea()
        {
            EditorGUILayout.LabelField("Volume Profile 등록", EditorStyles.miniBoldLabel);

            Rect dropRect = GUILayoutUtility.GetRect(0f, 52f, GUILayout.ExpandWidth(true));

            bool isDragging = dropRect.Contains(Event.current.mousePosition)
                && (Event.current.type == EventType.DragUpdated
                    || Event.current.type == EventType.DragPerform);

            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = isDragging
                ? new Color(0.55f, 0.75f, 0.95f)
                : new Color(0.82f, 0.82f, 0.82f);

            GUIStyle style = isDragging ? dropAreaDragStyle : dropAreaStyle;
            GUI.Box(dropRect, "Volume Profile을 여기에 드래그앤드롭\n(중복은 자동으로 무시됩니다)", style);
            GUI.backgroundColor = prevBg;

            HandleDragAndDrop(dropRect);
        }

        private void HandleDragAndDrop(Rect dropRect)
        {
            Event evt = Event.current;

            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
                return;
            if (!dropRect.Contains(evt.mousePosition))
                return;

            // 드래그 중인 오브젝트 중 VolumeProfile이 있는지 확인
            bool hasValidObject = false;
            for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
            {
                if (DragAndDrop.objectReferences[i] is VolumeProfile)
                {
                    hasValidObject = true;
                    break;
                }
            }

            DragAndDrop.visualMode = hasValidObject
                ? DragAndDropVisualMode.Copy
                : DragAndDropVisualMode.Rejected;

            if (evt.type == EventType.DragPerform && hasValidObject)
            {
                DragAndDrop.AcceptDrag();

                for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
                {
                    if (DragAndDrop.objectReferences[i] is VolumeProfile profile && !profiles.Contains(profile))
                        profiles.Add(profile);
                }

                GUI.FocusControl(null);
                Repaint();
            }

            evt.Use();
        }

        private void DrawProfileList()
        {
            // 헤더
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                $"등록된 프로파일 ({profiles.Count}개)",
                EditorStyles.miniBoldLabel);

            if (profiles.Count > 0 && GUILayout.Button("전체 제거", GUILayout.Width(70f)))
            {
                profiles.Clear();
                return;
            }
            EditorGUILayout.EndHorizontal();

            if (profiles.Count == 0)
            {
                EditorGUILayout.HelpBox("등록된 Volume Profile이 없습니다.", MessageType.None);
                return;
            }

            // 스크롤 리스트 (최대 6개 높이 고정, 초과 시 스크롤)
            float rowHeight = 26f;
            float listHeight = Mathf.Min(profiles.Count, 6) * rowHeight + 4f;

            profileListScroll = EditorGUILayout.BeginScrollView(
                profileListScroll,
                GUILayout.Height(listHeight));

            int removeIndex = -1;

            for (int i = 0; i < profiles.Count; i++)
            {
                EditorGUILayout.BeginHorizontal(profileRowStyle);

                // 인덱스 번호
                EditorGUILayout.LabelField($"{i + 1}.", GUILayout.Width(22f));

                // Object Field (읽기 전용 형태로 표시)
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField(profiles[i], typeof(VolumeProfile), false);
                EditorGUI.EndDisabledGroup();

                // 개별 제거 버튼
                if (GUILayout.Button("✕", GUILayout.Width(24f), GUILayout.Height(18f)))
                    removeIndex = i;

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            if (removeIndex >= 0)
                profiles.RemoveAt(removeIndex);
        }

        private void DrawCheckboxes()
        {
            EditorGUILayout.LabelField("추가할 오버라이드 선택", EditorStyles.miniBoldLabel);
            EditorGUILayout.Space(4f);

            addSSAO = EditorGUILayout.ToggleLeft(
                "스크린스페이스 앰비언트 오클루전  (Screen Space Ambient Occlusion)", addSSAO);
            addSSGI = EditorGUILayout.ToggleLeft(
                "스크린스페이스 글로벌 일루미네이션  (Global Illumination)", addSSGI);
            addSSR = EditorGUILayout.ToggleLeft(
                "스크린스페이스 리플렉션  (Screen Space Reflection)", addSSR);
            addIndirectController = EditorGUILayout.ToggleLeft(
                "인다이렉트 라이팅 컨트롤러  (Indirect Lighting Controller)", addIndirectController);
        }

        private void DrawApplyButton()
        {
            bool anyChecked = addSSAO || addSSGI || addSSR || addIndirectController;
            bool canApply = profiles.Count > 0 && anyChecked;

            EditorGUI.BeginDisabledGroup(!canApply);
            if (GUILayout.Button(
                $"전체 프로파일에 오버라이드 추가  ({profiles.Count}개 대상)",
                GUILayout.Height(36f)))
            {
                ApplyOverridesToAll();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(4f);

            if (profiles.Count == 0)
                EditorGUILayout.HelpBox("Volume Profile을 드래그하여 등록하세요.", MessageType.Info);
            else if (!anyChecked)
                EditorGUILayout.HelpBox("추가할 오버라이드를 하나 이상 선택하세요.", MessageType.Warning);
        }

        private void ApplyOverridesToAll()
        {
            if (profiles.Count == 0) return;

            int totalAdded = 0;
            int totalSkipped = 0;
            var resultLines = new List<string>();

            for (int i = 0; i < profiles.Count; i++)
            {
                VolumeProfile profile = profiles[i];
                if (!profile) continue;

                Undo.RecordObject(profile, "Volume Override 일괄 추가");

                var added = new List<string>();
                var skipped = new List<string>();

                TryAddOverride<ScreenSpaceAmbientOcclusion>(addSSAO, "SSAO", profile, added, skipped);
                TryAddOverride<GlobalIllumination>(addSSGI, "SSGI", profile, added, skipped);
                TryAddOverride<ScreenSpaceReflection>(addSSR, "SSR", profile, added, skipped);
                TryAddOverride<IndirectLightingController>(addIndirectController, "Indirect", profile, added, skipped);

                EditorUtility.SetDirty(profile);

                totalAdded += added.Count;
                totalSkipped += skipped.Count;

                string addedStr = added.Count > 0 ? $"+{string.Join(",", added)}" : "";
                string skippedStr = skipped.Count > 0 ? $"skip:{string.Join(",", skipped)}" : "";
                string detail = string.Join("  ", new[] { addedStr, skippedStr }).Trim();
                resultLines.Add($"• {profile.name}  [{detail}]");
            }

            AssetDatabase.SaveAssets();

            // 결과 다이얼로그
            string msg = $"대상 프로파일 {profiles.Count}개 처리 완료\n"
                + $"총 추가: {totalAdded}개 / 건너뜀: {totalSkipped}개\n\n"
                + string.Join("\n", resultLines);

            EditorUtility.DisplayDialog("일괄 추가 완료", msg, "확인");
            Debug.Log($"[VolumeOverrideAdder] 완료 — 프로파일 {profiles.Count}개, 추가 {totalAdded}, 건너뜀 {totalSkipped}");
        }

        private static void TryAddOverride<T>(
            bool shouldAdd,
            string displayName,
            VolumeProfile profile,
            List<string> added,
            List<string> skipped) where T : VolumeComponent
        {
            if (!shouldAdd) return;

            if (profile.Has<T>())
            {
                skipped.Add(displayName);
                return;
            }

            profile.Add<T>(false);
            added.Add(displayName);
        }
    }
}
