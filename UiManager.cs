using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;


namespace TRAINEE
{
    public class UiManager : Singleton<UiManager>
    {
        public static int LAYER_SORTING = 10;
        public static int POPUP_SORTING = 100;
        public static int SUPER_POPUP_SORTING = 1000;

        private Dictionary<ELayerType, LayerBase> cachedLayers = new Dictionary<ELayerType, LayerBase>();
        private Dictionary<EPopupType, PopupBase> cachedPopups = new Dictionary<EPopupType, PopupBase>();
        private Stack<UiBase> _uiStack = new Stack<UiBase>();


        private SceneManager _sceneManager = null;
        private Camera _uiCamera = null;
        private Camera _uiSubCamera = null;
        private LayerBase _uiPrvLayer = null;
        public Camera GetUiCamera { get { return _uiCamera; } }
        public Camera GetUiSubCamera { get { return _uiSubCamera; } }
        protected override void Awake()
        {
            base.Awake();
        }

        protected override void Start()
        {
            base.Start();
            Init();
        }

        protected override void Init()
        {
            _sceneManager = SceneManager.Instance;

            CreateUiCamera(ref _uiCamera, "UiCamera");
            _uiCamera.targetDisplay = 0;
#if DrillSergeant
            if (Display.displays.Length > 1)
            {
                Display.displays[1].Activate();
            }
            CreateUiCamera(ref _uiSubCamera, "UiSubCamera");            
            _uiSubCamera.targetDisplay = 1;
#endif
        }


        private void CreateUiCamera(ref Camera cam, string name)
        {
            cam = new GameObject(name).AddComponent<Camera>();

            // 카메라의 부모 설정
            cam.transform.SetParent(this.transform);
            // 카메라의 위치 설정
            cam.transform.position = new Vector3(0, 1, -10);
            cam.transform.rotation = Quaternion.identity;
            // 카메라의 기본 설정
            cam.fieldOfView = 60;
            cam.clearFlags = CameraClearFlags.Depth;
            cam.backgroundColor = new Color(49 / 255f, 77 / 255f, 121 / 255f, 0f);
            cam.orthographic = false;
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            cam.depth = 0f;
            cam.cullingMask = LayerMask.GetMask("UI", "UI3D");
        }

        private void CreateUiSubCamera()
        {
            _uiCamera = new GameObject("UiSubCamera").AddComponent<Camera>();

            _uiCamera.transform.SetParent(this.transform);
        }

        #region Show & Hide
        public void Show<T>(T ui) where T : UiBase
        {
            ui.Show();
        }

        public void Hide(EPopupType _popup)
        {
            PopupBase popup = null;

            if (cachedPopups.TryGetValue(_popup, out popup))
            {
                if (_uiStack.Count > 0)
                {
                    UiBase stack = _uiStack.Peek();
                    if (stack == popup)
                    {
                        stack.Hide();
                        _uiStack.Pop();
                    }
                }

                popup.gameObject.SetActive(false);
            }
        }

        public void Hide(ELayerType _layer)
        {
            LayerBase layer = null;

            if (cachedLayers.TryGetValue(_layer, out layer))
            {
                if (_uiStack.Count > 0)
                {
                    UiBase stack = _uiStack.Peek();
                    if (stack == layer)
                    {
                        stack.Hide();
                        _uiStack.Pop();
                    }
                }

                layer.gameObject.SetActive(false);
            }
        }

        public void Hide<T>(T ui) where T : UiBase
        {
            if (ui is LayerBase layer)
            {
                _uiPrvLayer = layer;
            }

            if (_uiStack.Count > 0)
            {
                UiBase stack = _uiStack.Peek();
                if (stack == ui)
                {
                    stack.Hide();
                    _uiStack.Pop();
                }
            }

            ui.gameObject.SetActive(false);
        }

        public void Hide(UiBase ui)
        {
            if (ui is LayerBase layer)
            {
                _uiPrvLayer = layer;
            }

            if (_uiStack.Count > 0)
            {
                UiBase stack = _uiStack.Peek();
                if (stack == ui)
                {
                    stack.Hide();
                    _uiStack.Pop();
                }
            }

            ui.gameObject.SetActive(false);
        }

        public void AllHide()
        {
            int count = _uiStack.Count;
            for (int idx = 0; idx < count; ++idx)
            {
                Hide(_uiStack.Peek());
            }

            if (_uiStack.Count > 0)
            {
                _uiStack.Clear();
            }
        }
        #endregion


#region Find Ui
        public T FindLayer<T>(ELayerType _layer) where T : LayerBase
        {
            if (cachedLayers.ContainsKey(_layer))
            {
                return cachedLayers[_layer] as T;
            }

            Debug.LogWarning($"layer not cached. : {_layer}");
            return null;
        }

        public T FindPopup<T>(EPopupType _popup) where T : PopupBase
        {
            if (cachedPopups.ContainsKey(_popup))
            {
                return cachedPopups[_popup] as T;
            }

            Debug.LogWarning($"popup not cached. : {_popup}");
            return null;
        }
        #endregion

        public async UniTask<LayerBase> ShowLayer(ELayerType _layer)
        {
            LayerBase layer = null;

            if (cachedLayers.ContainsKey(_layer))
            {
                layer = cachedLayers[_layer];
            }
            else
            {
                layer = await LoadLayer<LayerBase>(_layer);
            }

            int sortingOrder = _uiStack.Count + LAYER_SORTING;
            layer.SetSortingOrder(sortingOrder);

            Show(layer);

            _uiStack.Push(layer);

            Debug.Log("Stack Count : " + _uiStack.Count);
            return layer;
        }

