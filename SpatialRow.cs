using TRAINEE;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace DrillSergeant
{
    public class SpatialRow : MonoBehaviour
    {
        private TextMeshProUGUI _titleText;
        private OptionalToggle[] _toggles;
        private List<ERoomState> _availableStates;

        // 데이터 주머니
        private string _currentElementId;
        private Action<string, ERoomState> _onStateChangedAction;

        private void Awake()
        {
            _titleText = GetComponentInChildren<TextMeshProUGUI>(true);
            _toggles = GetComponentsInChildren<OptionalToggle>(true);

            gameObject.SetActive(false);

            for (int i = 0; i < _toggles.Length; i++)
            {
                _toggles[i].Init();

                int index = i;
                _toggles[i].EachToggle.onValueChanged.AddListener((isOn) => OnToggleChanged(isOn, index));
            }
        }

        private void OnToggleChanged(bool isOn, int index)
        {
            if (isOn == true && _availableStates != null && index < _availableStates.Count)
            {
                _onStateChangedAction?.Invoke(_currentElementId, _availableStates[index]);
            }
        }

        public void Refresh(RoomElementData data, ERoomState currentState, Action<string, ERoomState> onStateChanged)
        {
            _currentElementId = data.Id;
            _onStateChangedAction = onStateChanged;

            _titleText.text = TRAINEE.DataManager.Instance.GetDisplayName(data.displayName.ToString());
            _availableStates = MonitoringPoolSystem.Instance.GetAvailableStates(data.Id);

            foreach (OptionalToggle item in _toggles) 
                item.gameObject.SetActive(false);

            for (int i = 0; i < _availableStates.Count; i++)
            {
                _toggles[i].OptionText = _availableStates[i].ToString();

                ERoomState targetState = _availableStates[i];
                bool isTarget = (targetState == currentState);

                _toggles[i].EachToggle.SetIsOnWithoutNotify(isTarget);
                _toggles[i].gameObject.SetActive(true);
            }
        }
    }
}
