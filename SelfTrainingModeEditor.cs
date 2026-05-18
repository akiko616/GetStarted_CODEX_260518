#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using SelfTraining;

namespace SelfTraining.Editor
{
    /// <summary>
    /// SelfTrainingMode 커스텀 에디터
    /// Play 모드에서 훈련 제어를 쉽게 할 수 있도록 지원
    /// </summary>
    [CustomEditor(typeof(SelfTrainingMode))]
    public class SelfTrainingModeEditor : UnityEditor.Editor
    {
        private SelfTrainingMode selfTraining;

        // 커스텀 설정
        private string customPlayerName = "테스트 훈련생";
        private string customSceneName = "TrainingScene";
        private int customScenarioType = 0;
        private bool showAdvancedSettings = false;

        private void OnEnable()
        {
            selfTraining = (SelfTrainingMode)target;
        }

        public override void OnInspectorGUI()
        {
            // 기본 인스펙터
            DrawDefaultInspector();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Self Training 제어", EditorStyles.boldLabel);

            // Play 모드에서만 제어 가능
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Play 모드에서 Self Training을 제어할 수 있습니다.", MessageType.Info);

                EditorGUILayout.Space(5);
                showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "훈련 설정 미리보기");
                if (showAdvancedSettings)
                {
                    EditorGUI.indentLevel++;
                    customPlayerName = EditorGUILayout.TextField("플레이어 이름", customPlayerName);
                    customSceneName = EditorGUILayout.TextField("씬 이름", customSceneName);
                    customScenarioType = EditorGUILayout.IntField("시나리오 타입", customScenarioType);
                    EditorGUI.indentLevel--;
                }
                return;
            }

            // 상태 표시
            DrawStatusSection();

            EditorGUILayout.Space(5);

            // 제어 버튼
            DrawControlButtons();

            EditorGUILayout.Space(5);

            // 커스텀 설정
            DrawCustomSettingsSection();

            // 현재 훈련 데이터 표시
            if (selfTraining.IsTrainingActive && selfTraining.CurrentTrainingData != null)
            {
                EditorGUILayout.Space(10);
                DrawTrainingDataSection();
            }

