using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class UIComponent_ImageAnimate : UIComponent
{
    [SerializeField] float frameRate = 10f;
    [SerializeField] Image _targetImage;

    Sprite[] _frames;
    int _currentFrame;
    float _timer;
    bool _isPlaying;
    public override void Init()
    {
        base.Init();
        _targetImage = GetComponent<Image>();
    }
    public void Play(Sprite[] frames)
    {
        if (_targetImage == null || frames == null || frames.Length == 0)
            return;

        _frames = frames;
        _currentFrame = 0;
        _timer = 0f;
        _isPlaying = true;

        _targetImage.sprite = _frames[0];
    }
    public void Stop()
    {
        _isPlaying = false;
    }
    public void SetAnimation(Image image, Sprite[] frames)
    {
        if (image == null || frames == null || frames.Length == 0)
            return;

        image.sprite = frames[0];
    }

    protected override void Update()
    {
        if (!_isPlaying || _frames == null || _frames.Length == 0)
            return;

        _timer += Time.deltaTime;
        float interval = 1f / frameRate;

        if (_timer >= interval)
        {
            _timer -= interval;
            _currentFrame = (_currentFrame + 1) % _frames.Length;
            _targetImage.sprite = _frames[_currentFrame];
        }
    }
}
