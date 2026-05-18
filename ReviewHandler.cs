using TRAINEE;
using UnityEngine;
using UnityEngine.UI;

public class ReviewHandler : MonoBehaviour
{
    [SerializeField] private GameObject _rowPrefab;
    [SerializeField] private Button _closeBtn;

    [SerializeField] private Sprite[] _rowBackImgs; // Row의 배경이미지

    [SerializeField] private Transform _contentsParent;

    private UiMainExcurtionLayer _mainLayer;

    
    public void Init(UiMainExcurtionLayer layer)
    {
        _mainLayer = layer;
    }

    public void Setup()
    {
        SetReviewList();
        _closeBtn.onClick.AddListener(CloseBtn);
    }

    private void CloseBtn()
    {
        _mainLayer.TurnOnJustOne(_mainLayer.ModeSelectingHandler);
    }

    private void SetReviewList()
    {
        // 무언가 서버로부터 훈련결과 데이터를 받아와서 적용시키기
    }
}
