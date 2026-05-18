using System;
using System.Text;
using ReplaySystem.Compression;
using ReplaySystem.Core;
using UnityEngine;

namespace ReplaySystem.Capture
{
    /// <summary>Transform 캡처 컴포넌트입니다.</summary>
    public class TransformCapture : MonoBehaviour, IReplayCapture
    {
        private static Type _networkObjectType;
        private static bool _networkObjectTypeResolved;

        private static Type GetNetworkObjectType()
        {
            if (_networkObjectTypeResolved)
            {
                return _networkObjectType;
            }
            _networkObjectType = Type.GetType("FishNet.Object.NetworkObject, FishNet.Runtime");
            _networkObjectTypeResolved = true;
            return _networkObjectType;
        }

        private bool DetectNetworkObject()
        {
            var type = GetNetworkObjectType();
            return type != null && GetComponent(type) != null;
        }

        // 인스턴스별 재사용 버퍼 (스레드 안전, lazy initialization)
        private StringBuilder hierarchyPathBuilder;

        [SerializeField] private string prefabName;
        [SerializeField] private string prefabPath;

        private int objectId = -1;
        private Transform cachedTransform;

        /// <inheritdoc/>
        public int ObjectId => objectId;

        /// <inheritdoc/>
        public string PrefabName
        {
            get => prefabName;
            set => prefabName = value;
        }

        /// <summary>Resources.Load 경로. 동적/네트워크 오브젝트 재생성에 사용.</summary>
        public string PrefabPath
        {
            get => prefabPath;
            set => prefabPath = value;
        }

        private void Awake()
        {
            cachedTransform = transform;

            if (string.IsNullOrEmpty(prefabName))
            {
                prefabName = gameObject.name;
            }
        }

        /// <inheritdoc/>
        public void Initialize(int id)
        {
            objectId = id;
        }

        /// <inheritdoc/>
        public ObjectState CaptureInitialState()
        {
            var rbCapture = GetComponent<RigidbodyCapture>();

            var state = new ObjectState
            {
                Id = objectId,
                PrefabName = prefabName,
                PrefabPath = prefabPath,
                HierarchyPath = GetHierarchyPath(),
                IsNetworkObject = DetectNetworkObject(),
                Transform = CompressedTransform.FromTransform(cachedTransform, gameObject.activeSelf),
                HasRigidbody = rbCapture != null && rbCapture.HasRigidbody,
                CustomData = null
            };

            if (state.HasRigidbody)
            {
                state.Rigidbody = rbCapture.CaptureState();
            }

            return state;
        }

        /// <inheritdoc/>
        public CompressedTransform CaptureCompressedTransform()
        {
            return CompressedTransform.FromTransform(cachedTransform, gameObject.activeSelf);
        }

        /// <inheritdoc/>
        public void ApplyState(CompressedTransform state)
        {
            state.ApplyToTransform(cachedTransform);
        }

        /// <inheritdoc/>
        public void ApplyInterpolatedState(CompressedTransform from, CompressedTransform to, float t)
        {
            Vector3 fromPos = from.GetPosition();
            Vector3 toPos = to.GetPosition();
            Quaternion fromRot = from.GetRotation();
            Quaternion toRot = to.GetRotation();
            Vector3 fromScale = from.GetScale();
            Vector3 toScale = to.GetScale();

            cachedTransform.position = Vector3.Lerp(fromPos, toPos, t);
            cachedTransform.rotation = Quaternion.Slerp(fromRot, toRot, t);
            cachedTransform.localScale = Vector3.Lerp(fromScale, toScale, t);
            gameObject.SetActive(t < 0.5f ? from.IsActive : to.IsActive);
        }

        public string GetHierarchyPath()
        {
            // Lazy initialization (첫 호출 시에만 할당)
            hierarchyPathBuilder ??= new StringBuilder(256);
            hierarchyPathBuilder.Clear();
            BuildHierarchyPathRecursive(cachedTransform, hierarchyPathBuilder);
            return hierarchyPathBuilder.ToString();
        }

        private void BuildHierarchyPathRecursive(Transform t, StringBuilder sb)
        {
            if (t.parent != null)
            {
                BuildHierarchyPathRecursive(t.parent, sb);
                sb.Append('/');
            }

            sb.Append(t.name);
        }
    }
}
