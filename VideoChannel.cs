using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace TRAINEE
{
    public class VideoChannel
    {
        private VideoPlayer _player = null;
        private RawImage _targetUi = null;
        private RenderTexture _targetRt = null;

        private string _url = "";

        public VideoChannel(string url)
        {
            _url = url;

            if (_targetRt == null)
            {
                _targetRt = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
                _targetRt.Create();
            }

        }

        public bool IsPlay()
        {
            bool isPlay = false;
            if (_player == null)
            {
                return false;
            }

            isPlay = _player.isPlaying && (_player.frame > 0);
            return isPlay;

        }

        public void Init(VideoPlayer player, RawImage ui)
        {
            _player = player;
            _targetUi = ui;

            _player.playOnAwake = false;
            _player.isLooping = false;

            _player.source = VideoSource.Url;
            _player.renderMode = VideoRenderMode.RenderTexture;

            _player.url = _url;
            _player.targetTexture = _targetRt;

            _targetUi.texture = _targetRt;
        }

        public void Play()
        {
            if (_player != null && !_player.isPlaying)
            {

                ReSizeTexture();

                _player.Play();
            }

        }

        public void Stop()
        {
            if (_player != null && _player.isPlaying)
            {
                _player?.Stop();

            }
        }

        private void ReSizeTexture()
        {
            if (_targetUi == null)
            {
                Debug.Log($"Target Ui Null Error");
                return;
            }

            int width = (int)_targetUi.rectTransform.rect.width;
            int height = (int)_targetUi.rectTransform.rect.height;
            
            if(width <= 0 || height <= 0)
            {
                Debug.Log($"Target Ui Width or Height Size Zero Error");
                return;
            }

            if (_targetRt.width != width || _targetRt.height != height)
            {
                _targetRt.Release();
                _targetRt.width = width;
                _targetRt.height = height;

                _targetRt.format = RenderTextureFormat.ARGB32;
                _targetRt.filterMode = FilterMode.Bilinear;

                _targetRt.Create();

                _player.targetTexture = _targetRt;
            }

            _targetUi.texture = _targetRt;
        }

        public void Release()
        {
            if(_targetRt != null)
            {
                _targetRt.Release();
                Object.Destroy(_targetRt);
                _targetRt = null;
            }
        }
    }
}
