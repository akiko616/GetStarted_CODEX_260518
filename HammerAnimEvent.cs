using UnityEngine;


namespace TRAINEE
{
    public class HammerAnimEvent : EquipAnimEventBase
    {
        public override void OnHit()
        {
            base.OnHit();
        }

        public override void OnHitEnd()
        {
            base.OnHitEnd();
        }

        protected override void OnHitEffect()
        {
            Debug.Log("HammerAnimEvent 연출 실행");
        }
    }
}
