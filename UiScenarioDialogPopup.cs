using TMPro;
using UnityEngine;
using UnityEngine.UI;


namespace TRAINEE
{
    public class UiScenarioDialogPopup : PopupBase
    {
        [SerializeField] private GameObject _dialog = null;
        [SerializeField] private TMP_Text _speecher_txt = null;
        [SerializeField] private TMP_Text _speech_txt = null;
        [SerializeField] private RawImage _video = null;

        public override void Init()
        {
            base.Init();
            _video.enabled = false;
            _dialog.SetActive(false);
        }

        public void SetupVideo(string id)
        {
            VideoManager.Instance.PrepareVideo(id,_video);
            _video.enabled = false;
            _dialog.SetActive(false);
        }

        public void ScenarioDialogShow(string role , string speech)
        {
            if (!string.IsNullOrEmpty(role) && !string.IsNullOrEmpty(speech))
            {
                _speecher_txt.text = DataManager.Instance.GetDisplayName(role);
                _speech_txt.text = DataManager.Instance.GetDisplayName(speech);
            }
            else
            {
                _speecher_txt.text = "";
                _speech_txt.text = "";
            }
            _video.enabled = true;
            _dialog.SetActive(true);
        }

        public override void Show()
        {
            base.Show();

            Debug.Log("UiScenarioDialog Show");
        }



        public override void Hide()
        {
            base.Hide();

            _video.enabled = false;
            _dialog.SetActive(false);
        }
    }
}
