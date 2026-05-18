using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TRAINEE;

namespace DrillSergeant
{
    public class ReviewRow : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _textNum;
        [SerializeField] private TextMeshProUGUI _textTitle;
        [SerializeField] private TextMeshProUGUI _textMode;
        [SerializeField] private TextMeshProUGUI _textTimeStampStart;
        [SerializeField] private TextMeshProUGUI _textTimeStampEnd;
        [SerializeField] private TextMeshProUGUI _textSergeantID;
        [SerializeField] private TextMeshProUGUI _textDrillCode;
        [SerializeField] private TextMeshProUGUI _textParticipantCount;
        [SerializeField] private TextMeshProUGUI _textScenarioName;
        [SerializeField] private Button _detailBtn;

        public void SetEventOutcome(SaveFileDatas setSceneFile, Sprite backImg)
        {

        }

    }
}