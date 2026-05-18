using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Triggers;
using RootMotion;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities.UniversalDelegates;
using UnityEngine;
using UnityEngine.U2D;

namespace TRAINEE
{
    public class SpriteManager : Singleton<SpriteManager>
    {
        private Dictionary<string, SpriteAtlas> _atlasCache = new();
        private Dictionary<string, Sprite[]> _animationCache = new();

        protected override void Awake()
        {
            base.Awake();

            Init();
        }
        public void RegisterAtlas(string atlasName, SpriteAtlas atlas)
        {
            if (!_atlasCache.ContainsKey(atlasName))
                _atlasCache.Add(atlasName, atlas);
        }

        public Sprite[] GetItemFrames(string iconID)
        {
            if (_animationCache.TryGetValue(iconID, out var cached)) return cached;
            if (!_atlasCache.TryGetValue(iconID, out var atlas)) return null;

            Sprite[] frames = new Sprite[atlas.spriteCount];
            atlas.GetSprites(frames);

            System.Array.Sort(frames, (a, b) => string.CompareOrdinal(a.name, b.name));

            _animationCache[iconID] = frames;

            return frames;
        }
        public Sprite[] GetDefaultButtonIconBackground => GetItemFrames("ButtonIcon_Background");

        protected override void Init()
        {
            base.Init();

            LoadingHelper.RegisterLoading(new Loading(LoadingAtlasData));


        }
        public async UniTask LoadingAtlasData(Action<float, string> onProgress, int delaytime)
        {
            var datas = DataManager.Instance.GetAllData<SpriteData>(EDataType.SpriteData);

            for (int i = 0; i < datas.Count; i++)
            {
                string iconID = datas[i].IconID;
                SpriteAtlas atlas = LdResources.Load<SpriteAtlas>($"5.IconAtlas/{iconID}");

                if (atlas != null)
                {
                    RegisterAtlas(iconID, atlas);

                    GetItemFrames(iconID);
                }

                onProgress?.Invoke((float)i / datas.Count, $"Loading Sprite: {iconID}");
            }

            await UniTask.Delay(delaytime);
        }
    }
}

