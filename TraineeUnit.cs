using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using System.Linq;
using System.Collections.Generic;

namespace DrillSergeant
{
    public class TraineeUnit : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private TextMeshProUGUI _traineeTitle;
        [SerializeField] private Image _stateImg;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private RectTransform _rect;
        [SerializeField] private int _myFixedIndex;
        [SerializeField] private Sprite[] _traineeBackgrounds;
        [SerializeField] private Transform _originalParent;
        

        private string _thisTraineeID = null;
        private TraineeEquip _traineeEquip = TraineeEquip.None;
        private string _traineeRole;
        private string _reservedSeatNum;

        private TraineeConnection _traineeConnection = TraineeConnection.Disconnected;
        private Canvas _canvas;

        public TraineeValues traineeValues = new()
        {
            isDeployed = false,
            isDragEnabled = false,
            lastPartyId = -1,
            lastRole = null,
        };

        public string ThisTraineeID { get => _thisTraineeID; }
        public string TraineeTitle { get => _traineeTitle.text; }

        public string TraineeRole { get => _traineeRole; set => _traineeRole = value; }

        public string ReservedSeatNum { get => _reservedSeatNum; set => _reservedSeatNum = value; }
        public TraineeConnection TraineeConnection
        {
            get => _traineeConnection;
            set
            {
                _traineeConnection = value;

                switch (_traineeConnection)
                {
                    case TraineeConnection.Disconnected:
                        ReturnHome();
                        traineeValues.isDragEnabled = false;
                        _stateImg.sprite = TraineeEquipSorting()[0];
                        ArrangeTraineeUnits();
                        break;
                    case TraineeConnection.Connected:
                        traineeValues.isDragEnabled = true;
                        _stateImg.sprite = TraineeEquipSorting()[1];
                        ArrangeTraineeUnits();
                        break;
                }
            }
        }

        public void TraineeSetup()
        {
            _canvas = GetComponentInParent<Canvas>();

            if (transform.parent.name == "TraineeBox")
                _originalParent = transform.parent;
        }

        public void SetInitialInfo(string id, TraineeEquip equip)
        {
            _thisTraineeID = id;
            _traineeEquip = equip;
        }

        // 클릭하여 드래그를 시작하는 순간
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (traineeValues.isDragEnabled == false || _traineeConnection == TraineeConnection.Disconnected)
                return;

            VacantBox currentBox = GetComponentInParent<VacantBox>();
            if (currentBox != null)
            {
                currentBox.TraineeGetsOff(this);
            }

            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.alpha = 0.6f;

            transform.SetParent(_canvas.transform, true);
        }

        // 마우스 드래그중
        public void OnDrag(PointerEventData eventData)
        {
            if (traineeValues.isDragEnabled == false || _traineeConnection == TraineeConnection.Disconnected)
                return;

            transform.position = eventData.position;
        }

        // 마우스 버튼 뗌
        public void OnEndDrag(PointerEventData eventData)
        {
            if (traineeValues.isDragEnabled == false || _traineeConnection == TraineeConnection.Disconnected)
                return;

            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.alpha = 1.0f;

            if (GetComponentInParent<VacantBox>() == null)
            {
                ReturnHome();
            }
        }

        // 배치가 취소되었거나 드래그 실패 했을때
        public void ReturnHome()
        {
            VacantBox currentBox = GetComponentInParent<VacantBox>();
            if (currentBox != null)
            {
                bool isManualDrag = (_traineeConnection == TraineeConnection.Connected);
                currentBox.TraineeGetsOff(this, isManualDrag);
                Debug.Log($"현재Unit의 정보 체크(Party) {currentBox.DrillUnit}");
            }

            transform.SetParent(_originalParent);
            ArrangeTraineeUnits();

            TraineeMovementStatus(null);            

            traineeValues.isDeployed = false;
            traineeValues.lastPartyId = -1;
            traineeValues.lastRole = null;

            Debug.Log($"지금 returnToHome 발동됐습니다.  발동된훈련생: {_thisTraineeID}");
        }

        public void ArrangeTraineeUnits()
        {
            List<TraineeUnit> traineeList = new List<TraineeUnit>();

            foreach (Transform child in _originalParent)
            {
                if (child.TryGetComponent(out TraineeUnit unit))
                {
                    traineeList.Add(unit);
                }
            }
            List<TraineeUnit> sortedList = traineeList.OrderBy(u => u._myFixedIndex).ToList();

            for (int i = 0; i < sortedList.Count; ++i)
            {
                sortedList[i].transform.SetSiblingIndex(i);
            }
        }

        private Sprite[] TraineeEquipSorting()
        {
            int index = 0;

            if (_traineeEquip == TraineeEquip.None)
                index = 0;
            else if (_traineeEquip == TraineeEquip.PC)
                index = 2;
            else
                index = 4;

            return new Sprite[2] { _traineeBackgrounds[index], _traineeBackgrounds[index + 1] };
        }

        public void TraineeMovementStatus(VacantBox targetBox = null)
        {
            int currentPartyId = targetBox != null ? targetBox.DrillUnit.PartyId : -1;
            string currentRole = targetBox != null ? targetBox.RoleText : null;

            MovementStatus status = MovementStatus.None;

            if (traineeValues.lastPartyId == -1 && currentPartyId != -1) status = MovementStatus.Join;
            else if (traineeValues.lastPartyId != -1 && currentPartyId == -1) status = MovementStatus.Leave;
            else if (traineeValues.lastPartyId != -1 && currentPartyId != -1 && traineeValues.lastPartyId != currentPartyId) status = MovementStatus.Move;
            else if (traineeValues.lastPartyId != -1 && traineeValues.lastPartyId == currentPartyId && traineeValues.lastRole != currentRole) status = MovementStatus.ChangeRole;


            DeploymentHandler handler = GetComponentInParent<DeploymentHandler>();
            if (handler != null && status != MovementStatus.None)
            {
                // Role은 한글형이 아닌 RoleId 숫자로 서버에 발송한다
                handler.ProcessTraineeMovement(_thisTraineeID, status, traineeValues.lastPartyId, currentPartyId, targetBox.RoleId);
            }

            traineeValues.lastPartyId = currentPartyId;
            traineeValues.lastRole = currentRole;
        }

    }


    public struct TraineeValues
    {
        public bool isDragEnabled; // 드래그가능불가능
        public bool isDeployed;  // vacantBox에 놓여졌는지
        public int lastPartyId;    // 이전 파티 ID (Base는 -1)
        public string lastRole;   // 이전 역할
    }

    public enum TraineeConnection
    {
        None,
        Disconnected,
        Connected
    }

    public enum TraineeEquip
    {
        None,
        PC,
        VR_PC,
        VR_TREADMILL
    }

    public enum MovementStatus
    {
        None,
        Join,
        Leave,
        Move,
        ChangeRole
    }
}