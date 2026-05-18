using System;
using UnityEngine;

namespace TRAINEE
{
    [CreateAssetMenu(fileName = "CharacterInfo", menuName = "Scriptable Object Aesset/CharacterInfo")]
    public class CharacterInfo : ScriptableObject
    {
        public CharacterEquipAniData[] _equipAnis;
        public CharacterAniData[] _anis;

        public AnimatorOverrideController FindEquipAnimator(string equipid)
        {
            for (int i = 0; i < _equipAnis.Length; i++)
            {
                if (_equipAnis[i]._equipid.Equals(equipid))
                {
                    return _equipAnis[i]._animator;
                }
            }

            return null;
        }

        public int FindEquipAnimatorLayer(string equipid)
        {
            for (int i = 0; i < _equipAnis.Length; i++)
            {
                if (_equipAnis[i]._equipid.Equals(equipid))
                {
                    return (int)_equipAnis[i]._eLayer;
                }
            }

            return (int)EAnimLayer.Base;
        }
    }


    [Serializable]
    public class CharacterAniData
    {
        public EAnimParam _eAniType;
        public EAnimLayer _eLayer;
    }

    [Serializable]
    public class CharacterEquipAniData
    {
        public string _equipid;
        public EAnimLayer _eLayer;
        public AnimatorOverrideController _animator;
    }
}
