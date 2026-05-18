using System.Collections.Generic;
using UnityEngine;


namespace TRAINEE
{
    public class TerrainView : MapViewBase
    {
        [Header("터레인 세팅")]
        [SerializeField] private GameObject _snowTerrain = null;

        [Header("날씨 세팅")]
        [SerializeField] private List<GameObject> _rains = null;
        [SerializeField] private List<GameObject> _snows = null;
        public override void Init(string id)
        {
            _id = id;

            _snowTerrain.SetActive(false);

            for (int i = 0; i < _rains.Count; i++)
            {
                _rains[i].SetActive(false);
            }

            for (int i = 0; i < _snows.Count; i++)
            {
                _snows[i].SetActive(false);
            }
        }

        public override void UpdateView(GameEvent data)
        {
            TerrainEventData _event = data._param as TerrainEventData;

            if (_event != null)
            {
                if(_event._weather == EWeather.Rain)
                {
                    // 비가 온다면 지형 요건 변경

                    for(int i = 0; i < _rains.Count; i++)
                    {
                        _rains[i].SetActive(true);
                    }
                }

                if(_event._weather == EWeather.Snow)
                {
                    // 눈이 온다면 지형 요건 변경
                    _snowTerrain.SetActive(true);

                    for (int i = 0; i < _snows.Count; i++)
                    {
                        _snows[i].SetActive(true);
                    }
                }
            }
        }
    }
}