            // 변경사항 갱신
            if (GUI.changed)
            {
                EditorUtility.SetDirty(target);
            }
        }

        private void DrawStatusSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 상태 LED 스타일
            GUIStyle statusStyle = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Bold
            };

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("초기화:", GUILayout.Width(80));
            DrawStatusIndicator(selfTraining.IsInitialized, "완료", "미완료");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Host 상태:", GUILayout.Width(80));
            DrawStatusIndicator(selfTraining.IsHostRunning, "실행 중", "중지됨");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("훈련 상태:", GUILayout.Width(80));
            DrawStatusIndicator(selfTraining.IsTrainingActive, "진행 중", "대기 중");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawStatusIndicator(bool isActive, string activeText, string inactiveText)
        {
            Color originalColor = GUI.color;
            GUI.color = isActive ? Color.green : Color.gray;
            EditorGUILayout.LabelField(isActive ? activeText : inactiveText);
            GUI.color = originalColor;
        }

        private void DrawControlButtons()
        {
            EditorGUILayout.BeginHorizontal();

            GUI.enabled = !selfTraining.IsHostRunning;
            if (GUILayout.Button("시작", GUILayout.Height(30)))
            {
                StartSelfTraining();
            }

            GUI.enabled = selfTraining.IsHostRunning;
            if (GUILayout.Button("중지", GUILayout.Height(30)))
            {
                selfTraining.StopSelfTraining();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            GUI.enabled = selfTraining.IsTrainingActive && Time.timeScale > 0;
            if (GUILayout.Button("일시정지", GUILayout.Height(25)))
            {
                selfTraining.PauseTraining();
            }

            GUI.enabled = selfTraining.IsTrainingActive && Time.timeScale == 0;
            if (GUILayout.Button("재개", GUILayout.Height(25)))
            {
                selfTraining.ResumeTraining();
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCustomSettingsSection()
        {
            showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "훈련 설정");

            if (showAdvancedSettings)
            {
                EditorGUI.indentLevel++;
                customPlayerName = EditorGUILayout.TextField("플레이어 이름", customPlayerName);
                customSceneName = EditorGUILayout.TextField("씬 이름", customSceneName);
                customScenarioType = EditorGUILayout.IntField("시나리오 타입", customScenarioType);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawTrainingDataSection()
        {
            EditorGUILayout.LabelField("현재 훈련 정보", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var data = selfTraining.CurrentTrainingData;

            EditorGUILayout.LabelField($"파티 ID: {data.PartyId}");
            EditorGUILayout.LabelField($"인스턴스 ID: {data.InstanceId}");
            EditorGUILayout.LabelField($"플레이어: {data.PlayerName} ({data.PlayerId})");
            EditorGUILayout.LabelField($"씬: {data.SceneFile}");
            EditorGUILayout.LabelField($"시작 시간: {data.StartTime:HH:mm:ss}");
            EditorGUILayout.LabelField($"멤버 수: {data.Members.Count}");

            if (data.Members.Count > 0)
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("멤버 목록:", EditorStyles.miniLabel);
                EditorGUI.indentLevel++;
                foreach (var member in data.Members)
                {
                    string status = member.IsHost ? "[Host]" : "";
                    string connected = member.IsConnected ? "연결됨" : "대기 중";
                    EditorGUILayout.LabelField($"{status} {member.MemberName} - {connected}");
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void StartSelfTraining()
        {
            var trainingData = selfTraining.CreateTrainingData(
                customSceneName,
                $"editor_player_{System.Guid.NewGuid():N}".Substring(0, 16),
                customPlayerName,
                customScenarioType
            );

            selfTraining.StartSelfTraining(trainingData);
        }
    }

    /// <summary>
    /// 메뉴에서 SelfTrainingMode를 쉽게 추가할 수 있도록 지원
    /// </summary>
    public static class SelfTrainingModeMenu
    {
        [MenuItem("GameObject/Network/Self Training Mode", false, 10)]
        private static void CreateSelfTrainingMode()
        {
            // 기존 SelfTrainingMode 확인
            var existing = Object.FindFirstObjectByType<SelfTrainingMode>();
            if (existing != null)
            {
                Debug.LogWarning("씬에 이미 SelfTrainingMode가 존재합니다.");
                Selection.activeGameObject = existing.gameObject;
                return;
            }

            // 새 GameObject 생성
            var go = new GameObject("SelfTrainingMode");
            go.AddComponent<SelfTrainingMode>();

            // NetworkManager가 없으면 경고
            var networkManager = Object.FindFirstObjectByType<FishNet.Managing.NetworkManager>();
            if (networkManager == null)
            {
                Debug.LogWarning("씬에 Fishnet NetworkManager가 없습니다. SelfTrainingMode가 작동하려면 NetworkManager가 필요합니다.");
            }

            Undo.RegisterCreatedObjectUndo(go, "Create SelfTrainingMode");
            Selection.activeGameObject = go;
        }

        [MenuItem("Tools/Self Training/Start Self Training")]
        private static void StartSelfTraining()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Play 모드에서만 Self Training을 시작할 수 있습니다.");
                return;
            }

            var selfTraining = SelfTrainingMode.Instance;
            if (selfTraining == null)
            {
                Debug.LogError("SelfTrainingMode를 찾을 수 없습니다. 씬에 SelfTrainingMode 컴포넌트를 추가하세요.");
                return;
            }

            selfTraining.StartSelfTraining();
        }

        [MenuItem("Tools/Self Training/Stop Self Training")]
        private static void StopSelfTraining()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Play 모드에서만 Self Training을 중지할 수 있습니다.");
                return;
            }

            var selfTraining = SelfTrainingMode.Instance;
            if (selfTraining == null)
            {
                Debug.LogError("SelfTrainingMode를 찾을 수 없습니다.");
                return;
            }

            selfTraining.StopSelfTraining();
        }
    }
}
#endif
