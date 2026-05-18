using UnityEngine;

namespace TRAINEE
{
    public class ItemSystemTester : MonoBehaviour
    {
        [Header("References")]
        public PlayerController player; // 테스트할 플레이어

        [Header("Test Settings")]
        public bool giveItemsOnStart = true;

        private void Start()
        {
            // 1. 테스트 환경 초기화
            SetupTestEnvironment();

            // 2. 플레이어에게 아이템 지급
            if (giveItemsOnStart && player != null)
            {
                // 약간의 딜레이 후 지급 (초기화 순서 보장)
                Invoke(nameof(GiveTestItems), 0.5f);
            }
        }

        private void Update()
        {
            if (player == null || player.EquipHandler == null) return;

            // --- 테스트 컨트롤 (Input System 무시하고 직접 호출) ---

            // 1. 무기 교체 (1:해머, 2:테이프)


            // 2. 사용 (좌클릭)
            //if (Input.GetMouseButtonDown(0)) player.EquipHandler.HandleFire();

            // 3. 아이템 줍기 (F키 - 시뮬레이션)
            if (Input.GetKeyDown(KeyCode.G))
            {
                // 플레이어 앞 2m 반경 내의 ItemPickup 찾아서 상호작용
                //Collider[] hits = Physics.OverlapSphere(player.transform.position, 2f);
                //foreach (var hit in hits)
                //{
                //    var pickup = hit.GetComponent<ItemPickup>();
                //    if (pickup != null)
                //    {
                //        pickup.OnInteract(player);
                //        Debug.Log($"[Test] 아이템 획득 시도: {hit.name}");
                //        break;
                //    }
                //}
            }
        }

        private void GiveTestItems()
        {
            Debug.Log("[Test] 아이템 지급 시작");
            //player.EquipHandler.SetupEquip(EEquipType.Hammer); // 1번 슬롯
            //player.EquipHandler.AddItem("TEST_TAPE");   // 3번 슬롯
            //player.SetupEquip();
        }

        // --- 핵심: 가짜 데이터와 프리팹을 만드는 공장 ---
        private void SetupTestEnvironment()
        {
            Debug.Log("[Test] 테스트 데이터 생성 중...");

            // A. 가짜 뷰(View) 프리팹 만들기 (큐브)
            //GameObject hammerPrefab = CreateDummyPrefab("Dummy_Hammer_View", Color.red, PrimitiveType.Cube);
            //GameObject tapePrefab = CreateDummyPrefab("Dummy_Tape_View", Color.blue, PrimitiveType.Sphere);

            // B. 해머 데이터 생성 (Melee)
 
            //ItemManager.Instance.RegisterItemDataForTest(data);
            


            // 드랍용 프리팹도 같은걸로
            // hammerData.dropPrefab = hammerPrefab; 

            // C. 테이프 데이터 생성 (Deployable)
            //ItemDataSO tapeData = ScriptableObject.CreateInstance<ItemDataSO>();
            //tapeData.id = "Tape";
            //tapeData.itemName = "Tape";
            //tapeData.itemType = EItemType.Deployable;
            //tapeData.equipSlot = EInventoryType.Thrid; // 3번
            //tapeData.range = 5.0f;
            //tapeData.cooldown = 1.0f;
            //tapeData.prefab = tapePrefab;
            // 설치될 월드 오브젝트 (납작한 쿼드)
            //tapeData.worldPrefab = CreateDummyPrefab("Dummy_Tape_Decal", Color.yellow, PrimitiveType.Quad);

            // D. 매니저에 강제 주입
        }

        private GameObject CreateDummyPrefab(string name, Color color, PrimitiveType type)
        {
            // 1. 기본 도형 생성
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.localScale = Vector3.one * 0.3f; // 작게

            // 2. 뷰 컴포넌트 부착 (필수!)
            // 타입에 따라 다른 뷰를 붙일 수도 있음
            if (name.Contains("Hammer")) go.AddComponent<EquipBaseView>(); // 혹은 WeaponBaseView
            else go.AddComponent<EquipBaseView>();

            // 3. 렌더러 색상 변경
            go.GetComponent<Renderer>().material.color = color;

            // 4. 충돌체 설정 (트리거로 만들거나 끄기 - 뷰는 물리 없어야 함)
            var col = go.GetComponent<Collider>();
            if (col) Destroy(col); // 뷰 프리팹엔 콜라이더 없이 시작 (WeaponBaseView가 알아서 함)

            // 5. 프리팹처럼 쓰기 위해 비활성화 해둘 필요는 없으나, 
            // 씬에 덩그러니 남지 않게 어딘가 숨겨두거나 파괴되지 않게 설정
            go.SetActive(false); // 인스턴스화 할 원본이므로 꺼둠
            DontDestroyOnLoad(go);

            return go;
        }
    }
}