using FishNet.Object;
using UnityEngine;

namespace TRAINEE
{
    public class PlayerFollwCam : MonoBehaviour
    {
        [SerializeField] private Camera _cam = null;

        [SerializeField] private Transform _headBone = null;

        [SerializeField] private Vector3 _Offset = Vector3.zero;

        public Camera MainCamera { get { return _cam; } }
        public void Init(Transform headBone)
        {
            _headBone = headBone;
            Camera.SetupCurrent(_cam);
        }

        public void OnLateUpdate(float pitch)
        {
            if(_headBone == null)
            {
                Debug.Log($"HeadBone is Null");
                return;
            }

            Quaternion cleanRotation = Quaternion.Euler(pitch, _headBone.root.eulerAngles.y, 0f);
            Vector3 targetPosition = _headBone.position + (cleanRotation * _Offset);

            _cam.transform.position = targetPosition;
        }

    }
}
