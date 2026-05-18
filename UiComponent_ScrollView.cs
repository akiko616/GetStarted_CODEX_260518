using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

public class UiComponent_ScrollView : UIComponent
{
    public RectTransform ContentsRect => contentsPanel;
    
    private RectTransform contentsPanel;

    private ScrollRect _scrollRect;
    public int maxItemCount = 50;

    private Dictionary<GameObject, Queue<GameObject>> _poolDictionary = new Dictionary<GameObject, Queue<GameObject>>();

    private Transform _scrollViewItemPool;
    public override void Init()
    {
        base.Init();

        _scrollRect = GetComponent<ScrollRect>();
        if (contentsPanel == null) contentsPanel = _scrollRect.content;

        _scrollViewItemPool = new GameObject("ScrollViewItemPool").transform;
        _scrollViewItemPool.SetParent(transform);
        _scrollViewItemPool.gameObject.SetActive(false);
    }

    protected override void Update()
    {
        base.Update();
    }

    public void ScrollToBottom()
    {
        _scrollRect.verticalNormalizedPosition = 0f;
    }
    public GameObject AddItem(GameObject prefab)
    {
        if (prefab == null) return null;

        if (contentsPanel.childCount >= maxItemCount)
        {
            //제일 위에꺼 (오래된거)
            ReturnItemToPool(contentsPanel.GetChild(0).gameObject);
        }

        if (!_poolDictionary.TryGetValue(prefab, out Queue<GameObject> pool))
        {
            pool = new Queue<GameObject>();
            _poolDictionary[prefab] = pool;
        }

        GameObject item;

        if (pool.Count > 0)
        {
            item = pool.Dequeue();
        }
        else
        {
            item = Instantiate(prefab);

            if (item.TryGetComponent(out IPoolableItem poolable))
            {
                poolable.PoolableItem = prefab;
            }
        }

        item.transform.SetParent(contentsPanel, false);
        item.transform.SetAsLastSibling();
        item.SetActive(true);

        return item;
    }

    private void ReturnItemToPool(GameObject item)
    {
        if (item.TryGetComponent(out IPoolableItem poolable))
        {
            item.transform.SetParent(_scrollViewItemPool, false);
            _poolDictionary[poolable.PoolableItem].Enqueue(item);
        }
        else
        {
            Debug.LogError($"{item.name}은 풀링 가능한 데이터가 아님");
            Destroy(item);
        }
    }

    public void ReturnAllToPool()
    {
        for (int i = contentsPanel.childCount - 1; i >= 0; i--)
        {
            ReturnItemToPool(contentsPanel.GetChild(i).gameObject);
        }
    }

    public void Clear()
    {
        foreach (Transform child in contentsPanel) Destroy(child.gameObject);
        foreach (Transform child in _scrollViewItemPool) Destroy(child.gameObject);

        _poolDictionary.Clear();
    }

    protected void UpdateScroll()
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(ContentsRect);
        ScrollToBottom();
    }
}


