using EPOOutline;
using TMPro;
using TRAINEE;
using UnityEngine;
using UnityEngine.UI;

namespace DrillSergeant
{
    public class CameraUnit : MonoBehaviour
    {
        [SerializeField] private Image _bg;
        [SerializeField] private TextMeshProUGUI _name;
        [SerializeField] private TextMeshProUGUI _info;
        [SerializeField] private Button _outlinableBtn;
        private string _id;

        private GameObject _matchingObject;
        private bool _isClickedOn = false;

        public string ID { get => _id; }

        private void Awake()
        {
            _outlinableBtn.onClick.AddListener(OutlinableBtn);
        }

        private void OnDestroy()
        {
            _outlinableBtn.onClick.RemoveListener(OutlinableBtn);
        }


        public void SetCameraUnitInfo(string name, string role, string id)
        {
            _name.text = name;
            _info.text = role;
            _id = id;

            gameObject.SetActive(true);
        }

        private void OutlinableBtn()
        {
            Debug.Log($"[Outlinable] 버튼클릭됨,   훈련생아이디, {_id}");
           
                PlayerSpawnSystem playerSpawnSystem = GameManager.Instance.GetSystem<PlayerSpawnSystem>();
                ControllerBase controllerBase = playerSpawnSystem.GetPlayerById(_id);
                Outlinable outlinable = controllerBase.GetComponent<Outlinable>();

            if (_isClickedOn == false)
            { 
                outlinable.enabled = true;
                _isClickedOn = true;
            }
            else
            { 
                outlinable.enabled = false;
                _isClickedOn = false;
            }
            Debug.Log($"[Outlinable] 버튼클릭 이벤트 완료,   훈련생아이디, {_id}");
        }


    }
}