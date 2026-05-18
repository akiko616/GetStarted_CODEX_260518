using TMPro;
using UnityEngine.UI;
using UnityEngine;

namespace DrillSergeant
{

    public class OptionalToggle : MonoBehaviour
    {
        private Toggle _thisToggle;
        private TextMeshProUGUI _optionText;

        public Toggle EachToggle { get => _thisToggle; set => _thisToggle = value; }
        public string OptionText { set => _optionText.text = value; }

        public void Init()
        {
            _thisToggle = GetComponent<Toggle>();
            _optionText = GetComponentInChildren<TextMeshProUGUI>(true);
        }
    }
}
