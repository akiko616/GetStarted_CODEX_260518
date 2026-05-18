using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Cysharp.Threading.Tasks;

namespace GameInstancePlugin.Client.MazeGame
{
    /// <summary>
    /// 시드 기반 결정론적 미로 생성기 (일반 MonoBehaviour).
    /// MazeGameManager가 시드를 전달하면 미로를 생성합니다.
    /// Recursive Backtracking 알고리즘으로 미로를 생성합니다.
    /// </summary>
    public class MazeGenerator : MonoBehaviour
    {
        [Header("미로 설정")]
        [SerializeField] private int gridWidth = 20;
        [SerializeField] private int gridHeight = 20;
        [SerializeField] private float cellSize = 4f;
        [SerializeField] private float wallHeight = 3f;
        [SerializeField] private float wallThickness = 0.3f;

        [Header("복잡도")]
        [Tooltip("미로 생성 후 랜덤으로 제거할 벽 비율 (0~1). 높을수록 열린 공간 증가")]
        [SerializeField, Range(0f, 0.5f)] private float wallRemovalRate = 0.25f;

        [Header("머티리얼")]
        [SerializeField] private Material floorMaterial;
        [SerializeField] private Material wallMaterial;

        [Header("NavMesh")]
        [SerializeField] private NavMeshSurface navMeshSurface;

        // 미로 데이터: [x, y, direction] — 0=North, 1=East, 2=South, 3=West
        private bool[,] visited;
        private bool[,,] walls;

        // 방향 벡터 (N, E, S, W)
        private static readonly int[] DX = { 0, 1, 0, -1 };
        private static readonly int[] DY = { 1, 0, -1, 0 };

        /// <summary>미로 생성 및 NavMesh 베이크 완료 여부</summary>
        public bool IsMazeReady { get; private set; }

        /// <summary>현재 미로의 시드 (외부에서 읽기 가능)</summary>
        public int CurrentSeed { get; private set; }

        private GameObject mazeContainer;

        /// <summary>
        /// 시드 기반으로 미로를 생성하고 지오메트리 + NavMesh를 구축합니다.
        /// MazeGameManager에서 호출합니다.
        /// </summary>
        public void BuildMazeFromSeed(int seed)
        {
            CurrentSeed = seed;
            GenerateMaze(seed);
            BuildGeometry();
            BakeNavMeshDelayedAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        /// <summary>
        /// Recursive Backtracking 알고리즘으로 미로 데이터를 생성합니다.
        /// 동일 시드 → 동일 미로 (결정론적)
        /// </summary>
        private void GenerateMaze(int seed)
        {
            var savedState = Random.state;
            Random.InitState(seed);

            visited = new bool[gridWidth, gridHeight];
            walls = new bool[gridWidth, gridHeight, 4];

            for (int x = 0; x < gridWidth; x++)
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    for (int d = 0; d < 4; d++)
                    {
                        walls[x, y, d] = true;
                    }
                }
            }

            RecursiveBacktrack(0, 0);
            RemoveRandomWalls();
            Random.state = savedState;
        }

        private void RecursiveBacktrack(int x, int y)
        {
            visited[x, y] = true;

            // 방향을 랜덤하게 섞기 (Fisher-Yates shuffle)
            int[] directions = { 0, 1, 2, 3 };
            for (int i = 3; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (directions[i], directions[j]) = (directions[j], directions[i]);
            }

            for (int i = 0; i < directions.Length; i++)
            {
                int dir = directions[i];
                int nx = x + DX[dir];
                int ny = y + DY[dir];

                if (nx >= 0 && nx < gridWidth && ny >= 0 && ny < gridHeight && !visited[nx, ny])
                {
                    walls[x, y, dir] = false;
                    walls[nx, ny, (dir + 2) % 4] = false;
                    RecursiveBacktrack(nx, ny);
                }
            }
        }

