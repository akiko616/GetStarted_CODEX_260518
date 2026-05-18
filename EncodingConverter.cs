using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace EncodingTools
{
    /// <summary>파일 인코딩 변환 정보</summary>
    [Serializable]
    public class ConversionInfo
    {
        public string originalPath;
        public string relativePath;
        public long backupOffset;
        public long backupLength;
        public string originalClassName;
        public string backupClassName;
        public DateTime convertedAt;
        public string detectedEncoding;
    }

    /// <summary>백업 정보 컨테이너</summary>
    [Serializable]
    public class BackupManifest
    {
        public string backupDataFile;
        public List<ConversionInfo> conversions = new List<ConversionInfo>();
    }

    /// <summary>진행 상황 정보</summary>
    public class ProgressInfo
    {
        public int current;
        public int total;
        public string message;
        public float progress => total > 0 ? (float)current / total : 0f;
    }

    /// <summary>파일 인코딩 변환 유틸리티</summary>
    public static class EncodingConverter
    {
        private static readonly string[] TargetExtensions = { ".cs", ".md", ".txt" };

        // 한국어 ANSI 코드 페이지 (EUC-KR)
        private static readonly Encoding KoreanEncoding = Encoding.GetEncoding(949);

        // Windows ANSI 코드 페이지
        private static readonly Encoding WindowsEncoding = Encoding.GetEncoding(1252);

        private static FileInfo[] GetTargetFiles(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
                return new FileInfo[0];

            var directoryInfo = new DirectoryInfo(rootPath);
            return directoryInfo.GetFiles("*.*", SearchOption.AllDirectories)
                .Where(f => TargetExtensions.Contains(f.Extension.ToLower()))
                .ToArray();
        }
        /// <summary>디렉토리에서 ANSI 인코딩 파일을 검색합니다</summary>
        public static List<FileInfo> FindAnsiFiles(string rootPath, Action<ProgressInfo> onProgress = null)
        {
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
            {
                return new List<FileInfo>();
            }

            var ansiFiles = new List<FileInfo>();
            var directoryInfo = new DirectoryInfo(rootPath);

            try
            {
                var allFiles = directoryInfo.GetFiles("*.*", SearchOption.AllDirectories)
                    .Where(f => TargetExtensions.Contains(f.Extension.ToLower()))
                    .ToList();

                onProgress?.Invoke(new ProgressInfo
                {
                    current = 0,
                    total = allFiles.Count,
                    message = "파일 검색 시작..."
                });

                for (int i = 0; i < allFiles.Count; i++)
                {
                    var file = allFiles[i];

                    onProgress?.Invoke(new ProgressInfo
                    {
                        current = i + 1,
                        total = allFiles.Count,
                        message = $"검사 중: {file.Name}"
                    });

                    if (IsAnsiEncoded(file.FullName))
                    {
                        ansiFiles.Add(file);
                    }
                }

                onProgress?.Invoke(new ProgressInfo
                {
                    current = allFiles.Count,
                    total = allFiles.Count,
                    message = $"검색 완료: {ansiFiles.Count}개 발견"
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EncodingConverter] 파일 검색 실패: {ex.Message}");
            }

            return ansiFiles;
        }

        /// <summary>파일이 ANSI 인코딩인지 확인합니다</summary>
        public static bool IsAnsiEncoded(string filePath)
        {
            try
            {
                var bytes = File.ReadAllBytes(filePath);

                if (bytes.Length == 0)
                    return false;

                // BOM 확인 - UTF-8/UTF-16이면 ANSI 아님
                if (HasUtfBom(bytes))
                    return false;

                // UTF-8 유효성 검사
                if (IsValidUtf8(bytes))
                    return false;

                // ANSI 특수문자 또는 한글 범위 확인
                return ContainsAnsiOrKoreanCharacters(bytes);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EncodingConverter] 인코딩 감지 실패 ({filePath}): {ex.Message}");
                return false;
            }
        }

        /// <summary>UTF BOM을 가지고 있는지 확인</summary>
        private static bool HasUtfBom(byte[] bytes)
        {
            if (bytes.Length >= 3)
            {
                // UTF-8 BOM
                if (bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                    return true;
            }

            if (bytes.Length >= 2)
            {
                // UTF-16 LE BOM
                if (bytes[0] == 0xFF && bytes[1] == 0xFE)
                    return true;

                // UTF-16 BE BOM
                if (bytes[0] == 0xFE && bytes[1] == 0xFF)
                    return true;
            }

            return false;
        }

        /// <summary>유효한 UTF-8 인코딩인지 확인</summary>
        private static bool IsValidUtf8(byte[] bytes)
        {
            try
            {
                var decoder = Encoding.UTF8.GetDecoder();
                decoder.Fallback = DecoderFallback.ExceptionFallback;

                var chars = new char[decoder.GetCharCount(bytes, 0, bytes.Length)];
                decoder.GetChars(bytes, 0, bytes.Length, chars, 0);

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>ANSI 또는 한글 문자를 포함하는지 확인</summary>
        private static bool ContainsAnsiOrKoreanCharacters(byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                // 0x80-0xFF 영역은 확장 ASCII/ANSI
                if (bytes[i] >= 0x80)
                {
                    // 한글 EUC-KR 범위 확인 (2바이트)
                    if (i + 1 < bytes.Length)
                    {
                        var firstByte = bytes[i];
                        var secondByte = bytes[i + 1];

                        // 한글 완성형: 0xB0A1-0xC8FE, 0xA1A1-0xFEFE
                        if ((firstByte >= 0xB0 && firstByte <= 0xC8) ||
                            (firstByte >= 0xA1 && firstByte <= 0xFE && secondByte >= 0xA1 && secondByte <= 0xFE))
                        {
                            return true;
                        }
                    }

                    return true;
                }
            }

            return false;
        }

        /// <summary>파일의 실제 인코딩을 감지합니다</summary>
        private static Encoding DetectEncoding(byte[] bytes)
        {
            // 한글 문자 감지
            for (int i = 0; i < bytes.Length - 1; i++)
            {
                if (bytes[i] >= 0xB0 && bytes[i] <= 0xC8)
                {
                    return KoreanEncoding; // EUC-KR
                }
            }

            return WindowsEncoding; // Windows-1252
        }

        /// <summary>여러 파일을 UTF-8로 변환하고 바이너리 백업을 생성합니다</summary>
        public static bool ConvertMultipleToUtf8(List<FileInfo> files, string backupRoot, out BackupManifest manifest, Action<ProgressInfo> onProgress = null)
        {
            manifest = new BackupManifest();

            try
            {
                var backupDir = Path.Combine(backupRoot, "Backup");
                Directory.CreateDirectory(backupDir);

                var manifestPath = Path.Combine(backupDir, "backup_manifest.json");
                string backupDataFile;

                // 기존 매니페스트 확인
                if (File.Exists(manifestPath))
                {
                    var existingManifest = LoadManifest(manifestPath);

                    if (existingManifest != null &&
                        !string.IsNullOrEmpty(existingManifest.backupDataFile) &&
                        File.Exists(existingManifest.backupDataFile))
                    {
                        // 기존 백업 병합 모드
                        Debug.Log($"[EncodingConverter] 기존 백업 발견: {existingManifest.backupDataFile}");
                        manifest = existingManifest;
                        backupDataFile = existingManifest.backupDataFile;
                    }
                    else
                    {
                        // 새 백업 파일 생성
                        backupDataFile = Path.Combine(backupDir, $"backup_data_{DateTime.Now:yyyyMMdd_HHmmss}.bin");
                        manifest.backupDataFile = backupDataFile;
                    }
                }
                else
                {
                    // 새 백업 파일 생성
                    backupDataFile = Path.Combine(backupDir, $"backup_data_{DateTime.Now:yyyyMMdd_HHmmss}.bin");
                    manifest.backupDataFile = backupDataFile;
                }

                onProgress?.Invoke(new ProgressInfo
                {
                    current = 0,
                    total = files.Count,
                    message = "변환 시작..."
                });

                // 백업 파일에 추가 (Append 모드로 병합)
                using (var backupStream = new FileStream(backupDataFile, FileMode.Append, FileAccess.Write))
                using (var writer = new BinaryWriter(backupStream))
                {
                    for (int i = 0; i < files.Count; i++)
                    {
                        var file = files[i];

                        onProgress?.Invoke(new ProgressInfo
                        {
                            current = i,
                            total = files.Count,
                            message = $"변환 중: {file.Name}"
                        });

                        // 이미 백업된 파일인지 확인
                        var existingInfo = manifest.conversions.FirstOrDefault(c => c.originalPath == file.FullName);

                        if (existingInfo != null)
                        {
                            Debug.Log($"[EncodingConverter] 이미 백업된 파일 건너뜀: {file.Name}");
                            continue;
                        }

                        if (ConvertSingleFile(file.FullName, backupRoot, writer, out var info))
                        {
                            manifest.conversions.Add(info);
                        }
                    }
                }

                onProgress?.Invoke(new ProgressInfo
                {
                    current = files.Count,
                    total = files.Count,
                    message = $"변환 완료: {manifest.conversions.Count}개"
                });

                return manifest.conversions.Count > 0;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EncodingConverter] 일괄 변환 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>단일 파일을 UTF-8로 변환합니다</summary>
        public static bool ConvertToUtf8(string filePath, string backupRoot, out ConversionInfo info)
        {
            info = null;

            try
            {
                var backupDir = Path.Combine(backupRoot, "Backup");
                Directory.CreateDirectory(backupDir);

                // 기존 매니페스트 로드
                var manifestPath = Path.Combine(backupDir, "backup_manifest.json");
                BackupManifest manifest;
                string backupDataFile;

                if (File.Exists(manifestPath))
                {
                    manifest = LoadManifest(manifestPath);

                    if (manifest != null)
                    {
                        // 이미 백업된 파일인지 확인
                        var existingInfo = manifest.conversions.FirstOrDefault(c => c.originalPath == filePath);

                        if (existingInfo != null)
                        {
                            Debug.LogWarning($"[EncodingConverter] 이미 백업된 파일입니다: {Path.GetFileName(filePath)}");
                            info = existingInfo;
                            return false;
                        }

                        if (!string.IsNullOrEmpty(manifest.backupDataFile) && File.Exists(manifest.backupDataFile))
                        {
                            // 기존 백업 파일 사용
                            backupDataFile = manifest.backupDataFile;
                        }
                        else
                        {
                            // 새 백업 파일 생성
                            backupDataFile = Path.Combine(backupDir, $"backup_data_{DateTime.Now:yyyyMMdd_HHmmss}.bin");
                            manifest.backupDataFile = backupDataFile;
                        }
                    }
                    else
                    {
                        // 매니페스트 로드 실패 - 새로 생성
                        backupDataFile = Path.Combine(backupDir, $"backup_data_{DateTime.Now:yyyyMMdd_HHmmss}.bin");
                        manifest = new BackupManifest { backupDataFile = backupDataFile };
                    }
                }
                else
                {
                    // 새 매니페스트 및 백업 파일 생성
                    backupDataFile = Path.Combine(backupDir, $"backup_data_{DateTime.Now:yyyyMMdd_HHmmss}.bin");
                    manifest = new BackupManifest { backupDataFile = backupDataFile };
                }

                // 백업 파일에 추가 (append 모드)
                using (var backupStream = new FileStream(backupDataFile, FileMode.Append, FileAccess.Write))
                using (var writer = new BinaryWriter(backupStream))
                {
                    if (ConvertSingleFile(filePath, backupRoot, writer, out info))
                    {
                        manifest.conversions.Add(info);
                        SaveManifest(backupRoot, manifest);
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EncodingConverter] 변환 실패 ({filePath}): {ex.Message}");
                return false;
            }
        }

        /// <summary>단일 파일 변환 (내부용)</summary>
        private static bool ConvertSingleFile(string filePath, string backupRoot, BinaryWriter backupWriter, out ConversionInfo info)
        {
            info = null;

            try
            {
                var fileInfo = new FileInfo(filePath);
                var originalBytes = File.ReadAllBytes(filePath);

                // 인코딩 감지
                var detectedEncoding = DetectEncoding(originalBytes);

                // ANSI → UTF-8 변환
                var content = detectedEncoding.GetString(originalBytes);

                // CS 파일인 경우 클래스명 추출
                string originalClassName = null;
                string backupClassName = null;

                if (fileInfo.Extension.ToLower() == ".cs")
                {
                    originalClassName = Path.GetFileNameWithoutExtension(fileInfo.Name);
                    backupClassName = originalClassName + "_Backup";
                }

                // 백업 데이터 저장 (바이너리)
                var backupOffset = backupWriter.BaseStream.Position;

                // 파일 정보 헤더: 원본 바이트 길이 + 원본 바이트
                backupWriter.Write(originalBytes.Length);
                backupWriter.Write(originalBytes);

                var backupLength = backupWriter.BaseStream.Position - backupOffset;

                // UTF-8로 변환하여 원본 경로에 저장
                File.WriteAllText(filePath, content, new UTF8Encoding(true)); // BOM 포함

                // 상대 경로 계산
                var relativePath = GetRelativePath(backupRoot, filePath);

                // 변환 정보 생성
                info = new ConversionInfo
                {
                    originalPath = filePath,
                    relativePath = relativePath,
                    backupOffset = backupOffset,
                    backupLength = backupLength,
                    originalClassName = originalClassName,
                    backupClassName = backupClassName,
                    convertedAt = DateTime.Now,
                    detectedEncoding = detectedEncoding.WebName
                };

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EncodingConverter] 파일 변환 실패 ({filePath}): {ex.Message}");
                return false;
            }
        }



        /// <summary>백업 매니페스트를 저장합니다</summary>
        public static void SaveManifest(string rootPath, BackupManifest manifest)
        {
            var manifestPath = Path.Combine(rootPath, "Backup", "backup_manifest.json");
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath));

            var json = JsonUtility.ToJson(manifest, true);
            File.WriteAllText(manifestPath, json, Encoding.UTF8);
        }

        /// <summary>백업 매니페스트를 로드합니다</summary>
        public static BackupManifest LoadManifest(string manifestPath)
        {
            try
            {
                if (!File.Exists(manifestPath))
                    return null;

                var json = File.ReadAllText(manifestPath, Encoding.UTF8);
                return JsonUtility.FromJson<BackupManifest>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EncodingConverter] 매니페스트 로드 실패: {ex.Message}");
                return null;
            }
        }

        /// <summary>바이너리 백업에서 복원합니다</summary>
        public static bool RestoreBackup(BackupManifest manifest)
        {
            try
            {
                if (!File.Exists(manifest.backupDataFile))
                {
                    Debug.LogError($"[EncodingConverter] 백업 파일 없음: {manifest.backupDataFile}");
                    return false;
                }

                using (var backupStream = new FileStream(manifest.backupDataFile, FileMode.Open, FileAccess.Read))
                using (var reader = new BinaryReader(backupStream))
                {
                    foreach (var info in manifest.conversions)
                    {
                        // 백업 데이터 위치로 이동
                        backupStream.Seek(info.backupOffset, SeekOrigin.Begin);

                        // 원본 데이터 읽기
                        var dataLength = reader.ReadInt32();
                        var originalBytes = reader.ReadBytes(dataLength);

                        // 변환된 파일 삭제
                        if (File.Exists(info.originalPath))
                        {
                            File.Delete(info.originalPath);
                        }

                        // 원본 복원 (ANSI 그대로)
                        File.WriteAllBytes(info.originalPath, originalBytes);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EncodingConverter] 복원 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>백업 파일 및 매니페스트를 삭제합니다</summary>
        public static bool DeleteBackup(BackupManifest manifest, string manifestPath)
        {
            try
            {
                // 백업 데이터 파일 삭제
                if (!string.IsNullOrEmpty(manifest.backupDataFile) && File.Exists(manifest.backupDataFile))
                {
                    File.Delete(manifest.backupDataFile);
                }

                // 매니페스트 삭제
                if (File.Exists(manifestPath))
                {
                    File.Delete(manifestPath);
                }

                // 빈 백업 디렉토리 정리
                var backupDir = Path.GetDirectoryName(manifestPath);
                if (Directory.Exists(backupDir) && !Directory.EnumerateFileSystemEntries(backupDir).Any())
                {
                    Directory.Delete(backupDir, true);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EncodingConverter] 백업 삭제 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>백업 데이터 무결성 검증</summary>
        public static bool VerifyBackup(BackupManifest manifest)
        {
            try
            {
                if (!File.Exists(manifest.backupDataFile))
                    return false;

                using (var backupStream = new FileStream(manifest.backupDataFile, FileMode.Open, FileAccess.Read))
                {
                    foreach (var info in manifest.conversions)
                    {
                        // 오프셋이 파일 범위 내인지 확인
                        if (info.backupOffset < 0 || info.backupOffset >= backupStream.Length)
                            return false;

                        // 데이터 길이가 유효한지 확인
                        if (info.backupLength <= 0 || info.backupOffset + info.backupLength > backupStream.Length)
                            return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EncodingConverter] 백업 검증 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>여러 파일을 UTF-8로 변환합니다 (백업 없음)</summary>
        public static int ConvertMultipleToUtf8WithoutBackup(List<FileInfo> files, Action<ProgressInfo> onProgress = null)
        {
            int convertedCount = 0;

            try
            {
                onProgress?.Invoke(new ProgressInfo
                {
                    current = 0,
                    total = files.Count,
                    message = "변환 시작..."
                });

                for (int i = 0; i < files.Count; i++)
                {
                    var file = files[i];

                    onProgress?.Invoke(new ProgressInfo
                    {
                        current = i,
                        total = files.Count,
                        message = $"변환 중: {file.Name}"
                    });

                    if (ConvertSingleFileWithoutBackup(file.FullName, out var detectedEncoding))
                    {
                        convertedCount++;
                        Debug.Log($"[EncodingConverter] 변환 완료 (백업 없음): {file.Name} ({detectedEncoding})");
                    }
                }

                onProgress?.Invoke(new ProgressInfo
                {
                    current = files.Count,
                    total = files.Count,
                    message = $"변환 완료: {convertedCount}개"
                });

                return convertedCount;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EncodingConverter] 일괄 변환 실패 (백업 없음): {ex.Message}");
                return convertedCount;
            }
        }

        /// <summary>단일 파일을 UTF-8로 변환합니다 (백업 없음)</summary>
        public static bool ConvertToUtf8WithoutBackup(string filePath, out string detectedEncodingName)
        {
            detectedEncodingName = null;
            return ConvertSingleFileWithoutBackup(filePath, out detectedEncodingName);
        }

        /// <summary>단일 파일 변환 (백업 없음, 내부용)</summary>
        private static bool ConvertSingleFileWithoutBackup(string filePath, out string detectedEncodingName)
        {
            detectedEncodingName = null;

            try
            {
                var originalBytes = File.ReadAllBytes(filePath);

                // 인코딩 감지
                var detectedEncoding = DetectEncoding(originalBytes);
                detectedEncodingName = detectedEncoding.WebName;

                // ANSI → UTF-8 변환
                var content = detectedEncoding.GetString(originalBytes);

                // UTF-8로 변환하여 원본 경로에 저장
                File.WriteAllText(filePath, content, new UTF8Encoding(true)); // BOM 포함

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EncodingConverter] 파일 변환 실패 (백업 없음) ({filePath}): {ex.Message}");
                return false;
            }
        }

        #region 깨진 한글 복원

        /// <summary>깨진 한글 파일 정보</summary>
        [Serializable]
        public class BrokenKoreanFileInfo
        {
            public string fullPath;
            public string relativePath;
            public string sampleBroken;   // 깨진 텍스트 샘플
            public string sampleFixed;    // 복원된 텍스트 샘플
            public int brokenCharCount;   // 깨진 문자 수
        }

        /// <summary>디렉토리에서 깨진 한글 파일을 검색합니다</summary>
        public static List<BrokenKoreanFileInfo> ScanBrokenKoreanFiles(string rootPath, Action<ProgressInfo> onProgress = null)
        {
            var result = new List<BrokenKoreanFileInfo>();

            if (!Directory.Exists(rootPath))
                return result;

            var files = GetTargetFiles(rootPath);
            int processed = 0;

            foreach (var file in files)
            {
                onProgress?.Invoke(new ProgressInfo
                {
                    current = processed,
                    total = files.Length,
                    message = $"검사 중: {file.Name}"
                });

                var info = CheckBrokenKorean(file.FullName, rootPath);
                if (info != null)
                {
                    result.Add(info);
                }

                processed++;
            }

            onProgress?.Invoke(new ProgressInfo
            {
                current = files.Length,
                total = files.Length,
                message = $"검사 완료: {result.Count}개 발견"
            });

            return result;
        }

        /// <summary>파일이 깨진 한글을 포함하는지 검사합니다</summary>
        public static BrokenKoreanFileInfo CheckBrokenKorean(string filePath, string rootPath = null)
        {
            try
            {
                var bytes = File.ReadAllBytes(filePath);

                // UTF-8 BOM 확인 (EF BB BF)
                bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

                // BOM이 없으면 ANSI 파일일 가능성 → 깨진 한글 아님
                if (!hasBom)
                    return null;

                // UTF-8로 읽기
                var content = Encoding.UTF8.GetString(bytes);

                // 깨진 한글 패턴 감지
                // EUC-KR 바이트가 UTF-8로 잘못 해석되면 Latin-1 Supplement 문자들이 나타남
                // 예: 0xBE 0xC8 (안) → UTF-8에서 0xC2 0xBE 0xC3 0x88 로 저장됨
                int brokenCount = 0;
                var brokenPatterns = new List<(int start, int length)>();

                for (int i = 0; i < content.Length; i++)
                {
                    char c = content[i];

                    // Latin-1 Supplement 범위 (U+0080 ~ U+00FF) 중 
                    // 특히 한글 EUC-KR 범위와 겹치는 부분 (0x80-0xFF)
                    if (c >= 0x80 && c <= 0xFF)
                    {
                        brokenCount++;

                        // 연속된 패턴 기록
                        if (brokenPatterns.Count == 0 || 
                            brokenPatterns[brokenPatterns.Count - 1].start + brokenPatterns[brokenPatterns.Count - 1].length < i)
                        {
                            brokenPatterns.Add((i, 1));
                        }
                        else
                        {
                            var last = brokenPatterns[brokenPatterns.Count - 1];
                            brokenPatterns[brokenPatterns.Count - 1] = (last.start, i - last.start + 1);
                        }
                    }
                }

                // 깨진 문자가 일정 수 이상이면 깨진 파일로 판단
                // (최소 2개 이상의 연속된 Latin-1 Supplement 문자)
                if (brokenCount < 2)
                    return null;

                // 복원 시도하여 유효한 한글인지 확인
                var fixedContent = TryFixBrokenKorean(content);
                if (fixedContent == null || fixedContent == content)
                    return null;

                // 실제로 한글이 복원되었는지 확인
                int koreanCount = CountKoreanChars(fixedContent);
                if (koreanCount < 2)
                    return null;

                // 샘플 추출
                string sampleBroken = "";
                string sampleFixed = "";

                if (brokenPatterns.Count > 0)
                {
                    var firstPattern = brokenPatterns[0];
                    int sampleStart = Math.Max(0, firstPattern.start - 10);
                    int sampleEnd = Math.Min(content.Length, firstPattern.start + firstPattern.length + 10);
                    sampleBroken = content.Substring(sampleStart, sampleEnd - sampleStart);

                    if (sampleStart < fixedContent.Length && sampleEnd <= fixedContent.Length)
                    {
                        // 복원된 내용에서 동일 위치의 샘플 추출
                        // 길이가 다를 수 있으므로 비율로 계산
                        float ratio = (float)fixedContent.Length / content.Length;
                        int fixedStart = (int)(sampleStart * ratio);
                        int fixedEnd = Math.Min(fixedContent.Length, (int)(sampleEnd * ratio));
                        if (fixedEnd > fixedStart)
                        {
                            sampleFixed = fixedContent.Substring(fixedStart, fixedEnd - fixedStart);
                        }
                    }
                }

                // 샘플 길이 제한
                if (sampleBroken.Length > 50) sampleBroken = sampleBroken.Substring(0, 50) + "...";
                if (sampleFixed.Length > 50) sampleFixed = sampleFixed.Substring(0, 50) + "...";

                return new BrokenKoreanFileInfo
                {
                    fullPath = filePath,
                    relativePath = !string.IsNullOrEmpty(rootPath) 
                        ? GetRelativePath(filePath, rootPath) 
                        : Path.GetFileName(filePath),
                    sampleBroken = sampleBroken,
                    sampleFixed = sampleFixed,
                    brokenCharCount = brokenCount
                };
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EncodingConverter] 깨진 한글 검사 실패 ({filePath}): {ex.Message}");
                return null;
            }
        }

        /// <summary>깨진 한글을 복원 시도합니다</summary>
        public static string TryFixBrokenKorean(string brokenContent)
        {
            try
            {
                // 방법 1: UTF-8 문자열 → Latin-1 바이트 → EUC-KR 디코딩
                // UTF-8로 잘못 저장된 EUC-KR 바이트를 복원
                var latin1 = Encoding.GetEncoding("ISO-8859-1");
                var bytes = latin1.GetBytes(brokenContent);

                // EUC-KR로 디코딩 시도
                var fixedContent = KoreanEncoding.GetString(bytes);

                return fixedContent;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>한글 문자 수를 셉니다</summary>
        private static int CountKoreanChars(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            int count = 0;
            foreach (char c in text)
            {
                // 한글 유니코드 범위: AC00-D7AF (가-힣), 1100-11FF (한글 자모)
                if ((c >= 0xAC00 && c <= 0xD7AF) || (c >= 0x1100 && c <= 0x11FF) ||
                    (c >= 0x3130 && c <= 0x318F)) // 한글 호환 자모
                {
                    count++;
                }
            }
            return count;
        }

                /// <summary>상대 경로를 가져옵니다</summary>
        private static string GetRelativePath(string rootPath, string fullPath)
        {
            try
            {
                var rootUri = new Uri(rootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
                var fullUri = new Uri(fullPath);
                var relativeUri = rootUri.MakeRelativeUri(fullUri);
                return Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
            }
            catch
            {
                return fullPath;
            }
        }

        /// <summary>단일 파일의 깨진 한글을 복원합니다</summary>
        public static bool FixBrokenKoreanFile(string filePath, string backupRoot, out ConversionInfo info)
        {
            info = null;

            try
            {
                var bytes = File.ReadAllBytes(filePath);

                // UTF-8 BOM 확인
                bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                if (!hasBom)
                {
                    Debug.LogWarning($"[EncodingConverter] UTF-8 BOM이 없는 파일: {filePath}");
                    return false;
                }

                var content = Encoding.UTF8.GetString(bytes);
                var fixedContent = TryFixBrokenKorean(content);

                if (fixedContent == null || fixedContent == content)
                {
                    Debug.LogWarning($"[EncodingConverter] 복원할 내용 없음: {filePath}");
                    return false;
                }

                // 백업 생성
                var backupDir = Path.Combine(backupRoot, "Backup");
                Directory.CreateDirectory(backupDir);

                var manifestPath = Path.Combine(backupDir, "broken_restore_manifest.json");
                var manifest = File.Exists(manifestPath) 
                    ? LoadBrokenRestoreManifest(manifestPath) 
                    : new BackupManifest();

                // 중복 확인
                if (manifest.conversions.Any(c => c.originalPath == filePath))
                {
                    Debug.Log($"[EncodingConverter] 이미 복원된 파일: {filePath}");
                    return false;
                }

                // 백업 파일 경로
                if (string.IsNullOrEmpty(manifest.backupDataFile))
                {
                    manifest.backupDataFile = Path.Combine(backupDir, $"broken_backup_{DateTime.Now:yyyyMMdd_HHmmss}.bin");
                }

                // 백업 저장
                long offset, length;
                using (var backupStream = new FileStream(manifest.backupDataFile, FileMode.Append, FileAccess.Write))
                {
                    offset = backupStream.Position;
                    backupStream.Write(bytes, 0, bytes.Length);
                    length = bytes.Length;
                }

                // 복원된 내용 저장
                File.WriteAllText(filePath, fixedContent, new UTF8Encoding(true));

                // 정보 기록
                info = new ConversionInfo
                {
                    originalPath = filePath,
                    relativePath = GetRelativePath(filePath, backupRoot),
                    backupOffset = offset,
                    backupLength = length,
                    convertedAt = DateTime.Now,
                    detectedEncoding = "BrokenUTF8-KR"
                };

                manifest.conversions.Add(info);
                SaveBrokenRestoreManifest(manifestPath, manifest);

                Debug.Log($"[EncodingConverter] 깨진 한글 복원 완료: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EncodingConverter] 깨진 한글 복원 실패 ({filePath}): {ex.Message}");
                return false;
            }
        }

        /// <summary>단일 파일의 깨진 한글을 복원합니다 (백업 없음)</summary>
        public static bool FixBrokenKoreanFileWithoutBackup(string filePath)
        {
            try
            {
                var bytes = File.ReadAllBytes(filePath);

                // UTF-8 BOM 확인
                bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                if (!hasBom)
                {
                    Debug.LogWarning($"[EncodingConverter] UTF-8 BOM이 없는 파일: {filePath}");
                    return false;
                }

                var content = Encoding.UTF8.GetString(bytes);
                var fixedContent = TryFixBrokenKorean(content);

                if (fixedContent == null || fixedContent == content)
                {
                    Debug.LogWarning($"[EncodingConverter] 복원할 내용 없음: {filePath}");
                    return false;
                }

                // 복원된 내용 저장
                File.WriteAllText(filePath, fixedContent, new UTF8Encoding(true));

                Debug.Log($"[EncodingConverter] 깨진 한글 복원 완료 (백업 없음): {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EncodingConverter] 깨진 한글 복원 실패 ({filePath}): {ex.Message}");
                return false;
            }
        }

        /// <summary>여러 파일의 깨진 한글을 복원합니다</summary>
        public static int FixMultipleBrokenKoreanFiles(List<BrokenKoreanFileInfo> files, string backupRoot, 
            bool createBackup, Action<ProgressInfo> onProgress = null)
        {
            int fixedCount = 0;

            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];

                onProgress?.Invoke(new ProgressInfo
                {
                    current = i,
                    total = files.Count,
                    message = $"복원 중: {Path.GetFileName(file.fullPath)}"
                });

                bool success;
                if (createBackup)
                {
                    success = FixBrokenKoreanFile(file.fullPath, backupRoot, out _);
                }
                else
                {
                    success = FixBrokenKoreanFileWithoutBackup(file.fullPath);
                }

                if (success)
                {
                    fixedCount++;
                }
            }

            onProgress?.Invoke(new ProgressInfo
            {
                current = files.Count,
                total = files.Count,
                message = $"복원 완료: {fixedCount}개"
            });

            return fixedCount;
        }

        /// <summary>깨진 한글 복원 매니페스트 로드</summary>
        public static BackupManifest LoadBrokenRestoreManifest(string path)
        {
            try
            {
                var json = File.ReadAllText(path);
                return JsonUtility.FromJson<BackupManifest>(json);
            }
            catch
            {
                return new BackupManifest();
            }
        }

        /// <summary>깨진 한글 복원 매니페스트 저장</summary>
        public static void SaveBrokenRestoreManifest(string path, BackupManifest manifest)
        {
            var json = JsonUtility.ToJson(manifest, true);
            File.WriteAllText(path, json);
        }

        /// <summary>깨진 한글 복원을 되돌립니다 (백업에서 복원)</summary>
        public static bool RevertBrokenKoreanFix(string manifestPath, string filePath)
        {
            try
            {
                var manifest = LoadBrokenRestoreManifest(manifestPath);
                if (manifest == null || !File.Exists(manifest.backupDataFile))
                    return false;

                var info = manifest.conversions.FirstOrDefault(c => c.originalPath == filePath);
                if (info == null)
                    return false;

                // 백업에서 원본 데이터 읽기
                byte[] originalBytes;
                using (var stream = new FileStream(manifest.backupDataFile, FileMode.Open, FileAccess.Read))
                {
                    stream.Seek(info.backupOffset, SeekOrigin.Begin);
                    originalBytes = new byte[info.backupLength];
                    stream.Read(originalBytes, 0, originalBytes.Length);
                }

                // 원본 복원
                File.WriteAllBytes(filePath, originalBytes);

                Debug.Log($"[EncodingConverter] 깨진 한글 복원 되돌리기 완료: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EncodingConverter] 되돌리기 실패 ({filePath}): {ex.Message}");
                return false;
            }
        }

        #endregion
    }
}