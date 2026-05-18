using UnityEngine;

namespace TRAINEE
{
    public class WalkieTalkieAnimEvent : EquipAnimEventBase
    {
        protected override void OnHitEffect()
        {
            Debug.Log("WalkieTalkieAnimEvent 연출 실행");
        }
    }
}
