using System;
using System.Linq;
using TMPro;
using UnityEngine;

namespace DrillSergeant
{
    public class OccasionalRow : MonoBehaviour
    {
        private TextMeshProUGUI _titleText;
        private OptionalToggle[] _toggles;

        private string _currentElementId;

        private void Awake()
        {
            _titleText = GetComponentInChildren<TextMeshProUGUI>(true);
            _toggles = GetComponentsInChildren<OptionalToggle>(true);

            gameObject.SetActive(false);

            foreach (OptionalToggle toggle in _toggles) 
                toggle.Init();
        }


        /// <summary>
        /// 1. 이 코드의 목적: Enum값을 다른걸로 셋팅하면서 다양하게 액션이벤트를 토글에 넣기위함
        /// 2. 핵심 로직 흐름(3줄 이내): Enum
        /// 3. 왜 이렇게 구현했는지: 이미 문제없이 잘 작동하는 SpatialRow는 그대로 사용하고 앞으로 추가되는 것들은 따로 관리하기 위함
        /// 4. 리스크: 새로운 훈련을 만들때마다 기존은 파괴하고 새로 만들어 데이터 효율성 떨어짐
        /// 5. 예외: 
        /// </summary>
        public void Init<T>(string id, string displayName, T initStateFromFTP, Action<string, T> onStateChanged) where T : Enum
        {
            _currentElementId = id;
            _titleText.text = displayName;

            T[] sortedValues = Enum.GetValues(typeof(T)).Cast<T>().OrderBy(e => Convert.ToInt32(e)).ToArray();
            // 필요시 여기서 Values 솔팅

            foreach (var toggle in _toggles)
                toggle.gameObject.SetActive(false);

            for (int i = 0; i < sortedValues.Length; i++)
            {
                if (i >= _toggles.Length)
                {
                    Debug.Log($"enum에 등록된 항목 수: {sortedValues.Length} 가, 토글개수: {_toggles.Length} 보다 많아서 등록오류 ");
                    break;
                }

                T enumValue = (T)sortedValues.GetValue(i);

                _toggles[i].OptionText = enumValue.ToString();

                bool isTarget = enumValue.Equals(initStateFromFTP);
                _toggles[i].EachToggle.SetIsOnWithoutNotify(isTarget);


                _toggles[i].EachToggle.onValueChanged.RemoveAllListeners();
                _toggles[i].EachToggle.onValueChanged.AddListener((isOn) =>
                {
                    if (isOn)
                    {
                        onStateChanged?.Invoke(_currentElementId, enumValue);
                    }
                });
                _toggles[i].gameObject.SetActive(true);
            }
        }
    }
}