using Cysharp.Threading.Tasks;
using UnityEngine;

namespace TRAINEE
{
    public class UiEduDialogResultPopup : UiEduDialogPopup
    {
        public override void Init()
        {
            Debug.Log("UiEduDialogResultPopup Init");
            base.Init();
        }

        public override void Show()
        {
            base.Show();
        }

        public override void Hide()
        {
            base.Hide();
        }

        public void SetupEduDialogResult(string result)
        {

        }

        public override void OnClickConfirmButton()
        {
            Debug.Log("UiEduDialogResultPopup OnClickConfirmButton");
            SceneManager.Instance.ChangeScene(ESceneType.TrainingLobby).Forget();
            base.OnClickConfirmButton();
        }
    }
}
