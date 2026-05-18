using System.Collections.Generic;
using UnityEngine;
using TRAINEE; // DataManager 및 각종 Data 구조체, Enum 참조

namespace DrillSergeant
{
    public static class MinimapSpriteBuilder
    {
        // SortOrder 화이트리스트 딕셔너리 (터레인은 -10으로 별도 고정 처리)
        private static Dictionary<string, int> _whiteListDic = new Dictionary<string, int>()
        {
            { "인테리어", 12 }, { "바닥", 8 }, { "외벽", 14 }, { "측벽", 14 }, { "내벽", 14 },
            { "문", 16 }, { "요구조자", 18 }
        };

        private const string MINIMAP_SPRITE_ROOT = "DrillSergeant/MinimapSprites/";

        #region 일반 엘리먼트 스프라이트 조립
        //public static void AttachAdjustedSprite(GameObject rootElement, string elementId, string state, EBuildingType buildingType)
        //{
        //    MinimapSpriteData spriteData = DataManager.Instance.GetData<MinimapSpriteData>(EDataType.MinimapSpriteData, elementId);

        //    if (spriteData == null)
        //        return;

        //    string buildingName = buildingType.ToString();
        //    string spritePath = $"{MINIMAP_SPRITE_ROOT}{buildingName}/{spriteData.sprite}_{state}";
        //    Sprite targetSprite = Resources.Load<Sprite>(spritePath);

        //    if (targetSprite == null)
        //    {
        //        string fallbackPath = $"{MINIMAP_SPRITE_ROOT}{buildingName}/{spriteData.sprite}_{ERoomState.Normal}";
        //        targetSprite = Resources.Load<Sprite>(fallbackPath);

        //        if (targetSprite == null)
        //        {
        //            Debug.LogWarning($"[MinimapSpriteBuilder] {elementId}의 스프라이트를 찾을 수 없습니다: {spritePath} (Fallback 포함)");
        //            return;
        //        }
        //    }

        //    int sortOrder = 0;
        //    RoomElementData roomData = DataManager.Instance.GetData<RoomElementData>(EDataType.RoomElementData, elementId);
        //    if (roomData != null)
        //    {
        //        string koreanName = DataManager.Instance.GetDisplayName(roomData.displayName);
        //        if (!string.IsNullOrEmpty(koreanName) && _whiteListDic.TryGetValue(koreanName, out int order))
        //        {
        //            sortOrder = order;
        //        }
        //    }

        //    ProcessTarget(rootElement, spriteData.targetfirst, targetSprite, sortOrder, spriteData);

        //    if (!string.IsNullOrEmpty(spriteData.targetsecond) && spriteData.targetsecond != "None" && spriteData.targetsecond != "-")
        //    {
        //        ProcessTarget(rootElement, spriteData.targetsecond, targetSprite, sortOrder, spriteData);
        //    }
        //}

        //private static void ProcessTarget(GameObject rootElement, string targetName, Sprite sprite, int sortOrder, MinimapSpriteData data)
        //{
        //    List<Transform> foundTargets = new List<Transform>();
        //    Transform parentTransform = rootElement.transform;

        //    bool isNone = string.IsNullOrEmpty(targetName) || targetName == "None";

        //    if (isNone)
        //    {
        //        foundTargets.Add(rootElement.transform);
        //        if (rootElement.transform.childCount > 0)
        //            parentTransform = rootElement.transform.GetChild(0);
        //    }
        //    else
        //    {
        //        FindTargetsRecursively(rootElement.transform, targetName, foundTargets);

        //        if (foundTargets.Count == 0)
        //            return;

        //        if (foundTargets.Count == 1)
        //        {
        //            parentTransform = foundTargets[0];
        //        }
        //        else
        //        {
        //            if (rootElement.transform.childCount > 0)
        //                parentTransform = rootElement.transform.GetChild(0);
        //        }
        //    }

        //    Bounds totalBounds = CalculateTotalBounds(foundTargets);
        //    CreateAndSetupSpriteRenderer(parentTransform, sprite, sortOrder, totalBounds, data.rotationx, data.rotationy, data.rotationz);
        //}
        #endregion

