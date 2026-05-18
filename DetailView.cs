using TRAINEE;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DrillSergeant
{
    public class DetailView : MonoBehaviour
    {
        private TextMeshProUGUI _titleText;
        private Toggle _toggleMyself;

        // 데이터 주머니
        private string _currentRoomId;
        private Action<string> _onClickAction;

        private void Awake()
        {
            _titleText = GetComponentInChildren<TextMeshProUGUI>(true);
            _toggleMyself = GetComponent<Toggle>();

            gameObject.SetActive(false);

            _toggleMyself.onValueChanged.AddListener(OnToggleValueChanged);
        }

        private void OnToggleValueChanged(bool isOn)
        {
            if (isOn == true && string.IsNullOrEmpty(_currentRoomId) == false)
            {
                _onClickAction?.Invoke(_currentRoomId);
            }
        }

        public void Refresh(RoomData data, Action<string> onClick)
        {
            _currentRoomId = data.Id;
            _onClickAction = onClick;

            _titleText.text = TRAINEE.DataManager.Instance.GetDisplayName(data.displayName.ToString());            
        }
    }
}
