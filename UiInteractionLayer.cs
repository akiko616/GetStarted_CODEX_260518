using Cysharp.Threading.Tasks;
using UnityEngine;

namespace TRAINEE
{
    public class UiInteractionLayer : LayerBase, IEvent
    {
        /// <summary>
        /// UiInteractionPopup이 띄워 질 수 있는 상태인지에 대한 플래그
        /// </summary>
        public bool IsLock;

        public override void Init()
        {
            base.Init();

            //InitAddListener();

            // Pre 로드
            UiManager.Instance.LoadPopup<UiInteractionPopup>(EPopupType.UiInteractionPopup).Forget();
        }

        public void InitAddListener()
        {

        }

        public override void Show()
        {
            base.Show();

            GameEventManager.Instance.Subscribe(EEventType.UI, this);

        }

        public void OnUnRegisterEvent()
        {
            GameEventManager.Instance.UnSubscribe(EEventType.UI, this);
        }

        public override void Hide()
        {
            GameEventManager.Instance.UnSubscribe(EEventType.UI, this);
        }
        private void OnDestroy()
        {
            GameEventManager.Instance.UnSubscribe(EEventType.UI, this);
        }

        public void OnEvent(GameEvent data)
        {
            switch (data._type)
            {
                case EEventType.UI:
                    {
                        if (data._param == null)
                        {
                            UiManager.Instance.Hide(EPopupType.UiInteractionPopup);
                            break;
                        }

                        if (data._param is RescueeEventData rescueeData)
                        {
                            if (rescueeData._rescueeType == ERescueeType.Minor)
                            {
                                //Following 상태이거나 그 이후면 상호작용 팝업을 강제로 숨김!
                                if (rescueeData._currentState >= ERescueeState.Following)
                                {
                                    UiManager.Instance.Hide(EPopupType.UiInteractionPopup);
                                    break;
                                }

                                string btnText = "";
                                switch (rescueeData._currentState)
                                {
                                    case ERescueeState.Undiscovered:
                                        btnText = "요구조자 보고";
                                        break;
                                    case ERescueeState.Discovered:
                                        btnText = "의식 확인";
                                        break;
                                    case ERescueeState.ConsciousChecked:
                                        btnText = "안전지대로 이송";
                                        break;
                                }

                                UiManager.Instance.FindPopup<UiInteractionPopup>(EPopupType.UiInteractionPopup)?.SetupButtonText(btnText);
                            }
                            else if (rescueeData._rescueeType == ERescueeType.Severe)
                            {
                                // 중상자 처리
                            }
                        }


                        if (data._param is DoorEventData)
                        {
                            UiInteractionPopup pop = UiManager.Instance.FindPopup<UiInteractionPopup>(EPopupType.UiInteractionPopup);

                            if (pop != null)
                            {
                                pop.SetupButtonText("문열기");
                            }
                        }

                        if (data._param is DisasterEventData)
                        {
                            UiInteractionPopup pop = UiManager.Instance.FindPopup<UiInteractionPopup>(EPopupType.UiInteractionPopup);

                            if (pop != null)
                            {
                                pop.SetupButtonText("제거");
                            }
                        }

                        if (data._param is EquipEventData equipData)
                        {
                            UiInteractionPopup pop = UiManager.Instance.FindPopup<UiInteractionPopup>(EPopupType.UiInteractionPopup);
                            if (pop != null)
                            {
                                pop.SetupButtonText("아이템 줍기");
                            }
                        }

                        if(data._param is LobbyItemEventData lobbyItemData)
                        {
                            string msg = null;

                            switch (lobbyItemData._type)
                            {
                                case ELobbyItemType.Video:
                                    msg = "훈련";
                                    break;

                                case ELobbyItemType.Text:
                                    msg = "교육";
                                    break;

                            }
                            UiManager.Instance.FindPopup<UiInteractionPopup>(EPopupType.UiInteractionPopup).SetupButtonText(msg);
                        }

                        UiManager.Instance.ShowPopup(EPopupType.UiInteractionPopup).Forget();
                    }
                    break;
            }
        }
    }
}