        #region 터레인 전용 스프라이트 조립
        //public static void AttachTerrainSprite(GameObject terrainObj, EBuildingType buildingType, ETerrainType terrainType)
        //{
        //    string buildingName = buildingType.ToString();
        //    string terrainName = terrainType.ToString();

        //    string spritePath = $"{MINIMAP_SPRITE_ROOT}{buildingName}/{terrainName}";

        //    Sprite targetSprite = Resources.Load<Sprite>(spritePath);
        //    if (targetSprite == null)
        //    {
        //        Debug.LogWarning($"[MinimapSpriteBuilder] 터레인 스프라이트를 찾을 수 없습니다: {spritePath}");
        //        return;
        //    }

        //    Bounds terrainBounds;
        //    Terrain terrainComp = terrainObj.GetComponentInChildren<Terrain>();
        //    terrainBounds = CalculateTotalBounds(new List<Transform> { terrainObj.transform });

        //    CreateAndSetupSpriteRenderer(terrainObj.transform, targetSprite, -10, terrainBounds, 90f, 0f, 0f);
        //}
        #endregion

        #region 공용 유틸리티 함수
        private static void CreateAndSetupSpriteRenderer(Transform parent, Sprite sprite, int sortOrder, Bounds bounds, float rotX, float rotY, float rotZ)
        {
            GameObject spriteObj = new GameObject($"MinimapSprite_{sprite.name}");
            spriteObj.transform.SetParent(parent, false);
            spriteObj.layer = 22; // Minimap 레이어

            SpriteRenderer sr = spriteObj.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = sortOrder;

            spriteObj.transform.localRotation = Quaternion.Euler(rotX, rotY, rotZ);
            spriteObj.transform.position = new Vector3(bounds.center.x, -0.4f, bounds.center.z);

            MatchScaleToWorldBounds(spriteObj, sr, parent.gameObject, bounds.size);

            // y축 위치 조절(아래로)
        }

        private static void FindTargetsRecursively(Transform current, string targetName, List<Transform> results)
        {
            if (current.name == targetName)
            {
                results.Add(current);
            }

            foreach (Transform child in current)
            {
                FindTargetsRecursively(child, targetName, results);
            }
        }

        private static Bounds CalculateTotalBounds(List<Transform> targets)
        {
            bool hasBounds = false;
            Bounds totalBounds = new Bounds(Vector3.zero, Vector3.zero);

            foreach (Transform target in targets)
            {
                // 1. 일반 메쉬 렌더러 바운드 계산
                Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in renderers)
                {
                    if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer && renderer.enabled)
                    {
                        if (!hasBounds)
                        {
                            totalBounds = renderer.bounds;
                            hasBounds = true;
                        }
                        else
                        {
                            totalBounds.Encapsulate(renderer.bounds);
                        }
                    }
                }

                // 2. 터레인 바운드 계산 (월드 좌표로 변환하여 병합)
                Terrain[] terrains = target.GetComponentsInChildren<Terrain>(true);
                foreach (Terrain terrain in terrains)
                {
                    if (terrain.enabled && terrain.terrainData != null)
                    {
                        Bounds terrainBounds = terrain.terrainData.bounds;
                        terrainBounds.center += terrain.transform.position; // 로컬 -> 월드 좌표 보정

                        if (!hasBounds)
                        {
                            totalBounds = terrainBounds;
                            hasBounds = true;
                        }
                        else
                        {
                            totalBounds.Encapsulate(terrainBounds);
                        }
                    }
                }
            }

            if (!hasBounds && targets.Count > 0)
            {
                totalBounds = new Bounds(targets[0].position, Vector3.one);
            }

            return totalBounds;
        }

        private static void MatchScaleToWorldBounds(GameObject visualObj, SpriteRenderer spriteRenderer, GameObject parentObj, Vector3 targetSize)
        {
            float spriteWidth = spriteRenderer.localBounds.size.x;
            float spriteHeight = spriteRenderer.localBounds.size.y;

            if (spriteWidth > 0 && spriteHeight > 0)
            {
                float targetScaleX = targetSize.x / spriteWidth;
                float targetScaleY = targetSize.z / spriteHeight;

                visualObj.transform.localScale = new Vector3(
                    targetScaleX / parentObj.transform.lossyScale.x,
                    targetScaleY / parentObj.transform.lossyScale.z,
                    1f
                );
            }
        }
        #endregion
    }
}