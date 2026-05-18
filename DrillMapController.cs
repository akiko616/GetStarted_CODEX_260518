using System.Collections.Generic;
using TRAINEE;
using UnityEngine;

namespace DrillSergeant
{
    public class DrillMapController : MapControllerBase
    {
        private List<GameObject> _floors = new List<GameObject>();

        // [todo]사실상 삭제예정
        //private Dictionary<string, ERoomState> _serverElementStates = new Dictionary<string, ERoomState>();

        private Dictionary<string, OccationalSprite> _occationalSprites = new Dictionary<string, OccationalSprite>();

        // 빌더가 완성된 프리팹을 자식으로 넣을 수 있게 접근 허용
        public Transform BuildingRoot => _buildingRoot;
        public Transform TerrainRoot => _terrainRoot;
        public Transform StartPointRoot => _startPonitRoot;

        public List<GameObject> Floors => _floors;

        //public Dictionary<string, ERoomState> ServerElementStates { get => _serverElementStates; }

        // 미사용(보류)
        //public Dictionary<string, OccationalSprite> OccationalSprites { get => _occationalSprites; set => _occationalSprites = value; }

        public override void Init()
        {
            LoadingHelper.RegisterLoading(new Loading(LoadingEquipAsync));

            Debug.Log($"게임이벤트매니저 {DrillManager.Instance.GameEventManager}");

            DrillManager.Instance.GameEventManager.Subscribe(EEventType.Interaction, this);
            DrillManager.Instance.GameEventManager.Subscribe(EEventType.UpdateView, this);
            DrillManager.Instance.GameEventManager.Subscribe(EEventType.Server, this);
        }

        // 씬 언로드 전 대피 작업
        public void ClearMap()
        {
            MonitoringPoolSystem.Instance.ReturnAllToPool();

            if (this.transform.parent != DrillManager.Instance.transform)
            {
                this.transform.SetParent(DrillManager.Instance.transform);
            }

            _views.Clear();
            _models.Clear();
            //_serverElementStates.Clear();
            _floors.Clear();
            _occationalSprites.Clear();

            for (int i = _buildingRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(_buildingRoot.GetChild(i).gameObject);
            }

            if (DrillManager.Instance.GameEventManager != null)
            {
                DrillManager.Instance.GameEventManager.UnSubscribe(EEventType.Interaction, this);
                DrillManager.Instance.GameEventManager.UnSubscribe(EEventType.UpdateView, this);
                DrillManager.Instance.GameEventManager.UnSubscribe(EEventType.Server, this);
            }
        }


        public void InitRegisteredViewsModels()
        {
            _dynamicProp.Clear();

            foreach (MapModelBase model in _models.Values)
            {
                if (model != null)
                {
                    model.Init();
                }
            }

            foreach (var viewPair in _views)
            {
                string viewId = viewPair.Key;
                IMapView iView = viewPair.Value;

                if (iView is MapViewBase mapView)
                {
                    if (iView is TerrainView)
                        continue;

                    mapView.Init(viewId);
                }
            }
        }

        public void ChangeElementState(string id, ERoomState newState)
        {
            bool hasView = _views.TryGetValue(id, out IMapView oldView);
            bool hasModel = _models.ContainsKey(id);

            if (!hasView) return;

            MapViewBase view = oldView as MapViewBase;

            Transform parent = view.transform.parent;
            Vector3 pos = view.transform.position;
            Quaternion rot = view.transform.rotation;
            Vector3 scale = view.transform.localScale;

            RemoveView(id);
            MonitoringPoolSystem.Instance.ReturnToPool(view.gameObject);

            GameObject newObj = MonitoringPoolSystem.Instance.GetElement(id, newState);
            if (newObj == null)
                newObj = MonitoringPoolSystem.Instance.GetElement(id, ERoomState.Normal);

            if (newObj != null)
            {
                newObj.transform.SetParent(parent, false);
                newObj.transform.position = pos;
                newObj.transform.rotation = rot;
                newObj.transform.localScale = scale;
                newObj.SetActive(true);


                // FishNet 연결전에 view.Init 예정
                //MapViewBase newView = newObj.GetComponent<MapViewBase>();
                //if (newView != null)
                //{
                //    newView.Init(id);
                //    RegisterView(id, newView);
                //}
            }

            if (hasModel)
            {
                RemoveModel(id);
                RoomElementData elementData = TRAINEE.DataManager.Instance.GetData<RoomElementData>(EDataType.RoomElementData, id);

                if (elementData != null)
                {
                    ElementModelBase newModel = ModelFactoryHelper.CreateElementModel(elementData);
                    RegisterModel(id, newModel);
                }
            }
        }

        public void RemoveView(string id)
        {
            if (_views.ContainsKey(id)) _views.Remove(id);
        }

        public void RemoveModel(string id)
        {
            if (_models.TryGetValue(id, out MapModelBase model))
            {
                _models.Remove(id);
            }
        }

        public void RegisterOccationalSprite(string rescueeId, OccationalSprite sprite)
        {
            if (!_occationalSprites.ContainsKey(rescueeId))
            {
                _occationalSprites.Add(rescueeId, sprite);
            }
        }

        // index == -1이면 전부끔
        public void ToggleRescueeSprite(string rescueeId, int index)
        {
            if (_occationalSprites.TryGetValue(rescueeId, out OccationalSprite sprite))
            {
                sprite.SetActiveInIndex(index);
            }
        }

        public int CountExceptForNoneRescuee()
        {
            int left = 0;

            foreach (OccationalSprite occation in _occationalSprites.Values)
            {
                if (occation.Representative != -1)
                {
                    left++;
                }
            }

            return left;
        }

        public void AllOccationSetOff()
        {
            foreach (var item in _occationalSprites)
            {
                item.Value.SetActiveInIndex(-1);
            }
        }

        // -1일경우 전체켬 (실제 층 오브젝트에 대한)
        public void SetViewFloor(int whichFloor)
        {

            if (whichFloor == -1)
            {
                for (int i = 0; i < _floors.Count; ++i)
                {
                    _floors[i].SetActive(true);
                }
                return;
            }

            for (int i = 0; i < _floors.Count; ++i)
            {
                if (_floors[i] == null)
                    return;

                _floors[i].SetActive(false);
            }
            if (whichFloor > 0 && whichFloor <= _floors.Count)
            {
                _floors[whichFloor - 1].SetActive(true);
            }
        }
    }
}