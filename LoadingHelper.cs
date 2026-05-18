using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace TRAINEE
{

    public interface ILoading
    {
        public UniTask LoadingAsync(Action<float, string> onProgress,int delaytime);
    }

    public class Loading : ILoading
    {
        private readonly Func<Action<float, string>,int,UniTask> _loading;
        private readonly float _delaytime;
        public Loading(Func<Action<float,string>,int,UniTask> loading)
        {
            _loading = loading;
        }
        public async UniTask LoadingAsync(Action<float, string> onProgress, int delaytime = 0)
        {
            await _loading.Invoke(onProgress, delaytime);
        }
    }
    public static class LoadingHelper
    {
        private static List<ILoading> _loadings = new List<ILoading>();
        private static bool _isRunning = false;
        public static void AllClear()
        {
            _loadings.Clear();
        }
        public static void UnRegisterLoading(ILoading loading)
        {
            for (int i = _loadings.Count - 1; i >= 0; i--)
            {
                if (_loadings[i] == loading)
                {
                    _loadings.RemoveAt(i);
                }
            }
        }

        public static void RegisterLoading(ILoading loading)
        {
            _loadings.Add(loading);
        }
        public static async UniTask<bool> LoadingAsync(Action<float,string> onProgress,int delaytime)
        {

            if (_isRunning)
            {
                return _isRunning;
            }

            _isRunning = true;

            int phaseCount = 1;
            UnityEngine.Debug.Log($"로딩 시작 : {_loadings.Count}");
            while (_loadings.Count > 0)
            {
                ILoading[] currentBatch = _loadings.ToArray();

                _loadings.Clear();

                int totalCnt = currentBatch.Length;

                for (int i = 0; i < totalCnt; i++)
                {
                    await currentBatch[i].LoadingAsync((float progress, string msg) =>
                    {
                        float totalprogress = (i + progress) / (float)totalCnt;

                        onProgress?.Invoke(totalprogress, $"[Phase {phaseCount}] {msg}");
                    }, delaytime);
                }

                phaseCount++;
            }

            onProgress?.Invoke(1, $"  로딩 완료");

            return _isRunning = false;
        }
    }
}
