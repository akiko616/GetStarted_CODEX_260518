using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;


namespace TRAINEE
{
    public class VideoManager : Singleton<VideoManager>
    {
        private Dictionary<string, VideoChannel> _movieDatas = null;

        private VideoChannel _currentChannel = null;


        public bool IsPlay()
        {
            if(_currentChannel == null)
            {
                return false;
            }
            
            return _currentChannel.IsPlay();
        }

        protected override void Awake()
        {
            base.Awake();
        }
        protected override void Start()
        {
            base.Start();
        }
        protected override void Init()
        {
            base.Init();

            if( _movieDatas == null )
            {
                _movieDatas = new Dictionary<string, VideoChannel>();
                
            }
            else
                _movieDatas.Clear();

            if (_currentChannel != null)
            {
                _currentChannel.Release();
                _currentChannel = null;
            }

            LoadingHelper.RegisterLoading(new Loading(LoadingVideo));
        }

        public void VideoManagerSetup()
        {
            Init();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (_movieDatas != null)
            {
                foreach (var channel in _movieDatas.Values)
                {
                    channel.Release(); // RT 메모리 정리
                }

                _movieDatas.Clear();
            }
        }

        public void PrepareVideo(string id, RawImage targetUi)
        {
            if (_currentChannel != null)
                _currentChannel = null;

            if (!_movieDatas.ContainsKey(id))
            {
                Debug.Log($"찾을 수 없는 Moive Id : {id}");
                return;
            }

            VideoChannel channel = _movieDatas[id];
            VideoPlayer player = targetUi.GetComponent<VideoPlayer>();

            if (player == null)
            {
                player = targetUi.gameObject.AddComponent<VideoPlayer>();
            }

            if (channel != null)
            {
                channel.Init(player,targetUi);
            }

            _currentChannel = channel;
        }

        public void PlayVideo()
        {
            _currentChannel?.Play();

#if !GAMEINSTANCE || !DrillSergeant
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
#endif
        }

        public void StopVideo()
        {
            _currentChannel?.Stop();
        }

        private string GetFullUrl(string fileName)
        {
            string fullUrl = "";
            string extension = ".mp4";
            //if (isServerMode)
            //{
            //    // 서버 주소 결합: http://.../m_00.mp4
            //    finalUrl = $"{_serverBaseUrl}{fileName}{extension}";
            //}
            //else
            //{
            //    // 로컬 스트리밍 주소 결합: file://.../StreamingAssets/m_00.mp4
            //    // StreamingAssetsPath는 플랫폼별로 경로가 다를 수 있으니 주의 필요하지만 PC/Android 기준:
            //    string localPath = Path.Combine(Application.streamingAssetsPath, fileName + extension);
            //    finalUrl = $"file://{localPath}";
            //}

            string localPath = $"{Application.streamingAssetsPath}/{Const.Path.LOCAL_VIDEO_PATH}{fileName}{extension}";
            fullUrl = $"file://{localPath}";

            return fullUrl;


        }

        public async UniTask LoadingVideo(Action<float, string> onProgress, int delaytime)
        {
            List<MovieidData> moives = DataManager.Instance.GetAllData<MovieidData>(EDataType.MovieidData);

            int totalCnt = moives.Count;
            int currentCnt = 0;

            for (int i = 0; i < moives.Count; i++)
            {
                if(GameManager.Instance.GetCurrentScenario == (EScenario)moives[i].scenario)
                {
                    if(_movieDatas != null)
                    {
                        if (_movieDatas.ContainsKey(moives[i].movieid))
                        {
                            Debug.Log($"중복 처리된 아이디 키 값 존재 Key : {moives[i].movieid}, Value : {moives[i].local_movieid}");
                            return;
                        }

                        // 로컬/서버 분기
                        string fileName = moives[i].local_movieid;
                        //string fileName = moives[i].server_movieid;

                        string url = GetFullUrl(fileName);

                        VideoChannel newChannel = new VideoChannel(url);

                        _movieDatas.Add(moives[i].movieid, newChannel);
                    }
                }

                await UniTask.Delay(delaytime);

                currentCnt++;
                float progress = (float)currentCnt/totalCnt;
                onProgress?.Invoke(progress, $"영상 데이터 로드 중...");
            }
        }



    }
}