        public async UniTask<PopupBase> ShowPopup(EPopupType _popup)
        {
            PopupBase popup = null;

            if (cachedPopups.ContainsKey(_popup))
            {
                popup = cachedPopups[_popup];
            }
            else
            {
                popup = await LoadPopup<PopupBase>(_popup);
            }

            int sortingOrder = _uiStack.Count + POPUP_SORTING;
            popup.SetSortingOrder(sortingOrder);

            Show(popup);

            _uiStack.Push(popup);

            if (GetVRCamera != null && popup.TryGetComponent<Canvas>(out Canvas canvas))
            {
                VRCheck(canvas);
            }

            return popup;
        }

        public async UniTask<T> LoadLayer<T>(ELayerType _type, bool _is_builtIn = false) where T : LayerBase
        {
            string path = "";

            if (cachedLayers.ContainsKey(_type))
            {
                Debug.LogError($"This data has already been cached : {_type}");
                return null;
            }

#if DrillSergeant
            path = $"{Const.Path.BUILT_IN_SERGEANTLAYER_PATH}{_type}";
#else
            path = $"{Const.Path.BUILT_IN_LAYER_PATH}{_type}";
#endif
            T layer = await Load<T>(path);
            if(!cachedLayers.ContainsKey(_type))
            cachedLayers.Add(_type, layer);

            return layer;
        }

        public async UniTask<T> LoadPopup<T>(EPopupType _type, bool _is_builtIn = false) where T : PopupBase
        {
            string path = "";

            if (cachedPopups.ContainsKey(_type))
            {
                Debug.LogWarning($"This data has already been cached : {_type}");
                return null;
            }

#if DrillSergeant
            path = $"{Const.Path.BUILT_IN_SERGEANTPOPUP_PATH}{_type}";
#else
            path = $"{Const.Path.BUILT_IN_POPUP_PATH}{_type}";
#endif
            

            T popup = await Load<T>(path);

            cachedPopups.Add(_type, popup);
            return popup;
        }

        //public async UniTask<T> LoadWorldPopup<T>(EPopupType _type, bool _is_builtIn = false) where T : PopupBase, ILdObjectPool
        //{
        //    string path = "";

        //    if (cachedPopups.ContainsKey(_type))
        //    {
        //        Debug.LogError($"This data has already been cached : {_type}");
        //        return null;
        //    }
        //    path = $"{Const.Path.BUILT_IN_POPUP_PATH}{_type}";

        //    T popup = await ObjectPoolManager.Instance.AllocAsync<T>($"{_type}");

        //    cachedPopups.Add(_type, popup);
        //    return popup;
        //}

        public async UniTask<T> Load<T>(string _path) where T : UiBase
        {
            T ui = await LdResources.LoadUI<T>(_path, this.transform);
            if (ui != null)
            {
                if (ui == null)
                {
                    Debug.LogError($"Failed to load UI component of type {typeof(T)} from Addressable: {_path}");
                    return null;
                }

                ui.name = ui.name;
                ui.Init();
                ui.gameObject.SetActive(false);
            }

            return ui;
        }

        public async UniTask<T> Load<T>(string _path, Transform _parent) where T : MonoBehaviour
        {
            T ui = await LdResources.LoadUI<UiBase>(_path, _parent) as T;
            return ui;
        }

        Camera VRcamera = null;

        public Camera GetVRCamera { get { return VRcamera; } }

        public void VRCameraRegisterUI(Camera camera)
        {
            if (_uiCamera.gameObject.activeSelf) _uiCamera.gameObject.SetActive(false);

            VRcamera = camera;

            foreach (var popup in cachedPopups.Values)
            {
                if (popup != null && popup.TryGetComponent<Canvas>(out Canvas canvas))
                {
                    VRCheck(canvas);
                }
            }
        }

        public void VRCheck(Canvas canvas)
        {
            if (GetVRCamera == null) return;

            canvas.worldCamera = GetVRCamera;
            canvas.pixelPerfect = true;
            canvas.gameObject.layer = LayerMask.NameToLayer("UI");
            canvas.renderMode = RenderMode.WorldSpace;

            if (!canvas.gameObject.TryGetComponent<TrackedDeviceGraphicRaycaster>(out _))
                canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

            if (!canvas.gameObject.TryGetComponent<LazyFollow>(out LazyFollow lazyFollow))
                lazyFollow = canvas.gameObject.AddComponent<LazyFollow>();

            RectTransform tmCanvasRect = canvas.GetComponent<RectTransform>();
            lazyFollow.target = GetVRCamera.transform;
            tmCanvasRect.localScale = new Vector3(0.001f, 0.001f, 0.001f);

            if (canvas.name.Contains("UiInteractionPopup"))
            {
                lazyFollow.targetOffset = new Vector3(0f, 0f, 0.4f);
            }
            else
            {
                lazyFollow.targetOffset = new Vector3(0f, 0f, 1.2f);
                lazyFollow.maxDistanceAllowed = 1.5f;
                lazyFollow.rotationFollowMode = LazyFollow.RotationFollowMode.LookAtWithWorldUp;
                lazyFollow.maxAngleAllowed = 25f;
            }

            CanvasScaler canvasScaler = canvas.GetComponent<CanvasScaler>();

            canvasScaler.dynamicPixelsPerUnit = 3f;

        }
    }
}
