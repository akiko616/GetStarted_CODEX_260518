using System;
using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public class UIComponent_Fade : UIComponent
{
    [SerializeField] private CanvasGroup canvasGroup;

    private float _duration;
    private float _t;
    private float _startAlpha;
    private float _targetAlpha;

    private bool _isFading;

    public Action OnFadeEnd;

    public override void Init()
    {
        base.Init();

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
    }

    public void AlphaControl(float value)
    {
        canvasGroup.alpha = value;
    }
    public void Fade(float from, float to, float duration)
    {
        _startAlpha = from;
        _targetAlpha = to;
        _duration = duration;

        _t = 0f;
        _isFading = true;

        canvasGroup.alpha = from;
    }

    protected override void Update()
    {
        if (!_isFading) return;

        _t += Time.unscaledDeltaTime;

        float ratio = _t / _duration;
        canvasGroup.alpha = Mathf.Lerp(_startAlpha, _targetAlpha, ratio);

        if (_t >= _duration)
        {
            canvasGroup.alpha = _targetAlpha;
            _isFading = false;

            OnFadeEnd?.Invoke();
        }
    }
}
