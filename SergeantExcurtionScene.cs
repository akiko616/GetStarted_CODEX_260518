using System;
using System.Collections.Generic;
using UnityEngine;

namespace TRAINEE
{
    public class SergeantExcurtionScene : SceneBase
    {
        [SerializeField] private GameObject _globalVolume;
        [SerializeField] private GameObject _DirectionalLight;
        
        public override void Init()
        {
            base.Init();
        }

        public override void Init(Action<float> onProgress = null)
        {
            base.Init(null);
            
            onProgress?.Invoke(1f);
        }

        public void VolumeLightOnOff(bool isOn)
        {
            _globalVolume.SetActive(isOn);
            _DirectionalLight.SetActive(isOn);
        }

    }
}
