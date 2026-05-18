using FishNet.Object;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace TRAINEE
{
    public class PlayerFollwCam_VR : MonoBehaviour
    {
        [SerializeField] private Camera _cam = null;
        [SerializeField] private CharacterController controller;

        public Camera MainCamera { get { return _cam; } }
        public void Init(CharacterController cc)
        {
            controller = cc;
        }

        //private void LateUpdate()
        //{
        //    //if (controller == null || xrOrigin.Camera == null) return;

        //    //// XROrigin 카메라 로컬 위치
        //    //Vector3 currentLocalPos = xrOrigin.CameraInOriginSpacePos;

        //    //// Y축(높이)을 제외한 수평 이동 거리만 계산
        //    //float deltaX = currentLocalPos.x - _lastLocalPos.x;
        //    //float deltaZ = currentLocalPos.z - _lastLocalPos.z;
        //    //float sqrDistance = (deltaX * deltaX) + (deltaZ * deltaZ);

        //    //if (sqrDistance > sqrMoveThreshold)
        //    //{
        //    //    SyncCharacterToCamera(false);
        //    //    _lastLocalPos = currentLocalPos;
        //    //}
            
        //}

        //public void SyncCharacterToCamera(bool forceAll)
        //{
        //    Vector3 cameraPos = xrOrigin.CameraInOriginSpacePos;

        //    //cc 중심점을 카메라의 X, Z 위치로 이동
        //    controller.center = new Vector3(cameraPos.x, controller.height * 0.5f, cameraPos.z);

        //    //캐릭터 모델링을 발밑 위치에 맞춤
        //    if (characterVisual != null)
        //    {
        //        characterVisual.localPosition = new Vector3(cameraPos.x, 0f, cameraPos.z);
        //    }
        //}
    }
}
