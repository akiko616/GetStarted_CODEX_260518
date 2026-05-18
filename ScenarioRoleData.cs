using UnityEngine;
using TRAINEE;
using System.Collections.Generic;

namespace DrillSergeant
{
    /// <summary>
    /// 1. 이 코드의 목적: 교관석에서 사용할 로컬데이터인, 롤아이디, 롤스트링데이터, 시나리오아이콘을 효율적으로 관리하기 위함
    /// 2. 핵심 로직 흐름(3줄 이내): 생성시 맨처음 한번 자료를 셋팅, Get함수로 쉽게 가져다 쓰기
    /// 3. 왜 이렇게 구현했는지: 교관석이 실행될때 아래의 자료들은 한번만 셋팅되어야 하기때문에 인스턴스가 생성될때 한번 발동
    /// 4. 리스크: 아직 셋팅되지않은(스프라이트가 없는) 복합시나리오시 스프라이트 아이콘이 없다
    /// 5. 예외: 
    /// </summary>
    public class ScenarioRoleData
    {
        private static ScenarioRoleData _scenarioRoleData;
        public static ScenarioRoleData Instance => _scenarioRoleData ??= new ScenarioRoleData();

        private Dictionary<string, List<TraineeRoleInfo>> _roleInfoDict = new();
        private Dictionary<string, Sprite> _scenIcons = new();

        private const string SCENARIO_ICON = "6.Image/ScenIcons/";

        public ScenarioRoleData()
        {
            // 트레이니롤 셋팅
            List<ScenarioData> scenarioData = DataManager.Instance.GetAllData<ScenarioData>(EDataType.ScenarioData);
            List<CharEquipData> charEquipData = DataManager.Instance.GetAllData<CharEquipData>(EDataType.CharEquipData);

            foreach (ScenarioData scenario in scenarioData)
            {
                List<TraineeRoleInfo> list = new();

                foreach (CharEquipData charData in charEquipData)
                {
                    if (charData.scenario == scenario.scenario)
                    {
                        TraineeRoleInfo info = new TraineeRoleInfo
                        {
                            roleId = charData.roleid,
                            stringData = DataManager.Instance.GetDisplayName(charData.roleid)
                        };
                        list.Add(info);
                    }
                }
                _roleInfoDict.Add(scenario.Id.ToString(), list); 
            }


            // 시나리오 아이콘 셋팅
            Sprite[] iconSprits = Resources.LoadAll<Sprite>(SCENARIO_ICON);

            foreach (Sprite sprite in iconSprits)
            {
                _scenIcons.Add(sprite.name, sprite);
            }
        }


        public void Init()
        {
            Debug.Log("[ScenarioRoleData] 생성완료 Init");
        }

        public List<TraineeRoleInfo> GetTraineeRoles(string scenarioNum)
        {
            _roleInfoDict.TryGetValue(scenarioNum, out List<TraineeRoleInfo> list);

            return list;
        }

        public Sprite GetScenarioIcon(string scenarioNum)
        {
            _scenIcons.TryGetValue(scenarioNum, out Sprite sprite);

            return sprite;
        }
    }

    public struct TraineeRoleInfo
    {
        public string roleId;
        public string stringData;
    }
}