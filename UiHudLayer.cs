using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TRAINEE
{
    public class UiHudLayer : LayerBase, IEvent
    {

        [Header("Equip Icons")]
        [SerializeField] private UIComponent_ImageAnimate _mainAnim;
        [SerializeField] private UIComponent_ImageAnimate _subAnim;

        [Header("Communication Buttons")]
        [SerializeField] private UIComponent_Toggle _radioButton;
        [SerializeField] private UIComponent_Toggle _chatButton;

        [Header("Channel Buttons")]
        [SerializeField] private List<UIComponent_Button> _channelButtons;

        [Header("Communication Panel")]
        [SerializeField] private UIComponent_MovableUI _communicationPanel;
        [SerializeField] private Vector2 ShowPosition;
        [SerializeField] private Vector2 HidePosition;

        [Header("Radio Text")]
        [SerializeField] private GameObject _onRadioText;

        [SerializeField] private UIComponent_HUDChat _chat;

        [SerializeField] private TMP_Text _traniningName = null;

        List<UIComponent> _uiComponents = new();


        bool tempSetupHud = false;
        bool isOpen = false;

        public override void Init()
        {
            base.Init();

            SetupHud();

            _onRadioText.SetActive(false);

            SetupHud();


        }
        private void Update()
        {
            if (!tempSetupHud)
                return;

            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                _chat.AddMessage(new ChatData(ChatChannel.Channel1, ChatType.Mine, "테스트입니다.", System.DateTime.UtcNow));
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                _chat.AddMessage(new ChatData(ChatChannel.Channel1, ChatType.Other, "테스트입니다.", System.DateTime.UtcNow));
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                _chat.AddMessage(new ChatData(ChatChannel.Channel2, ChatType.Mine, "테스트입니다.", System.DateTime.UtcNow));
            }
            else if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                _chat.AddMessage(new ChatData(ChatChannel.Channel2, ChatType.Other, "테스트입니다.", System.DateTime.UtcNow));
            }
        }
        public override void UIComponentRegister(UIComponent component)
        {
            base.UIComponentRegister(component);
            _uiComponents.Add(component);
        }



        public override void Show()
        {
            base.Show();

            SetupHud();
        }

        public void SetupHud()
        {
            InitAddListener();

            _traniningName.text = DataManager.Instance.GetDisplayName(GameManager.Instance.GetCurrentScenarioId);

            Sprite[] sprites = SpriteManager.Instance.GetDefaultButtonIconBackground;

            if (sprites != null && sprites.Length > 0)
            {
                _radioButton.SetupToggleImage(sprites[0], sprites[1]);
                _chatButton.SetupToggleImage(sprites[0], sprites[1]);
            }

            tempSetupHud = true;

            _chat.OnChannelChanged += HandleChannelChanged;
            _chat.ChangeChannel(ChatChannel.Channel1);

        }

        public override void Hide()
        {
            base.Hide();
        }
        private void OnDestroy()
        {
            _chat.OnChannelChanged -= HandleChannelChanged;
        }
        public void OnEvent(GameEvent data)
        {
            switch (data._type)
            {
                case EEventType.Equip:
                    {
                        //TODO : 인벤 데이터 추가되면 ChangeItemImage에 매개로 추가필요
                        if (data._param is EquipEventData equipData)
                        {
                            ChangeItemImage(equipData);
                        }


                        break;
                    }
            }
        }

        private void AnimateChatLogWindow(bool isOn)
        {
            if (isOn)
            {
                _communicationPanel.MoveTo(ShowPosition);
            }
            else
            {
                _communicationPanel.MoveTo(HidePosition);
            }

            isOpen = isOn;

        }

        private void InitAddListener()
        {
            GameEventManager.Instance.Subscribe(EEventType.Equip, this);

            _chatButton.AddToggleListener((t) => AnimateChatLogWindow(t));
            _radioButton.AddPressListener(() => RadioStart(), () => RadioExit());

            for (int i = 0; i < _channelButtons.Count; i++)
            {
                int index = i;
                _channelButtons[i].AddClickListener(() =>
                {
                    _chat.ChangeChannel((ChatChannel)index);
                });
            }
        }




        private void ChangeItemImage(EquipEventData data)
        {

            Sprite[] frames = SpriteManager.Instance.GetItemFrames(data._id);
            if (frames == null) return;

/*                        switch (data.)
                        {
                            case EInventoryType.Main:
                                _mainAnim.Play(frames);
                                break;

                            case EInventoryType.Sub:
                                _subAnim.Play(frames);
                                break;
                            default:
                                Debug.LogError($"{data}의 인벤타입을 찾을 수 없음 : {slotIdx}");
                                break;
                        }*/

        }

        private void HandleChannelChanged(ChatChannel selectedChannel)
        {
            for (int i = 0; i < _channelButtons.Count; i++)
            {
                bool isSelected = i == (int)selectedChannel;
                _channelButtons[i].SelectButton(isSelected);
            }
        }

        private void RadioStart()
        {
            if (!isOpen)
                _chatButton.Click();

            _onRadioText.SetActive(true);
            Debug.Log("RadioStart");
            GameManager.Instance.GetSystem<PlayerSpawnSystem>().OnChangeLocalPlayerState(EStateType.EquipAction,1,true);

        }
        private void RadioExit()
        {
            _onRadioText.SetActive(false);
            Debug.Log("RadioExit");
            GameManager.Instance.GetSystem<PlayerSpawnSystem>().OnLocalPlayerActionExit();
            //GameManager.Instance.GetSystem<PlayerSpawnSystem>().OnChangeLocalPlayerState(EStateType.Idle,1);
        }



    }
}

