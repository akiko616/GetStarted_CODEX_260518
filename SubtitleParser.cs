using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace LiveKitStreaming
{
    /// <summary>
    /// 자막 파서 (SRT/VTT 지원)
    /// 자막 파일을 파싱하여 시간 기반 검색 제공
    /// </summary>
    public class SubtitleParser
    {
        #region Properties

        /// <summary>파싱된 자막 항목 목록</summary>
        public List<SubtitleEntry> Entries { get; } = new();

        /// <summary>자막 포맷</summary>
        public SubtitleFormat Format { get; private set; } = SubtitleFormat.Unknown;

        /// <summary>파싱 성공 여부</summary>
        public bool IsValid => Entries.Count > 0;

        #endregion

        #region Regex Patterns

        // SRT: "00:01:23,456 --> 00:01:25,789"
        private static readonly Regex SrtTimePattern = new(
            @"(\d{2}):(\d{2}):(\d{2}),(\d{3})\s*-->\s*(\d{2}):(\d{2}):(\d{2}),(\d{3})",
            RegexOptions.Compiled
        );

        // VTT: "00:01:23.456 --> 00:01:25.789"
        private static readonly Regex VttTimePattern = new(
            @"(\d{2}):(\d{2}):(\d{2})\.(\d{3})\s*-->\s*(\d{2}):(\d{2}):(\d{2})\.(\d{3})",
            RegexOptions.Compiled
        );

        // VTT Short: "01:23.456 --> 01:25.789" (시간 생략)
        private static readonly Regex VttShortTimePattern = new(
            @"(\d{2}):(\d{2})\.(\d{3})\s*-->\s*(\d{2}):(\d{2})\.(\d{3})",
            RegexOptions.Compiled
        );

        #endregion

        #region Public Methods

        /// <summary>자막 내용 파싱</summary>
        public bool Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                Debug.LogWarning("[SubtitleParser] 빈 내용");
                return false;
            }

            Entries.Clear();

            // 포맷 감지
            Format = DetectFormat(content);

            bool success = Format switch
            {
                SubtitleFormat.SRT => ParseSrt(content),
                SubtitleFormat.VTT => ParseVtt(content),
                _ => false
            };

            if (success)
            {
                // 시간순 정렬
                Entries.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
                Debug.Log($"[SubtitleParser] 파싱 완료: {Entries.Count}개 항목 ({Format})");
            }
            else
            {
                Debug.LogError("[SubtitleParser] 파싱 실패");
            }

            return success;
        }

        /// <summary>특정 시간의 자막 항목 반환</summary>
        public SubtitleEntry GetEntryAtTime(double timeInSeconds)
        {
            // 이진 검색으로 최적화 (정렬된 리스트 가정)
            int left = 0;
            int right = Entries.Count - 1;

            while (left <= right)
            {
                int mid = (left + right) / 2;
                var entry = Entries[mid];

                if (timeInSeconds >= entry.StartTime && timeInSeconds <= entry.EndTime)
                {
                    return entry;
                }
                else if (timeInSeconds < entry.StartTime)
                {
                    right = mid - 1;
                }
                else
                {
                    left = mid + 1;
                }
            }

            return null;
        }

        /// <summary>특정 시간 이후의 다음 자막 항목 반환</summary>
        public SubtitleEntry GetNextEntry(double timeInSeconds)
        {
            foreach (var entry in Entries)
            {
                if (entry.StartTime > timeInSeconds)
                {
                    return entry;
                }
            }
            return null;
        }

        /// <summary>인덱스로 자막 항목 반환</summary>
        public SubtitleEntry GetEntryByIndex(int index)
        {
            if (index >= 0 && index < Entries.Count)
            {
                return Entries[index];
            }
            return null;
        }

        #endregion

        #region Format Detection

        private SubtitleFormat DetectFormat(string content)
        {
            var firstLine = content.TrimStart();

            if (firstLine.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase))
            {
                return SubtitleFormat.VTT;
            }

            // SRT는 숫자로 시작 (인덱스)
            if (char.IsDigit(firstLine[0]))
            {
                // VTT 타임코드 패턴 확인
                if (VttTimePattern.IsMatch(content) || VttShortTimePattern.IsMatch(content))
                {
                    return SubtitleFormat.VTT;
                }
                // SRT 타임코드 패턴 확인
                if (SrtTimePattern.IsMatch(content))
                {
                    return SubtitleFormat.SRT;
                }
            }

            return SubtitleFormat.Unknown;
        }

        #endregion

        #region SRT Parsing

        private bool ParseSrt(string content)
        {
            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            int index = 0;
            int lineIndex = 0;

            while (lineIndex < lines.Length)
            {
                // 빈 줄 스킵
                while (lineIndex < lines.Length && string.IsNullOrWhiteSpace(lines[lineIndex]))
                {
                    lineIndex++;
                }

                if (lineIndex >= lines.Length) break;

                // 인덱스 번호 (스킵)
                if (!int.TryParse(lines[lineIndex].Trim(), out _))
                {
                    lineIndex++;
                    continue;
                }
                lineIndex++;

                if (lineIndex >= lines.Length) break;

                // 타임코드
                var timeMatch = SrtTimePattern.Match(lines[lineIndex]);
                if (!timeMatch.Success)
                {
                    lineIndex++;
                    continue;
                }

                double startTime = ParseSrtTime(timeMatch, 1);
                double endTime = ParseSrtTime(timeMatch, 5);
                lineIndex++;

                // 텍스트 (여러 줄 가능)
                var textLines = new List<string>();
                while (lineIndex < lines.Length && !string.IsNullOrWhiteSpace(lines[lineIndex]))
                {
                    textLines.Add(lines[lineIndex]);
                    lineIndex++;
                }

                if (textLines.Count > 0)
                {
                    var entry = new SubtitleEntry
                    {
                        Index = index++,
                        StartTime = startTime,
                        EndTime = endTime,
                        Text = string.Join("\n", textLines)
                    };
                    Entries.Add(entry);
                }
            }

            return Entries.Count > 0;
        }

        private double ParseSrtTime(Match match, int startGroup)
        {
            int hours = int.Parse(match.Groups[startGroup].Value);
            int minutes = int.Parse(match.Groups[startGroup + 1].Value);
            int seconds = int.Parse(match.Groups[startGroup + 2].Value);
            int millis = int.Parse(match.Groups[startGroup + 3].Value);

            return hours * 3600 + minutes * 60 + seconds + millis / 1000.0;
        }

        #endregion

        #region VTT Parsing

        private bool ParseVtt(string content)
        {
            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            int index = 0;
            int lineIndex = 0;

            // WEBVTT 헤더 스킵
            while (lineIndex < lines.Length)
            {
                var line = lines[lineIndex].Trim();
                if (line.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase))
                {
                    lineIndex++;
                    continue;
                }
                if (string.IsNullOrEmpty(line))
                {
                    lineIndex++;
                    continue;
                }
                // NOTE, STYLE, REGION 블록 스킵
                if (line.StartsWith("NOTE", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("STYLE", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("REGION", StringComparison.OrdinalIgnoreCase))
                {
                    while (lineIndex < lines.Length && !string.IsNullOrWhiteSpace(lines[lineIndex]))
                    {
                        lineIndex++;
                    }
                    continue;
                }
                break;
            }

            while (lineIndex < lines.Length)
            {
                // 빈 줄 스킵
                while (lineIndex < lines.Length && string.IsNullOrWhiteSpace(lines[lineIndex]))
                {
                    lineIndex++;
                }

                if (lineIndex >= lines.Length) break;

                // 큐 ID (선택적) - 타임코드 라인이 아니면 스킵
                var currentLine = lines[lineIndex];
                if (!currentLine.Contains("-->"))
                {
                    lineIndex++;
                    if (lineIndex >= lines.Length) break;
                    currentLine = lines[lineIndex];
                }

                // 타임코드 파싱
                double startTime = 0;
                double endTime = 0;
                bool timeFound = false;

                var vttMatch = VttTimePattern.Match(currentLine);
                if (vttMatch.Success)
                {
                    startTime = ParseVttTime(vttMatch, 1);
                    endTime = ParseVttTime(vttMatch, 5);
                    timeFound = true;
                }
                else
                {
                    var shortMatch = VttShortTimePattern.Match(currentLine);
                    if (shortMatch.Success)
                    {
                        startTime = ParseVttShortTime(shortMatch, 1);
                        endTime = ParseVttShortTime(shortMatch, 4);
                        timeFound = true;
                    }
                }

                if (!timeFound)
                {
                    lineIndex++;
                    continue;
                }

                lineIndex++;

                // 텍스트 (여러 줄 가능)
                var textLines = new List<string>();
                while (lineIndex < lines.Length && !string.IsNullOrWhiteSpace(lines[lineIndex]))
                {
                    // VTT 태그 제거
                    var text = RemoveVttTags(lines[lineIndex]);
                    textLines.Add(text);
                    lineIndex++;
                }

                if (textLines.Count > 0)
                {
                    var entry = new SubtitleEntry
                    {
                        Index = index++,
                        StartTime = startTime,
                        EndTime = endTime,
                        Text = string.Join("\n", textLines)
                    };
                    Entries.Add(entry);
                }
            }

            return Entries.Count > 0;
        }

        private double ParseVttTime(Match match, int startGroup)
        {
            int hours = int.Parse(match.Groups[startGroup].Value);
            int minutes = int.Parse(match.Groups[startGroup + 1].Value);
            int seconds = int.Parse(match.Groups[startGroup + 2].Value);
            int millis = int.Parse(match.Groups[startGroup + 3].Value);

            return hours * 3600 + minutes * 60 + seconds + millis / 1000.0;
        }

        private double ParseVttShortTime(Match match, int startGroup)
        {
            int minutes = int.Parse(match.Groups[startGroup].Value);
            int seconds = int.Parse(match.Groups[startGroup + 1].Value);
            int millis = int.Parse(match.Groups[startGroup + 2].Value);

            return minutes * 60 + seconds + millis / 1000.0;
        }

        private string RemoveVttTags(string text)
        {
            // <v>, <c>, <i>, <b>, <u>, <ruby>, <rt> 등의 태그 제거
            return Regex.Replace(text, @"<[^>]+>", "");
        }

        #endregion
    }

    #region Data Structures

    /// <summary>자막 항목</summary>
    [Serializable]
    public class SubtitleEntry
    {
        /// <summary>인덱스 (0-based)</summary>
        public int Index;

        /// <summary>시작 시간 (초)</summary>
        public double StartTime;

        /// <summary>종료 시간 (초)</summary>
        public double EndTime;

        /// <summary>자막 텍스트</summary>
        public string Text;

        /// <summary>표시 시간 (초)</summary>
        public double Duration => EndTime - StartTime;

        public override string ToString()
        {
            return $"[{Index}] {StartTime:F3}-{EndTime:F3}: {Text}";
        }
    }

    /// <summary>자막 포맷</summary>
    public enum SubtitleFormat
    {
        Unknown,
        SRT,
        VTT
    }

    #endregion
}
