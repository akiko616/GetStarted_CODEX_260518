using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DrillSergeant
{
    public class VacantBox : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private TextMeshProUGUI _innerText;
        [SerializeField] private TextMeshProUGUI _outerText;
        [SerializeField] private CanvasGroup _boxAlphaGroup;
        [SerializeField] private Transform _benchMark;

        private string _roleId = null;
        private bool _isAiPlayer = false;
        private DrillUnit _drillUnit = null;
        private TraineeUnit _occupied = null;

        public TraineeUnit Occupied { get => _occupied; set => _occupied = value; }
        public DrillUnit DrillUnit { get => _drillUnit; set => _drillUnit = value; }

        public CanvasGroup CanvasGroup { get => _boxAlphaGroup; }

        public bool IsAiPlayer
        {
            get => _isAiPlayer;
            set => _innerText.text = value ? "Ai" : "공석";
        }

        public string RoleText { get => _outerText.text; set => _outerText.text = value; }

        public string RoleId { get => _roleId; set => _roleId = value; }

        private void Start()
        {
            _drillUnit = GetComponentInParent<DrillUnit>();
        }

        // 박스안으로 들어왔을때
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_isAiPlayer)
                return;

            if (eventData.pointerDrag != null && _occupied == null)
            {
                if (eventData.pointerDrag.TryGetComponent<TraineeUnit>(out var unit))
                {
                    // 드래그 가능한 녀석일 때만 반응
                    if (unit.traineeValues.isDragEnabled)
                        _boxAlphaGroup.alpha = 0.5f;
                }
            }
        }

        // 박스밖으로 나갔을때
        public void OnPointerExit(PointerEventData eventData)
        {
            _boxAlphaGroup.alpha = 1.0f;
        }

        // 박스위에 놓았을때
        public void OnDrop(PointerEventData eventData)
        {
            if (_isAiPlayer)
                return;

            _boxAlphaGroup.alpha = 1.0f;

            if (eventData.pointerDrag == null)
                return;

            if (eventData.pointerDrag.TryGetComponent<TraineeUnit>(out var incomingUnit))
            {
                if (incomingUnit.traineeValues.isDragEnabled)
                {
                    TraineeGetsOn(incomingUnit);
                }
            }
        }

        // 박스안에 유닛 들어감
        public void TraineeGetsOn(TraineeUnit trainee)
        {
            if (trainee.TraineeConnection == TraineeConnection.Disconnected)
                return;

            trainee.transform.SetParent(_benchMark);
            trainee.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
            trainee.traineeValues.isDeployed = true;
            _occupied = trainee;

            if (_drillUnit.PartyId == -1)
                return;

            trainee.TraineeMovementStatus(this);
            trainee.TraineeRole = _outerText.text;
        }

        // 박스에서 유닛 나감
        public void TraineeGetsOff(TraineeUnit leaver, bool sendPacket = true)
        {
            if (_occupied == leaver)
            {
                _occupied = null;
                leaver.TraineeRole = null;
            }
        }
    }
}