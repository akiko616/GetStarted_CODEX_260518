using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class VoiceClipLibrary
{
    public class Entry
    {
        public string textId;
        public string sourceText;
        public string wavPath;
        public AudioClip clip;
    }

    private readonly List<Entry> _entries = new();
    private readonly Dictionary<string, Entry> _map = new();

    public int Count => _entries.Count;
    public IReadOnlyList<Entry> Entries => _entries;

    public AudioClip this[string id] => Get(id);

    public AudioClip Get(string textId)
    {
        return _map.TryGetValue(textId, out var e) ? e.clip : null;
    }

    public bool TryGet(string textId, out AudioClip clip)
    {
        clip = Get(textId);
        return clip != null;
    }

    public bool HasMatchingEntry(string textId, string text)
    {
        return _map.TryGetValue(textId, out var e)
            && e.clip != null
            && e.sourceText == text;
    }

    public void Register(string textId, string sourceText, string wavPath, AudioClip clip)
    {
        if (_map.TryGetValue(textId, out var existing))
        {
            existing.sourceText = sourceText;
            existing.wavPath = wavPath;
            existing.clip = clip;
        }
        else
        {
            var entry = new Entry { textId = textId, sourceText = sourceText, wavPath = wavPath, clip = clip };
            _entries.Add(entry);
            _map[textId] = entry;
        }
    }

    public void Clear()
    {
        _entries.Clear();
        _map.Clear();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  Disk Persistence
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Serializable]
    private class DiskData
    {
        public List<DiskEntry> entries = new();
    }

    [Serializable]
    private class DiskEntry
    {
        public string textId;
        public string sourceText;
        public string wavPath;
    }

    private static string DiskPath => Path.Combine(Application.persistentDataPath, "VoiceClipLibrary.json");

    public void Save()
    {
        var data = new DiskData();
        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            data.entries.Add(new DiskEntry
            {
                textId = e.textId,
                sourceText = e.sourceText,
                wavPath = e.wavPath
            });
        }

        string json = JsonUtility.ToJson(data, true);
        Directory.CreateDirectory(Path.GetDirectoryName(DiskPath));
        File.WriteAllText(DiskPath, json);
        Debug.Log($"[TTS] 라이브러리 저장: {DiskPath} ({data.entries.Count}건)");
    }

    public void Load()
    {
        if (!File.Exists(DiskPath))
        {
            Debug.Log("[TTS] 저장된 라이브러리 없음");
            return;
        }

        string json = File.ReadAllText(DiskPath);
        var data = JsonUtility.FromJson<DiskData>(json);
        if (data?.entries == null) return;

        int loaded = 0;
        for (int i = 0; i < data.entries.Count; i++)
        {
            var de = data.entries[i];
            if (_map.ContainsKey(de.textId)) continue;

            AudioClip clip = null;
            if (!string.IsNullOrEmpty(de.wavPath) && File.Exists(de.wavPath))
            {
                byte[] wav = File.ReadAllBytes(de.wavPath);
                clip = CloneVoiceTTSClient.WavToClip(wav, de.textId);
            }

            var entry = new Entry
            {
                textId = de.textId,
                sourceText = de.sourceText,
                wavPath = de.wavPath,
                clip = clip
            };
            _entries.Add(entry);
            _map[de.textId] = entry;
            loaded++;
        }

        Debug.Log($"[TTS] 라이브러리 로드: {loaded}건 복원 (총 {_entries.Count}건)");
    }
}
