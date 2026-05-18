using System.Collections.Generic;
using UnityEngine;

namespace ReplaySystem.Compression
{
    /// <summary>양자화 프리셋 관리자입니다.</summary>
    [CreateAssetMenu(fileName = "QuantizationPresetManager", menuName = "ReplaySystem/Preset Manager", order = 0)]
    public class QuantizationPresetManager : ScriptableObject
    {
        [Header("Default Preset")]
        [Tooltip("기본으로 사용할 프리셋")]
        [SerializeField] private QuantizationPreset defaultPreset;

        [Header("Available Presets")]
        [Tooltip("사용 가능한 프리셋 목록")]
        [SerializeField] private List<QuantizationPreset> presets = new List<QuantizationPreset>();

        private static QuantizationPresetManager instance;
        private Dictionary<string, QuantizationPreset> presetLookup;
        private PresetQuantizer defaultQuantizer;

        /// <summary>싱글톤 인스턴스</summary>
        public static QuantizationPresetManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = Resources.Load<QuantizationPresetManager>("QuantizationPresetManager");

                    if (instance == null)
                    {
                        instance = CreateInstance<QuantizationPresetManager>();
                        instance.CreateDefaultPresets();
                    }
                }

                return instance;
            }
        }

        /// <summary>기본 프리셋</summary>
        public QuantizationPreset DefaultPreset
        {
            get
            {
                if (defaultPreset == null && presets.Count > 0)
                {
                    defaultPreset = presets[0];
                }

                return defaultPreset;
            }
            set => defaultPreset = value;
        }

        /// <summary>기본 Quantizer</summary>
        public PresetQuantizer DefaultQuantizer
        {
            get
            {
                if (defaultQuantizer == null && DefaultPreset != null)
                {
                    defaultQuantizer = new PresetQuantizer(DefaultPreset);
                }

                return defaultQuantizer;
            }
        }

        /// <summary>등록된 프리셋 목록</summary>
        public IReadOnlyList<QuantizationPreset> Presets => presets;

        private void OnEnable()
        {
            BuildLookup();
        }

        private void BuildLookup()
        {
            presetLookup = new Dictionary<string, QuantizationPreset>();

            foreach (var preset in presets)
            {
                if (preset != null && !string.IsNullOrEmpty(preset.PresetName))
                {
                    presetLookup[preset.PresetName] = preset;
                }
            }
        }

        /// <summary>이름으로 프리셋 가져오기</summary>
        public QuantizationPreset GetPreset(string name)
        {
            if (presetLookup == null)
            {
                BuildLookup();
            }

            if (presetLookup.TryGetValue(name, out var preset))
            {
                return preset;
            }

            Debug.LogWarning($"[PresetManager] Preset not found: {name}");
            return DefaultPreset;
        }

        /// <summary>프리셋으로 Quantizer 생성</summary>
        public PresetQuantizer CreateQuantizer(QuantizationPreset preset)
        {
            return new PresetQuantizer(preset ?? DefaultPreset);
        }

        /// <summary>이름으로 Quantizer 생성</summary>
        public PresetQuantizer CreateQuantizer(string presetName)
        {
            return new PresetQuantizer(GetPreset(presetName));
        }

        /// <summary>프리셋 등록</summary>
        public void RegisterPreset(QuantizationPreset preset)
        {
            if (preset == null)
            {
                return;
            }

            if (!presets.Contains(preset))
            {
                presets.Add(preset);
            }

            if (presetLookup == null)
            {
                BuildLookup();
            }

            presetLookup[preset.PresetName] = preset;
        }

        /// <summary>프리셋 제거</summary>
        public void UnregisterPreset(QuantizationPreset preset)
        {
            if (preset == null)
            {
                return;
            }

            presets.Remove(preset);
            presetLookup?.Remove(preset.PresetName);

            if (defaultPreset == preset)
            {
                defaultPreset = presets.Count > 0 ? presets[0] : null;
            }
        }

        /// <summary>기본 프리셋들 생성</summary>
        public void CreateDefaultPresets()
        {
            presets.Clear();

            var fps = QuantizationPreset.CreateFPSPreset();
            var racing = QuantizationPreset.CreateRacingPreset();
            var space = QuantizationPreset.CreateSpacePreset();
            var micro = QuantizationPreset.CreateMicroPreset();
            var vr = QuantizationPreset.CreateVRPreset();

            presets.Add(fps);
            presets.Add(racing);
            presets.Add(space);
            presets.Add(micro);
            presets.Add(vr);

            defaultPreset = fps;
            BuildLookup();
        }

        /// <summary>프리셋 비교 정보</summary>
        public string GetPresetComparison()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== Quantization Preset Comparison ===\n");

            foreach (var preset in presets)
            {
                if (preset == null)
                {
                    continue;
                }

                var error = preset.GetErrorInfo();

                sb.AppendLine($"[{preset.PresetName}]");
                sb.AppendLine($"  {preset.Description}");
                sb.AppendLine($"  Position Range: {preset.Position.x.minValue:F0} ~ {preset.Position.x.maxValue:F0}");
                sb.AppendLine($"  Position Error: ±{error.positionError.x:F4}");
                sb.AppendLine($"  Rotation Error: ±{error.rotationError:F2}°");
                sb.AppendLine($"  Velocity Range: {preset.Velocity.x.minValue:F0} ~ {preset.Velocity.x.maxValue:F0}");
                sb.AppendLine($"  Transform Size: {preset.TransformByteSize} bytes");
                sb.AppendLine($"  Rigidbody Size: {preset.RigidbodyByteSize} bytes");
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
