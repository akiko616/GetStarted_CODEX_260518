using UnityEngine;
using FishNet.Object;
using FishNet.Connection;

namespace GameInstancePlugin.Client
{
    /// <summary>
    /// IsOwner 상태에 따라 등록된 GameObject/Component를 활성화/비활성화합니다.
    /// - ownerOnly: Owner일 때만 활성화 (예: 카메라, 입력, UI)
    /// - nonOwnerOnly: Owner가 아닐 때만 활성화 (예: 닉네임 표시, 원격 마커)
    /// </summary>
    public class OwnerObjectToggle : NetworkBehaviour
    {
        [Header("Owner일 때 활성화")]
        [SerializeField] private GameObject[] ownerOnlyObjects;
        [SerializeField] private Behaviour[] ownerOnlyComponents;

        [Header("Owner가 아닐 때 활성화")]
        [SerializeField] private GameObject[] nonOwnerOnlyObjects;
        [SerializeField] private Behaviour[] nonOwnerOnlyComponents;

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

        private void Apply(bool isOwner)
        {
            Toggle(ownerOnlyObjects, ownerOnlyComponents, isOwner);
            Toggle(nonOwnerOnlyObjects, nonOwnerOnlyComponents, !isOwner);
        }

        private static void Toggle(GameObject[] objects, Behaviour[] components, bool state)
        {
            if (objects != null)
            {
                for (int i = 0; i < objects.Length; i++)
                {
                    if (objects[i] != null)
                        objects[i].SetActive(state);
                }
            }

            if (components != null)
            {
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] != null)
                        components[i].enabled = state;
                }
            }
        }
    }
}
