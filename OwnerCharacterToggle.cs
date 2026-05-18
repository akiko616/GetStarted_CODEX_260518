using System;
using UnityEngine;
using UnityEngine.Events;
using FishNet.Object;
using FishNet.Connection;

namespace GameInstancePlugin.Client
{
    public enum PlatformType { PC, VR }

    /// <summary>
    /// IsOwner + PC/VR 플랫폼별로 등록된 오브젝트/컴포넌트를 토글합니다.
    /// - Common: 플랫폼 무관하게 IsOwner에 따라 토글
    /// - PC/VR: 해당 플랫폼일 때만 IsOwner에 따라 토글, 비활성 플랫폼은 전부 OFF
    /// - 델리게이트/UnityEvent로 외부 함수 연결 가능
    /// </summary>
    public class OwnerCharacterToggle : NetworkBehaviour
    {
        [Header("플랫폼 타입")]
        [SerializeField] private PlatformType platformType = PlatformType.PC;

        [Header("공통 (PC/VR 무관)")]
        [SerializeField] private ToggleSet common;

        [Header("PC 전용")]
        [SerializeField] private ToggleSet pc;

        [Header("VR 전용")]
        [SerializeField] private ToggleSet vr;

        [Header("UnityEvent")]
        [SerializeField] private UnityEvent<bool> onOwnerStateChanged;
        [SerializeField] private UnityEvent<PlatformType> onPlatformChanged;

        /// <summary>Owner 상태 변경 시 발행 (isOwner)</summary>
        public event Action<bool> OwnerStateChanged;

        /// <summary>플랫폼 타입 변경 시 발행 (newPlatformType)</summary>
        public event Action<PlatformType> PlatformTypeChanged;

        /// <summary>현재 플랫폼 타입</summary>
        public PlatformType CurrentPlatform => platformType;

        /// <summary>현재 Owner 여부 (OnStartClient 이후 유효)</summary>
        public bool IsCurrentOwner { get; private set; }

        // ── FishNet 콜백 ──────────────────────────────────

        public override void OnStartClient()
        {
            base.OnStartClient();
            Apply(IsOwner);
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);
            Apply(IsOwner);
        }

        // ── Public API ────────────────────────────────────

        /// <summary>런타임에 플랫폼 타입을 변경하고 토글을 재적용합니다.</summary>
        public void SetPlatformType(PlatformType newType)
        {
            if (platformType == newType) return;
            platformType = newType;
            Apply(IsCurrentOwner);

            onPlatformChanged?.Invoke(newType);
            PlatformTypeChanged?.Invoke(newType);
        }

        /// <summary>현재 상태를 기준으로 토글을 강제 재적용합니다.</summary>
        public void Refresh()
        {
            Apply(IsCurrentOwner);
        }

        // ── 내부 로직 ─────────────────────────────────────

        private void Apply(bool isOwner)
        {
            IsCurrentOwner = isOwner;

            // 공통: 항상 적용
            ApplySet(common, isOwner);

            // 플랫폼별: 활성 플랫폼만 Owner 토글, 비활성 플랫폼은 전부 OFF
            switch (platformType)
            {
                case PlatformType.PC:
                    ApplySet(pc, isOwner);
                    DisableAll(vr);
                    break;
                case PlatformType.VR:
                    ApplySet(vr, isOwner);
                    DisableAll(pc);
                    break;
            }

            onOwnerStateChanged?.Invoke(isOwner);
            OwnerStateChanged?.Invoke(isOwner);
        }

        private static void ApplySet(ToggleSet set, bool isOwner)
        {
            if (set == null) return;
            Toggle(set.ownerOnly, isOwner);
            Toggle(set.nonOwnerOnly, !isOwner);
        }

        private static void DisableAll(ToggleSet set)
        {
            if (set == null) return;
            Toggle(set.ownerOnly, false);
            Toggle(set.nonOwnerOnly, false);
        }

        private static void Toggle(ToggleGroup group, bool state)
        {
            if (group == null) return;

            var objects = group.objects;
            if (objects != null)
            {
                for (int i = 0; i < objects.Length; i++)
                {
                    if (objects[i] != null)
                        objects[i].SetActive(state);
                }
            }

            var components = group.components;
            if (components != null)
            {
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] != null)
                        components[i].enabled = state;
                }
            }
        }

        // ── 직렬화 구조 ───────────────────────────────────

        /// <summary>Owner/NonOwner 한 쌍의 토글 그룹</summary>
        [Serializable]
        public class ToggleSet
        {
            [Tooltip("Owner일 때 활성화")]
            public ToggleGroup ownerOnly;

            [Tooltip("Owner가 아닐 때 활성화")]
            public ToggleGroup nonOwnerOnly;
        }

        /// <summary>오브젝트 + 컴포넌트 등록 단위</summary>
        [Serializable]
        public class ToggleGroup
        {
            public GameObject[] objects = Array.Empty<GameObject>();
            public Behaviour[] components = Array.Empty<Behaviour>();
        }
    }
}
