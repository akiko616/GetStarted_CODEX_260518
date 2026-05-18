using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace EncodingTools.Editor
{
    /// <summary>파일 인코딩 변환 에디터 윈도우</summary>
    public class EncodingConverterWindow : EditorWindow
    {
        // EditorPrefs 키
        private const string PREF_ROOT_PATH = "EncodingConverter.RootPath";
        private const string PREF_MANIFEST_PATH = "EncodingConverter.ManifestPath";
        private const string PREF_ENABLE_BACKUP = "EncodingConverter.EnableBackup";

        private TextField rootPathField;
        private Toggle enableBackupToggle;
        private Button browseButton;
        private Button scanButton;
        private ListView fileListView;
        private Button convertAllButton;
        private TextField manifestPathField;
        private Button browseManifestButton;
        private Button verifyButton;
        private Button restoreButton;
        private Button deleteButton;
        private Label statusLabel;
        private Label backupInfoLabel;

        // 깨진 한글 복원 UI
        private Button scanBrokenButton;
        private ListView brokenKoreanListView;
        private Button fixAllBrokenButton;
        private Label brokenStatusLabel;
        private Foldout brokenKoreanFoldout;

        // 진행바 UI
        private VisualElement progressContainer;
        private ProgressBar progressBar;
        private Label progressLabel;

        private List<FileInfo> ansiFiles = new List<FileInfo>();
        private List<EncodingConverter.BrokenKoreanFileInfo> brokenKoreanFiles = new List<EncodingConverter.BrokenKoreanFileInfo>();
        private string currentRootPath;
        private BackupManifest currentManifest;
        private string currentManifestPath;

        // 백그라운드 작업 제어
        private CancellationTokenSource cancellationTokenSource;
        private bool isProcessing;

        [MenuItem("Tools/Encoding Converter")]
        public static void ShowWindow()
        {
            var window = GetWindow<EncodingConverterWindow>();
            window.titleContent = new GUIContent("Encoding Converter");
            window.minSize = new Vector2(600, 600);
        }

        private void OnDestroy()
        {
            // 백그라운드 작업 취소
            cancellationTokenSource?.Cancel();
            cancellationTokenSource?.Dispose();
        }

        /// <summary>UI 생성</summary>
        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;

            // 상단 고정 영역 (스크롤 안됨)
            var topSection = new VisualElement();
            topSection.style.paddingTop = 10;
            topSection.style.paddingLeft = 10;
            topSection.style.paddingRight = 10;
            topSection.style.flexShrink = 0;

            CreateRootPathSection(topSection);
            topSection.Add(CreateSeparator());

            root.Add(topSection);

            // 중앙 파일 리스트 영역 (스크롤 가능)
            var middleSection = new VisualElement();
            middleSection.style.flexGrow = 1;
            middleSection.style.paddingLeft = 10;
            middleSection.style.paddingRight = 10;
            middleSection.style.minHeight = 200;

            CreateFileListSection(middleSection);

            root.Add(middleSection);

            // 하단 고정 영역 (스크롤 안됨)
            var bottomSection = new VisualElement();
            bottomSection.style.paddingLeft = 10;
            bottomSection.style.paddingRight = 10;
            bottomSection.style.paddingBottom = 10;
            bottomSection.style.flexShrink = 0;

            bottomSection.Add(CreateSeparator());

            // 변환 버튼
            CreateConversionSection(bottomSection);

            bottomSection.Add(CreateSeparator());

            // 깨진 한글 복원 섹션
            CreateBrokenKoreanSection(bottomSection);

            bottomSection.Add(CreateSeparator());

            // 진행바
            CreateProgressSection(bottomSection);

            bottomSection.Add(CreateSeparator());

            // 복원 섹션
            CreateRestoreSection(bottomSection);

            // 상태 표시
            CreateStatusSection(bottomSection);

            root.Add(bottomSection);

            // 저장된 경로 로드
            LoadSavedPaths();
        }

        private void CreateFileListSection(VisualElement parent)
        {
            var section = new VisualElement();
            section.style.flexGrow = 1;
            section.style.marginBottom = 10;

            var label = new Label("ANSI 인코딩 파일 목록 (한글 자동 감지)");
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginBottom = 5;
            section.Add(label);

            fileListView = new ListView();
            fileListView.style.flexGrow = 1;
            fileListView.selectionType = SelectionType.Multiple;
            fileListView.showBorder = true;
            fileListView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;

            fileListView.makeItem = MakeFileListItem;
            fileListView.bindItem = BindFileListItem;

            section.Add(fileListView);
            parent.Add(section);
        }

        private void CreateProgressSection(VisualElement root)
        {
            progressContainer = new VisualElement();
            progressContainer.style.marginBottom = 10;
            progressContainer.style.display = DisplayStyle.None; // 초기에는 숨김

            var label = new Label("진행 상황");
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginBottom = 5;
            progressContainer.Add(label);

            progressBar = new ProgressBar();
            progressBar.style.height = 20;
            progressBar.style.marginBottom = 5;
            progressContainer.Add(progressBar);

            progressLabel = new Label("대기 중...");
            progressLabel.style.fontSize = 11;
            progressLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            progressContainer.Add(progressLabel);

            root.Add(progressContainer);
        }

        private void ShowProgress(bool show)
        {
            progressContainer.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateProgress(ProgressInfo info)
        {
            EditorApplication.delayCall += () =>
            {
                if (progressBar != null)
                {
                    progressBar.value = info.progress * 100f;
                    progressBar.title = $"{info.current} / {info.total}";
                }

                if (progressLabel != null)
                {
                    progressLabel.text = info.message;
                }
            };
        }

        /// <summary>저장된 경로를 로드합니다</summary>
        private void LoadSavedPaths()
        {
            // 루트 경로 로드
            if (EditorPrefs.HasKey(PREF_ROOT_PATH))
            {
                var savedRootPath = EditorPrefs.GetString(PREF_ROOT_PATH);
                if (Directory.Exists(savedRootPath))
                {
                    rootPathField.value = savedRootPath;
                    currentRootPath = savedRootPath;
                }
            }

            // 매니페스트 경로 로드
            if (EditorPrefs.HasKey(PREF_MANIFEST_PATH))
            {
                var savedManifestPath = EditorPrefs.GetString(PREF_MANIFEST_PATH);
                if (File.Exists(savedManifestPath))
                {
                    manifestPathField.value = savedManifestPath;
                    LoadManifest(savedManifestPath);
                }
            }

            // 백업 활성화 설정 로드 (기본값: true)
            var enableBackup = EditorPrefs.GetBool(PREF_ENABLE_BACKUP, true);
            if (enableBackupToggle != null)
            {
                enableBackupToggle.value = enableBackup;
            }
            UpdateConvertButtonText();
        }

        /// <summary>루트 경로를 저장합니다</summary>
        private void SaveRootPath(string path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                EditorPrefs.SetString(PREF_ROOT_PATH, path);
            }
        }

        /// <summary>매니페스트 경로를 저장합니다</summary>
        private void SaveManifestPath(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                EditorPrefs.SetString(PREF_MANIFEST_PATH, path);
            }
            else
            {
                EditorPrefs.DeleteKey(PREF_MANIFEST_PATH);
            }
        }

        private void CreateRootPathSection(VisualElement root)
        {
            var section = new VisualElement();
            section.style.marginBottom = 10;

            var labelRow = new VisualElement();
            labelRow.style.flexDirection = FlexDirection.Row;
            labelRow.style.justifyContent = Justify.SpaceBetween;
            labelRow.style.marginBottom = 5;

            // 왼쪽: ? 버튼 + 라벨
            var leftRow = new VisualElement();
            leftRow.style.flexDirection = FlexDirection.Row;
            leftRow.style.alignItems = Align.Center;

            var infoButton = new Button(ShowCreatorInfo);
            infoButton.text = "?";
            infoButton.style.width = 20;
            infoButton.style.height = 20;
            infoButton.style.marginRight = 5;
            infoButton.style.fontSize = 12;
            infoButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            infoButton.style.borderTopLeftRadius = 10;
            infoButton.style.borderTopRightRadius = 10;
            infoButton.style.borderBottomLeftRadius = 10;
            infoButton.style.borderBottomRightRadius = 10;
            leftRow.Add(infoButton);

            var label = new Label("루트 폴더 경로");
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            leftRow.Add(label);

            labelRow.Add(leftRow);

            // 오른쪽: 초기화 버튼
            var clearButton = new Button(OnClearRootPathClicked);
            clearButton.text = "경로 초기화";
            clearButton.style.width = 80;
            clearButton.style.height = 18;
            clearButton.style.fontSize = 10;
            labelRow.Add(clearButton);

            section.Add(labelRow);

            var pathRow = new VisualElement();
            pathRow.style.flexDirection = FlexDirection.Row;

            rootPathField = new TextField();
            rootPathField.style.flexGrow = 1;
            rootPathField.style.marginRight = 5;
            rootPathField.value = Application.dataPath;

            rootPathField.RegisterValueChangedCallback(evt =>
            {
                if (Directory.Exists(evt.newValue))
                {
                    SaveRootPath(evt.newValue);
                }
            });

            pathRow.Add(rootPathField);

            browseButton = new Button(OnBrowseClicked);
            browseButton.text = "찾아보기";
            browseButton.style.width = 80;
            pathRow.Add(browseButton);

            scanButton = new Button(OnScanClicked);
            scanButton.text = "스캔";
            scanButton.style.width = 80;
            scanButton.style.marginLeft = 5;
            pathRow.Add(scanButton);

            section.Add(pathRow);
            root.Add(section);
        }
        /// <summary>제작자 정보를 표시합니다 (간단 버전)</summary>
        private void ShowCreatorInfo()
        {
            EditorUtility.DisplayDialog(
                "📝 ANSI to UTF-8 인코딩 변환 도구",
                "ANSI로 한글깨지는게 짜증나서 만듬\n\n" +
                "제작자: noweel\n" +
                "AI 협업: Claude Sonnet 4.5\n\n" +
                "© 2025 All rights reserved",
                "확인"
            );
        }

        private void CreateConversionSection(VisualElement root)
        {
            var section = new VisualElement();
            section.style.marginBottom = 10;

            // 백업 활성화 체크박스
            var backupRow = new VisualElement();
            backupRow.style.flexDirection = FlexDirection.Row;
            backupRow.style.alignItems = Align.Center;
            backupRow.style.marginBottom = 8;

            enableBackupToggle = new Toggle();
            enableBackupToggle.value = true; // 기본값: 활성화
            enableBackupToggle.style.marginRight = 5;
            enableBackupToggle.RegisterValueChangedCallback(evt =>
            {
                EditorPrefs.SetBool(PREF_ENABLE_BACKUP, evt.newValue);
                UpdateConvertButtonText();
            });
            backupRow.Add(enableBackupToggle);

            var backupLabel = new Label("변환 시 원본 백업 생성");
            backupLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            backupRow.Add(backupLabel);

            section.Add(backupRow);

            convertAllButton = new Button(OnConvertAllClicked);
            convertAllButton.text = "전체 변환 (ANSI → UTF-8, 바이너리 백업 생성)";
            convertAllButton.style.height = 30;
            convertAllButton.SetEnabled(false);

            section.Add(convertAllButton);
            root.Add(section);
        }

        /// <summary>변환 버튼 텍스트를 백업 설정에 따라 업데이트합니다.</summary>
        private void UpdateConvertButtonText()
        {
            if (convertAllButton == null)
                return;

            if (enableBackupToggle != null && enableBackupToggle.value)
            {
                convertAllButton.text = "전체 변환 (ANSI → UTF-8, 바이너리 백업 생성)";
            }
            else
            {
                convertAllButton.text = "전체 변환 (ANSI → UTF-8, 백업 없음)";
            }
        }

        /// <summary>깨진 한글 복원 섹션을 생성합니다</summary>
        private void CreateBrokenKoreanSection(VisualElement root)
        {
            // Foldout으로 접을 수 있게 구성
            brokenKoreanFoldout = new Foldout();
            brokenKoreanFoldout.text = "깨진 한글 복원 (UTF-8로 잘못 변환된 파일)";
            brokenKoreanFoldout.value = false; // 기본적으로 접힌 상태
            brokenKoreanFoldout.style.marginBottom = 10;

            var section = new VisualElement();
            section.style.paddingLeft = 10;

            // 설명
            var descLabel = new Label("UTF-8 형식이지만 한글이 깨진 파일을 검색하고 복원합니다.");
            descLabel.style.fontSize = 11;
            descLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            descLabel.style.marginBottom = 5;
            section.Add(descLabel);

            // 스캔 버튼
            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.marginBottom = 5;

            scanBrokenButton = new Button(OnScanBrokenClicked);
            scanBrokenButton.text = "깨진 한글 스캔";
            scanBrokenButton.style.height = 25;
            scanBrokenButton.style.flexGrow = 1;
            scanBrokenButton.style.marginRight = 5;
            buttonRow.Add(scanBrokenButton);

            fixAllBrokenButton = new Button(OnFixAllBrokenClicked);
            fixAllBrokenButton.text = "전체 복원";
            fixAllBrokenButton.style.height = 25;
            fixAllBrokenButton.style.width = 100;
            fixAllBrokenButton.SetEnabled(false);
            buttonRow.Add(fixAllBrokenButton);

            section.Add(buttonRow);

            // 결과 리스트
            brokenKoreanListView = new ListView();
            brokenKoreanListView.style.height = 120;
            brokenKoreanListView.style.marginBottom = 5;
            brokenKoreanListView.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
            brokenKoreanListView.style.borderTopWidth = 1;
            brokenKoreanListView.style.borderBottomWidth = 1;
            brokenKoreanListView.style.borderLeftWidth = 1;
            brokenKoreanListView.style.borderRightWidth = 1;
            brokenKoreanListView.style.borderTopColor = new Color(0.3f, 0.3f, 0.3f);
            brokenKoreanListView.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f);
            brokenKoreanListView.style.borderLeftColor = new Color(0.3f, 0.3f, 0.3f);
            brokenKoreanListView.style.borderRightColor = new Color(0.3f, 0.3f, 0.3f);

            brokenKoreanListView.makeItem = MakeBrokenKoreanItem;
            brokenKoreanListView.bindItem = BindBrokenKoreanItem;
            brokenKoreanListView.itemsSource = brokenKoreanFiles;
            brokenKoreanListView.fixedItemHeight = 50;
            brokenKoreanListView.selectionType = SelectionType.Single;

            section.Add(brokenKoreanListView);

            // 상태 레이블
            brokenStatusLabel = new Label();
            brokenStatusLabel.style.fontSize = 11;
            brokenStatusLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            section.Add(brokenStatusLabel);

            brokenKoreanFoldout.Add(section);
            root.Add(brokenKoreanFoldout);
        }

        /// <summary>깨진 한글 리스트 아이템 생성</summary>
        private VisualElement MakeBrokenKoreanItem()
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.paddingTop = 3;
            container.style.paddingBottom = 3;
            container.style.paddingLeft = 5;
            container.style.paddingRight = 5;
            container.style.alignItems = Align.Center;

            // 정보 영역
            var infoContainer = new VisualElement();
            infoContainer.style.flexGrow = 1;

            var pathLabel = new Label();
            pathLabel.name = "path-label";
            pathLabel.style.fontSize = 11;
            pathLabel.style.color = Color.white;
            infoContainer.Add(pathLabel);

            var sampleContainer = new VisualElement();
            sampleContainer.style.flexDirection = FlexDirection.Row;
            sampleContainer.style.marginTop = 2;

            var brokenLabel = new Label();
            brokenLabel.name = "broken-label";
            brokenLabel.style.fontSize = 10;
            brokenLabel.style.color = new Color(1f, 0.5f, 0.5f);
            brokenLabel.style.flexGrow = 1;
            sampleContainer.Add(brokenLabel);

            var arrowLabel = new Label(" → ");
            arrowLabel.style.fontSize = 10;
            arrowLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            sampleContainer.Add(arrowLabel);

            var fixedLabel = new Label();
            fixedLabel.name = "fixed-label";
            fixedLabel.style.fontSize = 10;
            fixedLabel.style.color = new Color(0.5f, 1f, 0.5f);
            fixedLabel.style.flexGrow = 1;
            sampleContainer.Add(fixedLabel);

            infoContainer.Add(sampleContainer);
            container.Add(infoContainer);

            // 개별 복원 버튼
            var fixButton = new Button();
            fixButton.name = "fix-button";
            fixButton.text = "복원";
            fixButton.style.width = 50;
            fixButton.style.height = 20;
            fixButton.style.fontSize = 10;
            container.Add(fixButton);

            return container;
        }

        /// <summary>깨진 한글 리스트 아이템 바인딩</summary>
        private void BindBrokenKoreanItem(VisualElement element, int index)
        {
            if (index < 0 || index >= brokenKoreanFiles.Count)
                return;

            var info = brokenKoreanFiles[index];

            var pathLabel = element.Q<Label>("path-label");
            var brokenLabel = element.Q<Label>("broken-label");
            var fixedLabel = element.Q<Label>("fixed-label");
            var fixButton = element.Q<Button>("fix-button");

            pathLabel.text = info.relativePath;
            brokenLabel.text = info.sampleBroken;
            fixedLabel.text = info.sampleFixed;

            // 기존 콜백 제거 후 새로 등록
            fixButton.clicked -= null;
            fixButton.clicked += () => OnFixSingleBrokenClicked(info);
        }

        /// <summary>깨진 한글 스캔 버튼 클릭</summary>
        private async void OnScanBrokenClicked()
        {
            if (isProcessing)
                return;

            if (string.IsNullOrEmpty(currentRootPath) || !Directory.Exists(currentRootPath))
            {
                EditorUtility.DisplayDialog("오류", "먼저 유효한 스캔 경로를 설정하세요.", "확인");
                return;
            }

            isProcessing = true;
            SetUIEnabled(false);
            ShowProgress(true);

            brokenKoreanFiles.Clear();
            brokenKoreanListView.Rebuild();

            cancellationTokenSource?.Cancel();
            cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var rootPath = currentRootPath;

                var results = await Task.Run(() =>
                {
                    return EncodingConverter.ScanBrokenKoreanFiles(rootPath, UpdateProgress);
                }, cancellationTokenSource.Token);

                brokenKoreanFiles.Clear();
                brokenKoreanFiles.AddRange(results);
                brokenKoreanListView.Rebuild();

                fixAllBrokenButton.SetEnabled(brokenKoreanFiles.Count > 0);
                brokenStatusLabel.text = $"발견된 깨진 한글 파일: {brokenKoreanFiles.Count}개";

                if (brokenKoreanFiles.Count > 0)
                {
                    brokenKoreanFoldout.value = true; // 결과가 있으면 펼치기
                    brokenStatusLabel.style.color = new Color(1f, 0.8f, 0.3f);
                }
                else
                {
                    brokenStatusLabel.style.color = new Color(0.5f, 1f, 0.5f);
                }

                statusLabel.text = $"깨진 한글 스캔 완료: {brokenKoreanFiles.Count}개 발견";
                statusLabel.style.color = new Color(0.2f, 0.8f, 0.2f);
            }
            catch (System.OperationCanceledException)
            {
                statusLabel.text = "스캔이 취소되었습니다.";
                statusLabel.style.color = new Color(1.0f, 0.6f, 0.0f);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"스캔 오류: {ex.Message}");
                statusLabel.text = "스캔 중 오류가 발생했습니다.";
                statusLabel.style.color = new Color(1.0f, 0.3f, 0.3f);
            }
            finally
            {
                isProcessing = false;
                SetUIEnabled(true);
                ShowProgress(false);
            }
        }

        /// <summary>단일 깨진 한글 파일 복원</summary>
        private void OnFixSingleBrokenClicked(EncodingConverter.BrokenKoreanFileInfo info)
        {
            if (isProcessing)
                return;

            bool enableBackup = enableBackupToggle != null && enableBackupToggle.value;

            string dialogMessage = $"'{info.relativePath}'의 깨진 한글을 복원하시겠습니까?\n\n" +
                                   $"미리보기:\n" +
                                   $"깨진: {info.sampleBroken}\n" +
                                   $"복원: {info.sampleFixed}";

            if (!enableBackup)
            {
                dialogMessage += "\n\n⚠️ 백업이 생성되지 않습니다.";
            }

            if (!EditorUtility.DisplayDialog("깨진 한글 복원", dialogMessage, "복원", "취소"))
            {
                return;
            }

            bool success;
            if (enableBackup)
            {
                success = EncodingConverter.FixBrokenKoreanFile(info.fullPath, currentRootPath, out _);
            }
            else
            {
                success = EncodingConverter.FixBrokenKoreanFileWithoutBackup(info.fullPath);
            }

            if (success)
            {
                EditorUtility.DisplayDialog("복원 완료", "깨진 한글이 복원되었습니다.", "확인");

                // 리스트에서 제거
                brokenKoreanFiles.Remove(info);
                brokenKoreanListView.Rebuild();
                fixAllBrokenButton.SetEnabled(brokenKoreanFiles.Count > 0);
                brokenStatusLabel.text = $"남은 깨진 한글 파일: {brokenKoreanFiles.Count}개";

                AssetDatabase.Refresh();
            }
            else
            {
                EditorUtility.DisplayDialog("복원 실패", "파일 복원 중 오류가 발생했습니다.", "확인");
            }
        }

        /// <summary>전체 깨진 한글 복원</summary>
        private async void OnFixAllBrokenClicked()
        {
            if (isProcessing || brokenKoreanFiles.Count == 0)
                return;

            bool enableBackup = enableBackupToggle != null && enableBackupToggle.value;

            string dialogMessage = $"{brokenKoreanFiles.Count}개 파일의 깨진 한글을 복원하시겠습니까?";

            if (!enableBackup)
            {
                dialogMessage += "\n\n⚠️ 백업이 생성되지 않습니다.";
            }

            if (!EditorUtility.DisplayDialog("전체 깨진 한글 복원", dialogMessage, "복원", "취소"))
            {
                return;
            }

            isProcessing = true;
            SetUIEnabled(false);
            ShowProgress(true);

            cancellationTokenSource?.Cancel();
            cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var filesToFix = new List<EncodingConverter.BrokenKoreanFileInfo>(brokenKoreanFiles);
                var rootPath = currentRootPath;
                var backup = enableBackup;

                var fixedCount = await Task.Run(() =>
                {
                    return EncodingConverter.FixMultipleBrokenKoreanFiles(filesToFix, rootPath, backup, UpdateProgress);
                }, cancellationTokenSource.Token);

                EditorApplication.delayCall += () =>
                {
                    EditorUtility.DisplayDialog("복원 완료",
                        $"{fixedCount}개 파일의 깨진 한글이 복원되었습니다." +
                        (enableBackup ? "" : "\n(백업 없음)"), "확인");

                    brokenKoreanFiles.Clear();
                    brokenKoreanListView.Rebuild();
                    fixAllBrokenButton.SetEnabled(false);
                    brokenStatusLabel.text = "복원 완료";
                    brokenStatusLabel.style.color = new Color(0.5f, 1f, 0.5f);

                    statusLabel.text = $"✓ 깨진 한글 복원 완료: {fixedCount}개 파일";
                    statusLabel.style.color = new Color(0.2f, 0.8f, 0.2f);

                    AssetDatabase.Refresh();
                };
            }
            catch (System.OperationCanceledException)
            {
                statusLabel.text = "복원이 취소되었습니다.";
                statusLabel.style.color = new Color(1.0f, 0.6f, 0.0f);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"복원 오류: {ex.Message}");
                statusLabel.text = "복원 중 오류가 발생했습니다.";
                statusLabel.style.color = new Color(1.0f, 0.3f, 0.3f);
            }
            finally
            {
                isProcessing = false;
                SetUIEnabled(true);
                ShowProgress(false);
            }
        }

        private void CreateRestoreSection(VisualElement root)
        {
            var section = new VisualElement();
            section.style.marginBottom = 10;

            var labelRow = new VisualElement();
            labelRow.style.flexDirection = FlexDirection.Row;
            labelRow.style.justifyContent = Justify.SpaceBetween;
            labelRow.style.marginBottom = 5;

            var label = new Label("백업 복원 (바이너리 백업)");
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            labelRow.Add(label);

            var clearManifestButton = new Button(OnClearManifestClicked);
            clearManifestButton.text = "백업 초기화";
            clearManifestButton.style.width = 80;
            clearManifestButton.style.height = 18;
            clearManifestButton.style.fontSize = 10;
            labelRow.Add(clearManifestButton);

            section.Add(labelRow);

            // 매니페스트 경로
            var manifestRow = new VisualElement();
            manifestRow.style.flexDirection = FlexDirection.Row;
            manifestRow.style.marginBottom = 5;

            manifestPathField = new TextField();
            manifestPathField.style.flexGrow = 1;
            manifestPathField.style.marginRight = 5;
            manifestPathField.SetEnabled(false);
            manifestRow.Add(manifestPathField);

            browseManifestButton = new Button(OnBrowseManifestClicked);
            browseManifestButton.text = "찾아보기";
            browseManifestButton.style.width = 80;
            manifestRow.Add(browseManifestButton);

            section.Add(manifestRow);

            // 백업 정보 (3줄)
            backupInfoLabel = new Label();
            backupInfoLabel.style.marginBottom = 5;
            backupInfoLabel.style.fontSize = 11;
            backupInfoLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            backupInfoLabel.style.whiteSpace = WhiteSpace.Normal; // 여러 줄 허용
            section.Add(backupInfoLabel);

            // 버튼 행
            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;

            verifyButton = new Button(OnVerifyClicked);
            verifyButton.text = "검증";
            verifyButton.style.flexGrow = 1;
            verifyButton.style.height = 30;
            verifyButton.style.marginRight = 5;
            verifyButton.SetEnabled(false);
            buttonRow.Add(verifyButton);

            restoreButton = new Button(OnRestoreClicked);
            restoreButton.text = "복원";
            restoreButton.style.flexGrow = 1;
            restoreButton.style.height = 30;
            restoreButton.style.marginRight = 5;
            restoreButton.SetEnabled(false);
            buttonRow.Add(restoreButton);

            deleteButton = new Button(OnDeleteClicked);
            deleteButton.text = "삭제";
            deleteButton.style.flexGrow = 1;
            deleteButton.style.height = 30;
            deleteButton.SetEnabled(false);
            buttonRow.Add(deleteButton);

            section.Add(buttonRow);
            root.Add(section);
        }

        private void CreateStatusSection(VisualElement root)
        {
            statusLabel = new Label("경로를 설정하고 스캔 버튼을 클릭하세요.");
            statusLabel.style.marginTop = 10;
            statusLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            statusLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            root.Add(statusLabel);
        }

        private VisualElement CreateSeparator()
        {
            var separator = new VisualElement();
            separator.style.height = 1;
            separator.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            separator.style.marginTop = 10;
            separator.style.marginBottom = 10;
            return separator;
        }

        private VisualElement MakeFileListItem()
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.paddingTop = 2;
            container.style.paddingBottom = 2;
            container.style.paddingLeft = 5;
            container.style.paddingRight = 5;

            var pathLabel = new Label();
            pathLabel.name = "PathLabel";
            pathLabel.style.flexGrow = 1;
            pathLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            container.Add(pathLabel);

            var convertButton = new Button();
            convertButton.name = "ConvertButton";
            convertButton.text = "변환";
            convertButton.style.width = 60;
            container.Add(convertButton);

            return container;
        }

        private void BindFileListItem(VisualElement element, int index)
        {
            if (index >= ansiFiles.Count)
                return;

            var fileInfo = ansiFiles[index];
            var pathLabel = element.Q<Label>("PathLabel");
            var convertButton = element.Q<Button>("ConvertButton");

            pathLabel.text = GetRelativeDisplayPath(fileInfo.FullName);

            convertButton.clicked -= null;
            convertButton.clicked += () => OnConvertSingleClicked(fileInfo);
        }

        private string GetRelativeDisplayPath(string fullPath)
        {
            if (string.IsNullOrEmpty(currentRootPath))
                return fullPath;

            try
            {
                var rootUri = new System.Uri(currentRootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
                var fullUri = new System.Uri(fullPath);

                if (rootUri.IsBaseOf(fullUri))
                {
                    var relativeUri = rootUri.MakeRelativeUri(fullUri);
                    return System.Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
                }
            }
            catch { }

            return fullPath;
        }

        private void SetUIEnabled(bool enabled)
        {
            browseButton.SetEnabled(enabled);
            scanButton.SetEnabled(enabled);
            convertAllButton.SetEnabled(enabled && ansiFiles.Count > 0);
            browseManifestButton.SetEnabled(enabled);
            verifyButton.SetEnabled(enabled && currentManifest != null);
            restoreButton.SetEnabled(enabled && currentManifest != null);
            deleteButton.SetEnabled(enabled && currentManifest != null);

            // 깨진 한글 복원 UI
            scanBrokenButton?.SetEnabled(enabled);
            fixAllBrokenButton?.SetEnabled(enabled && brokenKoreanFiles.Count > 0);
        }

        private void OnBrowseClicked()
        {
            var path = EditorUtility.OpenFolderPanel("루트 폴더 선택", rootPathField.value, "");

            if (!string.IsNullOrEmpty(path))
            {
                rootPathField.value = path;
                SaveRootPath(path);
            }
        }

        private void OnClearRootPathClicked()
        {
            if (EditorUtility.DisplayDialog("경로 초기화",
                "저장된 루트 경로를 초기화하시겠습니까?",
                "초기화", "취소"))
            {
                EditorPrefs.DeleteKey(PREF_ROOT_PATH);
                rootPathField.value = Application.dataPath;
                currentRootPath = string.Empty;

                ansiFiles.Clear();
                fileListView.itemsSource = ansiFiles;
                fileListView.Rebuild();
                convertAllButton.SetEnabled(false);

                statusLabel.text = "루트 경로가 초기화되었습니다.";
                statusLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            }
        }

        private async void OnScanClicked()
        {
            if (isProcessing)
                return;

            currentRootPath = rootPathField.value;

            if (string.IsNullOrEmpty(currentRootPath) || !Directory.Exists(currentRootPath))
            {
                EditorUtility.DisplayDialog("오류", "유효한 경로를 입력하세요.", "확인");
                return;
            }

            SaveRootPath(currentRootPath);

            isProcessing = true;
            SetUIEnabled(false);
            ShowProgress(true);

            cancellationTokenSource?.Cancel();
            cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var rootPath = currentRootPath;
                var files = await Task.Run(() =>
                {
                    return EncodingConverter.FindAnsiFiles(rootPath, UpdateProgress);
                }, cancellationTokenSource.Token);

                ansiFiles = files;

                EditorApplication.delayCall += () =>
                {
                    fileListView.itemsSource = ansiFiles;
                    fileListView.Rebuild();

                    convertAllButton.SetEnabled(ansiFiles.Count > 0);

                    if (ansiFiles.Count > 0)
                    {
                        statusLabel.text = $"✓ ANSI/EUC-KR 파일 {ansiFiles.Count}개 발견 (한글 자동 감지)";
                        statusLabel.style.color = new Color(0.2f, 0.8f, 0.2f);
                    }
                    else
                    {
                        statusLabel.text = "ANSI 인코딩 파일이 없습니다.";
                        statusLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
                    }
                };
            }
            catch (System.OperationCanceledException)
            {
                statusLabel.text = "스캔이 취소되었습니다.";
                statusLabel.style.color = new Color(1.0f, 0.6f, 0.0f);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"스캔 오류: {ex.Message}");
                statusLabel.text = "스캔 중 오류가 발생했습니다.";
                statusLabel.style.color = new Color(1.0f, 0.3f, 0.3f);
            }
            finally
            {
                isProcessing = false;
                SetUIEnabled(true);
                ShowProgress(false);
            }
        }

        private void OnConvertSingleClicked(FileInfo fileInfo)
        {
            if (isProcessing)
                return;

            bool enableBackup = enableBackupToggle != null && enableBackupToggle.value;

            if (enableBackup)
            {
                // 백업 활성화: 기존 로직
                ConvertSingleWithBackup(fileInfo);
            }
            else
            {
                // 백업 비활성화: 백업 없이 변환
                ConvertSingleWithoutBackup(fileInfo);
            }
        }

        /// <summary>단일 파일 변환 (백업 포함)</summary>
        private void ConvertSingleWithBackup(FileInfo fileInfo)
        {
            // 기존 백업 확인
            var backupDir = Path.Combine(currentRootPath, "Backup");
            var manifestPath = Path.Combine(backupDir, "backup_manifest.json");

            string dialogMessage = $"'{GetRelativeDisplayPath(fileInfo.FullName)}'를 UTF-8로 변환하시겠습니까?\n\n";

            if (File.Exists(manifestPath))
            {
                var existingManifest = EncodingConverter.LoadManifest(manifestPath);
                if (existingManifest != null)
                {
                    var existingInfo = existingManifest.conversions.FirstOrDefault(c => c.originalPath == fileInfo.FullName);

                    if (existingInfo != null)
                    {
                        EditorUtility.DisplayDialog("이미 백업됨",
                            $"이 파일은 이미 백업되어 있습니다.\n\n" +
                            $"백업 시간: {existingInfo.convertedAt:yyyy-MM-dd HH:mm:ss}\n" +
                            $"감지된 인코딩: {existingInfo.detectedEncoding}\n\n" +
                            $"중복 백업을 방지하기 위해 변환이 취소됩니다.",
                            "확인");
                        return;
                    }

                    dialogMessage += $"기존 백업({Path.GetFileName(existingManifest.backupDataFile)})에 병합됩니다.";
                }
                else
                {
                    dialogMessage += "원본은 바이너리 백업으로 저장됩니다.";
                }
            }
            else
            {
                dialogMessage += "원본은 바이너리 백업으로 저장됩니다.";
            }

            if (!EditorUtility.DisplayDialog("변환 확인", dialogMessage, "변환", "취소"))
            {
                return;
            }

            var success = EncodingConverter.ConvertToUtf8(fileInfo.FullName, currentRootPath, out var info);

            if (success)
            {
                EditorUtility.DisplayDialog("변환 완료",
                    $"파일이 UTF-8로 변환되었습니다.\n감지된 인코딩: {info.detectedEncoding}", "확인");

                OnScanClicked();
                AssetDatabase.Refresh();
            }
            else
            {
                if (info != null)
                {
                    // 이미 백업된 경우
                    EditorUtility.DisplayDialog("변환 건너뜀",
                        "이 파일은 이미 백업되어 있어 변환을 건너뛰었습니다.", "확인");
                }
                else
                {
                    EditorUtility.DisplayDialog("변환 실패", "파일 변환 중 오류가 발생했습니다.", "확인");
                }
            }
        }

        /// <summary>단일 파일 변환 (백업 없음)</summary>
        private void ConvertSingleWithoutBackup(FileInfo fileInfo)
        {
            string dialogMessage = $"'{GetRelativeDisplayPath(fileInfo.FullName)}'를 UTF-8로 변환하시겠습니까?\n\n" +
                                   "⚠️ 백업이 생성되지 않습니다. 원본 파일이 덮어씌워집니다.";

            if (!EditorUtility.DisplayDialog("변환 확인 (백업 없음)", dialogMessage, "변환", "취소"))
            {
                return;
            }

            var success = EncodingConverter.ConvertToUtf8WithoutBackup(fileInfo.FullName, out var detectedEncoding);

            if (success)
            {
                EditorUtility.DisplayDialog("변환 완료",
                    $"파일이 UTF-8로 변환되었습니다.\n감지된 인코딩: {detectedEncoding}\n\n(백업 없음)", "확인");

                OnScanClicked();
                AssetDatabase.Refresh();
            }
            else
            {
                EditorUtility.DisplayDialog("변환 실패", "파일 변환 중 오류가 발생했습니다.", "확인");
            }
        }

        private async void OnConvertAllClicked()
        {
            if (isProcessing)
                return;

            if (ansiFiles.Count == 0)
                return;

            bool enableBackup = enableBackupToggle != null && enableBackupToggle.value;

            if (enableBackup)
            {
                await ConvertAllWithBackupAsync();
            }
            else
            {
                await ConvertAllWithoutBackupAsync();
            }
        }

        /// <summary>전체 파일 변환 (백업 포함)</summary>
        private async Task ConvertAllWithBackupAsync()
        {
            // 기존 백업 확인
            var backupDir = Path.Combine(currentRootPath, "Backup");
            var manifestPath = Path.Combine(backupDir, "backup_manifest.json");

            BackupManifest existingManifest = null;

            string dialogMessage = $"{ansiFiles.Count}개 파일을 UTF-8로 변환하시겠습니까?\n\n";

            if (File.Exists(manifestPath))
            {
                existingManifest = EncodingConverter.LoadManifest(manifestPath);
                if (existingManifest != null && File.Exists(existingManifest.backupDataFile))
                {
                    var backupSize = new FileInfo(existingManifest.backupDataFile).Length / 1024.0;
                    dialogMessage += $"기존 백업이 발견되었습니다.\n";
                    dialogMessage += $"백업 파일: {Path.GetFileName(existingManifest.backupDataFile)}\n";
                    dialogMessage += $"기존 기록: {existingManifest.conversions.Count}개 파일 ({backupSize:F2} KB)\n\n";
                    dialogMessage += "새로운 변환 내용이 기존 백업에 병합됩니다.\n";
                    dialogMessage += "중복된 파일은 자동으로 건너뜁니다.";
                }
                else
                {
                    dialogMessage += "원본은 새로운 바이너리 파일로 백업됩니다.\n";
                    dialogMessage += "한글 인코딩(EUC-KR)이 자동으로 감지됩니다.";
                }
            }
            else
            {
                dialogMessage += "원본은 새로운 바이너리 파일로 백업됩니다.\n";
                dialogMessage += "한글 인코딩(EUC-KR)이 자동으로 감지됩니다.";
            }

            if (!EditorUtility.DisplayDialog("일괄 변환 확인", dialogMessage, "변환", "취소"))
            {
                return;
            }

            isProcessing = true;
            SetUIEnabled(false);
            ShowProgress(true);

            cancellationTokenSource?.Cancel();
            cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var filesToConvert = new List<FileInfo>(ansiFiles);
                var rootPath = currentRootPath;

                var result = await Task.Run(() =>
                {
                    var success = EncodingConverter.ConvertMultipleToUtf8(
                        filesToConvert,
                        rootPath,
                        out var manifest,
                        UpdateProgress
                    );
                    return (success, manifest);
                }, cancellationTokenSource.Token);

                if (result.success && result.manifest.conversions.Count > 0)
                {
                    EncodingConverter.SaveManifest(currentRootPath, result.manifest);

                    var backupSize = new FileInfo(result.manifest.backupDataFile).Length / 1024.0;

                    EditorApplication.delayCall += () =>
                    {
                        var newConversions = result.manifest.conversions.Count(c =>
                            c.convertedAt > System.DateTime.Now.AddMinutes(-1));

                        string resultMessage = File.Exists(manifestPath) && existingManifest != null
                            ? $"기존 백업에 병합 완료!\n\n" +
                              $"이번 변환: {newConversions}개 파일\n" +
                              $"전체 기록: {result.manifest.conversions.Count}개 파일\n\n" +
                              $"백업 파일: {Path.GetFileName(result.manifest.backupDataFile)}\n" +
                              $"백업 크기: {backupSize:F2} KB"
                            : $"{result.manifest.conversions.Count}개 파일이 변환되었습니다.\n\n" +
                              $"백업 파일: {Path.GetFileName(result.manifest.backupDataFile)}\n" +
                              $"백업 크기: {backupSize:F2} KB";

                        EditorUtility.DisplayDialog("변환 완료", resultMessage, "확인");

                        statusLabel.text = $"✓ 변환 완료 (전체: {result.manifest.conversions.Count}개, 백업: {backupSize:F2} KB)";
                        statusLabel.style.color = new Color(0.2f, 0.8f, 0.2f);

                        // 생성된 매니페스트 자동 로드
                        if (File.Exists(manifestPath))
                        {
                            manifestPathField.value = manifestPath;
                            LoadManifest(manifestPath);
                        }

                        OnScanClicked();
                        AssetDatabase.Refresh();
                    };
                }
                else
                {
                    EditorApplication.delayCall += () =>
                    {
                        EditorUtility.DisplayDialog("변환 실패", "파일 변환 중 오류가 발생했습니다.", "확인");
                    };
                }
            }
            catch (System.OperationCanceledException)
            {
                statusLabel.text = "변환이 취소되었습니다.";
                statusLabel.style.color = new Color(1.0f, 0.6f, 0.0f);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"변환 오류: {ex.Message}");
                statusLabel.text = "변환 중 오류가 발생했습니다.";
                statusLabel.style.color = new Color(1.0f, 0.3f, 0.3f);
            }
            finally
            {
                isProcessing = false;
                SetUIEnabled(true);
                ShowProgress(false);
            }
        }

        /// <summary>전체 파일 변환 (백업 없음)</summary>
        private async Task ConvertAllWithoutBackupAsync()
        {
            string dialogMessage = $"{ansiFiles.Count}개 파일을 UTF-8로 변환하시겠습니까?\n\n" +
                                   "⚠️ 백업이 생성되지 않습니다.\n" +
                                   "원본 파일이 모두 덮어씌워집니다.\n\n" +
                                   "한글 인코딩(EUC-KR)이 자동으로 감지됩니다.";

            if (!EditorUtility.DisplayDialog("일괄 변환 확인 (백업 없음)", dialogMessage, "변환", "취소"))
            {
                return;
            }

            isProcessing = true;
            SetUIEnabled(false);
            ShowProgress(true);

            cancellationTokenSource?.Cancel();
            cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var filesToConvert = new List<FileInfo>(ansiFiles);

                var convertedCount = await Task.Run(() =>
                {
                    return EncodingConverter.ConvertMultipleToUtf8WithoutBackup(
                        filesToConvert,
                        UpdateProgress
                    );
                }, cancellationTokenSource.Token);

                EditorApplication.delayCall += () =>
                {
                    if (convertedCount > 0)
                    {
                        EditorUtility.DisplayDialog("변환 완료",
                            $"{convertedCount}개 파일이 변환되었습니다.\n\n(백업 없음)", "확인");

                        statusLabel.text = $"✓ 변환 완료: {convertedCount}개 파일 (백업 없음)";
                        statusLabel.style.color = new Color(0.2f, 0.8f, 0.2f);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("변환 실패", "파일 변환 중 오류가 발생했습니다.", "확인");
                    }

                    OnScanClicked();
                    AssetDatabase.Refresh();
                };
            }
            catch (System.OperationCanceledException)
            {
                statusLabel.text = "변환이 취소되었습니다.";
                statusLabel.style.color = new Color(1.0f, 0.6f, 0.0f);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"변환 오류: {ex.Message}");
                statusLabel.text = "변환 중 오류가 발생했습니다.";
                statusLabel.style.color = new Color(1.0f, 0.3f, 0.3f);
            }
            finally
            {
                isProcessing = false;
                SetUIEnabled(true);
                ShowProgress(false);
            }
        }

        private void OnBrowseManifestClicked()
        {
            var startPath = !string.IsNullOrEmpty(currentRootPath)
                ? Path.Combine(currentRootPath, "Backup")
                : Application.dataPath;

            var path = EditorUtility.OpenFilePanel("매니페스트 파일 선택", startPath, "json");

            if (!string.IsNullOrEmpty(path))
            {
                manifestPathField.value = path;
                LoadManifest(path);
                SaveManifestPath(path);
            }
        }

        private void OnClearManifestClicked()
        {
            if (EditorUtility.DisplayDialog("백업 초기화",
                "저장된 백업 경로를 초기화하시겠습니까?",
                "초기화", "취소"))
            {
                EditorPrefs.DeleteKey(PREF_MANIFEST_PATH);
                manifestPathField.value = string.Empty;
                currentManifest = null;
                currentManifestPath = null;

                verifyButton.SetEnabled(false);
                restoreButton.SetEnabled(false);
                deleteButton.SetEnabled(false);
                backupInfoLabel.text = "";

                statusLabel.text = "백업 경로가 초기화되었습니다.";
                statusLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            }
        }

        private void LoadManifest(string path)
        {
            currentManifestPath = path;
            currentManifest = EncodingConverter.LoadManifest(path);

            if (currentManifest != null)
            {
                verifyButton.SetEnabled(true);
                restoreButton.SetEnabled(true);
                deleteButton.SetEnabled(true);

                var backupSize = File.Exists(currentManifest.backupDataFile)
                    ? new FileInfo(currentManifest.backupDataFile).Length / 1024.0
                    : 0;

                var latestConversion = currentManifest.conversions
                    .OrderByDescending(c => c.convertedAt)
                    .FirstOrDefault();

                var infoText = $"백업 파일: {Path.GetFileName(currentManifest.backupDataFile)} ({backupSize:F2} KB)\n" +
                              $"변환 기록: {currentManifest.conversions.Count}개 파일";

                if (latestConversion != null)
                {
                    infoText += $"\n최근 변환: {latestConversion.convertedAt:yyyy-MM-dd HH:mm:ss}";
                }

                backupInfoLabel.text = infoText;

                statusLabel.text = $"✓ 매니페스트 로드됨: {currentManifest.conversions.Count}개 변환 기록";
                statusLabel.style.color = new Color(0.2f, 0.6f, 1.0f);

                SaveManifestPath(path);
            }
            else
            {
                verifyButton.SetEnabled(false);
                restoreButton.SetEnabled(false);
                deleteButton.SetEnabled(false);
                backupInfoLabel.text = "";
                statusLabel.text = "매니페스트 로드 실패";
                statusLabel.style.color = new Color(1.0f, 0.3f, 0.3f);
            }
        }

        private void OnVerifyClicked()
        {
            if (currentManifest == null)
                return;

            var isValid = EncodingConverter.VerifyBackup(currentManifest);

            if (isValid)
            {
                EditorUtility.DisplayDialog("검증 성공", "백업 데이터가 유효합니다.", "확인");
                statusLabel.text = "✓ 백업 검증 완료";
                statusLabel.style.color = new Color(0.2f, 0.8f, 0.2f);
            }
            else
            {
                EditorUtility.DisplayDialog("검증 실패", "백업 데이터가 손상되었거나 유효하지 않습니다.", "확인");
                statusLabel.text = "✗ 백업 검증 실패";
                statusLabel.style.color = new Color(1.0f, 0.3f, 0.3f);
            }
        }

        private void OnRestoreClicked()
        {
            if (currentManifest == null)
                return;

            if (!EditorUtility.DisplayDialog("복원 확인",
                $"{currentManifest.conversions.Count}개 파일을 바이너리 백업에서 복원하시겠습니까?\n\n변환된 UTF-8 파일은 원본 ANSI로 복원됩니다.",
                "복원", "취소"))
            {
                return;
            }

            var success = EncodingConverter.RestoreBackup(currentManifest);

            if (success)
            {
                EditorUtility.DisplayDialog("복원 완료", "바이너리 백업에서 파일이 복원되었습니다.", "확인");
                statusLabel.text = "✓ 백업 복원 완료";
                statusLabel.style.color = new Color(0.2f, 0.8f, 0.2f);
                AssetDatabase.Refresh();
            }
            else
            {
                EditorUtility.DisplayDialog("복원 실패", "파일 복원 중 오류가 발생했습니다.", "확인");
                statusLabel.text = "✗ 복원 실패";
                statusLabel.style.color = new Color(1.0f, 0.3f, 0.3f);
            }
        }

        private void OnDeleteClicked()
        {
            if (currentManifest == null || string.IsNullOrEmpty(currentManifestPath))
                return;

            if (!EditorUtility.DisplayDialog("백업 삭제 확인",
                "바이너리 백업 파일 및 매니페스트를 삭제하시겠습니까?\n\n이 작업은 되돌릴 수 없습니다.",
                "삭제", "취소"))
            {
                return;
            }

            var success = EncodingConverter.DeleteBackup(currentManifest, currentManifestPath);

            if (success)
            {
                manifestPathField.value = string.Empty;
                currentManifest = null;
                currentManifestPath = null;
                verifyButton.SetEnabled(false);
                restoreButton.SetEnabled(false);
                deleteButton.SetEnabled(false);
                backupInfoLabel.text = "";

                SaveManifestPath(null);

                EditorUtility.DisplayDialog("삭제 완료", "백업이 삭제되었습니다.", "확인");
                statusLabel.text = "✓ 백업 삭제 완료";
                statusLabel.style.color = new Color(0.2f, 0.8f, 0.2f);
                AssetDatabase.Refresh();
            }
            else
            {
                EditorUtility.DisplayDialog("삭제 실패", "백업 삭제 중 오류가 발생했습니다.", "확인");
                statusLabel.text = "✗ 삭제 실패";
                statusLabel.style.color = new Color(1.0f, 0.3f, 0.3f);
            }
        }
    }
}