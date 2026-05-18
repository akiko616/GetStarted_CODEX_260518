using UnityEngine;

namespace TRAINEE
{
    public class IkSetup : MonoBehaviour
    {
        [SerializeField] Transform _ik_target = null;
        [SerializeField] Transform _ik_hint = null;


        public void SetIk(Transform target,Transform hint)
        {
            if (target == null)
            {
                _ik_target.position = Vector3.zero;
                _ik_target.rotation = Quaternion.identity;
            }
            else
            {
                _ik_target.position = target.position;
                _ik_target.rotation = target.rotation;
            }

            if(hint == null)
            {

                _ik_hint.position = Vector3.zero;
                _ik_hint.rotation = Quaternion.identity;
            }
            else
            {

                _ik_hint.position = hint.position;
                _ik_hint.rotation = hint.rotation;
            }

        }

    }
}
