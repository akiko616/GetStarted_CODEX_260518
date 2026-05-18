using TRAINEE;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DrillSergeant
{
    public class ListUnit : MonoBehaviour
    {
        [SerializeField] private Image _titleIcon;
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _purposeText;
        [SerializeField] private TextMeshProUGUI _participantText;
        [SerializeField] private TextMeshProUGUI _infoText;
        [SerializeField] private Button _createBtn;

        private int _thisFTPIndex = -1;

        public Button CreateBtn
        {
            get { return _createBtn; }
        }


        public void Init(SaveFileDatas ftpData)
        {
            TrainingDatas trainingData = ftpData.trainingData;

            _titleIcon.sprite = ScenarioRoleData.Instance.GetScenarioIcon(trainingData.scenarioNum);
            _titleText.text = trainingData.scenarioName;
            _purposeText.text = trainingData.scenarioGoals;
            _participantText.text = trainingData.scenarioPeople;
            _infoText.text = trainingData.scenarioStory;
        }    

    }
}
