using UnityEngine;

public class UIComponent_MovableUI : UIComponent
{
    [SerializeField] float _speed = 10f;

    RectTransform _rect;
    Vector2 _targetPos;
    bool _isMoving;

    public override void Init()
    {
        base.Init();
        _rect = GetComponent<RectTransform>();
        _targetPos = _rect.anchoredPosition;
    } 


    public void MoveTo(Vector2 target)
    {
        _targetPos = target;
        _isMoving = true;
    }

    protected override void Update()
    {
        if (!_isMoving) return;

        _rect.anchoredPosition = Vector2.Lerp(_rect.anchoredPosition, _targetPos, Time.deltaTime * _speed);

        if (Vector2.Distance(_rect.anchoredPosition, _targetPos) < 0.1f)
        {
            _rect.anchoredPosition = _targetPos;
            _isMoving = false;
        }
    }
}