        /// <summary>
        /// 미로 생성 후 내부 벽을 랜덤으로 제거하여 복잡도를 낮춥니다.
        /// </summary>
        private void RemoveRandomWalls()
        {
            if (wallRemovalRate <= 0f) return;

            var remainingWalls = new List<(int x, int y, int dir)>();

            for (int x = 0; x < gridWidth; x++)
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    if (walls[x, y, 0] && y < gridHeight - 1)
                        remainingWalls.Add((x, y, 0));
                    if (walls[x, y, 1] && x < gridWidth - 1)
                        remainingWalls.Add((x, y, 1));
                }
            }

            int removeCount = Mathf.RoundToInt(remainingWalls.Count * wallRemovalRate);

            for (int i = remainingWalls.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (remainingWalls[i], remainingWalls[j]) = (remainingWalls[j], remainingWalls[i]);
            }

            for (int i = 0; i < removeCount && i < remainingWalls.Count; i++)
            {
                var (wx, wy, dir) = remainingWalls[i];
                int nx = wx + DX[dir];
                int ny = wy + DY[dir];

                walls[wx, wy, dir] = false;
                walls[nx, ny, (dir + 2) % 4] = false;
            }
        }

        /// <summary>
        /// 미로 데이터를 기반으로 바닥과 벽 지오메트리를 생성합니다.
        /// </summary>
        private void BuildGeometry()
        {
            if (mazeContainer)
            {
                Destroy(mazeContainer);
            }

            mazeContainer = new GameObject("MazeGeometry");
            mazeContainer.transform.SetParent(transform);
            mazeContainer.transform.localPosition = Vector3.zero;

            CreateFloor();

            for (int x = 0; x < gridWidth; x++)
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    Vector3 cellCenter = GetCellWorldPosition(x, y);

                    if (walls[x, y, 0])
                    {
                        Vector3 wallPos = cellCenter + new Vector3(0, wallHeight * 0.5f, cellSize * 0.5f);
                        CreateWall(wallPos, new Vector3(cellSize, wallHeight, wallThickness));
                    }

                    if (walls[x, y, 1])
                    {
                        Vector3 wallPos = cellCenter + new Vector3(cellSize * 0.5f, wallHeight * 0.5f, 0);
                        CreateWall(wallPos, new Vector3(wallThickness, wallHeight, cellSize));
                    }
                }
            }

            for (int x = 0; x < gridWidth; x++)
            {
                Vector3 southPos = GetCellWorldPosition(x, 0) + new Vector3(0, wallHeight * 0.5f, -cellSize * 0.5f);
                CreateWall(southPos, new Vector3(cellSize, wallHeight, wallThickness));
            }

            for (int y = 0; y < gridHeight; y++)
            {
                Vector3 westPos = GetCellWorldPosition(0, y) + new Vector3(-cellSize * 0.5f, wallHeight * 0.5f, 0);
                CreateWall(westPos, new Vector3(wallThickness, wallHeight, cellSize));
            }
        }

        private void CreateFloor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(mazeContainer.transform);

            float totalWidth = gridWidth * cellSize;
            float totalHeight = gridHeight * cellSize;
            float centerX = (gridWidth - 1) * cellSize * 0.5f;
            float centerZ = (gridHeight - 1) * cellSize * 0.5f;

            floor.transform.localPosition = new Vector3(centerX, -0.05f, centerZ);
            floor.transform.localScale = new Vector3(totalWidth + 1f, 0.1f, totalHeight + 1f);

            if (floorMaterial)
            {
                floor.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;
            }
        }

        private void CreateWall(Vector3 position, Vector3 scale)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Wall";
            wall.transform.SetParent(mazeContainer.transform);
            wall.transform.localPosition = position;
            wall.transform.localScale = scale;
            wall.isStatic = true;

            if (wallMaterial)
            {
                wall.GetComponent<MeshRenderer>().sharedMaterial = wallMaterial;
            }
        }

        /// <summary>
        /// NavMeshSurface 베이크를 1프레임 지연하여 콜라이더 등록을 보장합니다.
        /// </summary>
        private async UniTask BakeNavMeshDelayedAsync(CancellationToken ct)
        {
            await UniTask.Yield(ct);

            if (navMeshSurface)
            {
                navMeshSurface.BuildNavMesh();
                Debug.Log("[MazeGenerator] NavMesh 베이크 완료.");
            }
            else
            {
                Debug.LogWarning("[MazeGenerator] NavMeshSurface가 할당되지 않았습니다.");
            }

            IsMazeReady = true;
            Debug.Log($"[MazeGenerator] 미로 생성 완료. Seed={CurrentSeed}, Grid={gridWidth}x{gridHeight}");
        }

        /// <summary>
        /// 셀 좌표를 월드 좌표로 변환합니다.
        /// </summary>
        public Vector3 GetCellWorldPosition(int x, int y)
        {
            return transform.position + new Vector3(x * cellSize, 0, y * cellSize);
        }

        /// <summary>
        /// 미로 내부의 랜덤한 셀 중앙 좌표를 반환합니다. (스폰용)
        /// </summary>
        public Vector3 GetRandomSpawnPosition()
        {
            int x = Random.Range(0, gridWidth);
            int y = Random.Range(0, gridHeight);
            Vector3 pos = GetCellWorldPosition(x, y);
            pos.y = 0.5f;
            return pos;
        }
    }
}
