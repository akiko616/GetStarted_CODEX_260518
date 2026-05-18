using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace DrillSergeant
{
    public class DirectionIndicator : MonoBehaviour
    {
        [Header("Indicator Settings")]
        [SerializeField] private float _radius = 1.5f;           // 플레이어 기준 원 궤도 반지름
        [SerializeField] private float _yOffset = 0.05f;         // 바닥 텍스처 겹침 방지용
        [SerializeField] private float _solidDuration = 5f;      // 켜져 있는 시간
        [SerializeField] private float _blinkingDuration = 3f;   // 깜박이는 시간

        private Transform _player;           // 내 부모(플레이어 본체)
        private Renderer _arrowRenderer;     // 내 자식(화살표 메쉬)
        private CancellationTokenSource _cts;

        [Header("Test")]
        public Vector3 testTargetPosition;

        [ContextMenu("인디케이터 테스트 실행")]
        public void TestIndicator()
        {
            ShowIndicator(testTargetPosition);
            Debug.Log($"[DirectionIndicator] 테스트 실행: 타겟({testTargetPosition})");
        }

        private void Awake()
        {
            _arrowRenderer = GetComponentInChildren<Renderer>(true);

            if (transform.parent != null)
            {
                _player = transform.parent;
            }
            else
            {
                Debug.LogError("[DirectionIndicator] 부모가 없습니다.");
            }

            _arrowRenderer.gameObject.SetActive(false);
        }

        public void ShowIndicator(Vector3 targetPosition)
        {
            if (_player == null) return;

            _arrowRenderer.gameObject.SetActive(true);

            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
            }
            _cts = new CancellationTokenSource();

            IndicatorRoutineAsync(targetPosition, _cts.Token).Forget();
        }

        private async UniTaskVoid IndicatorRoutineAsync(Vector3 targetPos, CancellationToken token)
        {
            _arrowRenderer.enabled = true;
            float timer = 0f;

            while (timer < _solidDuration)
            {
                if (token.IsCancellationRequested) return;

                UpdatePositionAndRotation(targetPos);

                timer += Time.deltaTime;
                await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, token);
            }

            timer = 0f;
            float blinkSpeed = 10f;

            while (timer < _blinkingDuration)
            {
                if (token.IsCancellationRequested) return;

                UpdatePositionAndRotation(targetPos);

                blinkSpeed += Time.deltaTime * 25f;
                float sinValue = Mathf.Sin(timer * blinkSpeed);
                _arrowRenderer.enabled = (sinValue > 0f);

                timer += Time.deltaTime;
                await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, token);
            }

            _arrowRenderer.enabled = true;
            _arrowRenderer.gameObject.SetActive(false);
        }

        private void UpdatePositionAndRotation(Vector3 targetPos)
        {
            Vector3 dir = targetPos - _player.position;
            dir.y = 0f;

            if (dir.sqrMagnitude > 0.001f)
            {
                dir.Normalize();

                Vector3 calcPos = _player.position + (dir * _radius);
                calcPos.y = _player.position.y + _yOffset;

                transform.position = calcPos;
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}