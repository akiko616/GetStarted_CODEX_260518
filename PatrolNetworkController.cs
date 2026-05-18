using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using FishNet.Object;
using FishNet.Connection;

namespace GameInstancePlugin.Client
{
    /// <summary>
    /// 지정된 NavMesh 영역에서 클라이언트의 요청으로 서버가 랜덤한 순찰 경로를 추출해주고,
    /// 클라이언트가 해당 경로를 따라 이동(NavMeshAgent)하는 스크립트.
    /// 이동 상태(Transform)는 FishNet의 NetworkTransform 컴포넌트를 통해 자동으로 다른 유저에게 동기화됩니다.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class PatrolNetworkController : NetworkBehaviour
    {
        [Header("Patrol Settings")]
        [SerializeField, Tooltip("경로를 탐색할 반경")] 
        private float searchRadius = 20f;
        
        [SerializeField, Tooltip("한 번에 추출할 최소/최대 목적지 개수")] 
        private Vector2Int pointCountRange = new Vector2Int(3, 10);

        private NavMeshAgent _agent;
        private Vector3[] _patrolPoints;
        private int _currentPointIndex = 0;
        private bool _isPatrolling = false;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // 이 오브젝트의 소유권(Owner)을 가진 클라이언트만 조작합니다.
            if (IsOwner)
            {
                Debug.Log("[PatrolNetworkController] 내 소유의 오브젝트입니다. 서버에 패트롤 경로를 요청합니다.");
                CmdRequestPatrolPoints();
            }
            else
            {
                // 다른 유저(옵저버) 화면에서는 자신이 직접 NavMesh로 이동하지 않고,
                // NetworkTransform이 주는 위치 데이터만 받아 부드럽게 보간되도록 Agent를 끕니다.
                _agent.enabled = false;
            }
        }

        private void Update()
        {
            // 소유자가 아니거나 패트롤 중이 아니면 무시
            if (!IsOwner || !_isPatrolling || _patrolPoints == null || _patrolPoints.Length == 0)
                return;

            // 목적지에 도착했는지 검사
            if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance)
            {
                // 다음 목적지로 인덱스 증가
                _currentPointIndex++;

                // 모든 경로를 다 돌았다면 서버에 새 경로 다시 요청 (무한 패트롤)
                if (_currentPointIndex >= _patrolPoints.Length)
                {
                    _isPatrolling = false;
                    Debug.Log("[PatrolNetworkController] 패트롤 경로를 모두 순회했습니다. 새 경로를 요청합니다.");
                    CmdRequestPatrolPoints();
                }
                else
                {
                    // 다음 포인트로 이동 지시
                    _agent.SetDestination(_patrolPoints[_currentPointIndex]);
                }
            }
        }

        /// <summary>
        /// [클라이언트 -> 서버] 
        /// 클라이언트가 서버에게 "내 주변 반경에서 갈 수 있는 3~10개의 랜덤 NavMesh 포인트를 뽑아줘" 라고 요청합니다.
        /// </summary>
        [ServerRpc]
        private void CmdRequestPatrolPoints()
        {
            // 3~10개의 랜덤 개수 결정
            int count = UnityEngine.Random.Range(pointCountRange.x, pointCountRange.y + 1);
            List<Vector3> validPoints = new List<Vector3>();

            for (int i = 0; i < count; i++)
            {
                // 랜덤 방향과 거리 계산
                Vector3 randomDirection = UnityEngine.Random.insideUnitSphere * searchRadius;
                randomDirection += transform.position;

                // 해당 위치에서 가장 가까운 NavMesh 위 지점 찾기
                if (NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, searchRadius, NavMesh.AllAreas))
                {
                    validPoints.Add(hit.position);
                }
            }

            // 결과를 요청한 클라이언트(Owner)에게만 돌려줍니다.
            TargetReceivePatrolPoints(Owner, validPoints.ToArray());
        }

        /// <summary>
        /// [서버 -> 특정 클라이언트]
        /// 서버가 계산한 랜덤 포인트 배열을 요청한 클라이언트에게 전달합니다.
        /// </summary>
        [TargetRpc]
        private void TargetReceivePatrolPoints(NetworkConnection conn, Vector3[] points)
        {
            if (points == null || points.Length == 0)
            {
                Debug.LogWarning("[PatrolNetworkController] 추출된 유효한 NavMesh 포인트가 없습니다. 재요청합니다.");
                CmdRequestPatrolPoints();
                return;
            }

            _patrolPoints = points;
            _currentPointIndex = 0;
            _isPatrolling = true;

            // 첫 번째 목적지로 바로 이동 시작
            _agent.enabled = true; // 소유자니까 무조건 켜져 있어야 함
            _agent.SetDestination(_patrolPoints[_currentPointIndex]);
            
            Debug.Log($"[PatrolNetworkController] 서버로부터 {points.Length}개의 패트롤 포인트를 받았습니다. 이동을 시작합니다.");
        }
    }
}
