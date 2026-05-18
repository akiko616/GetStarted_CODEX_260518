using UnityEngine;
public interface IPoolableItem
{
    GameObject PoolableItem { get; set; }
}
public abstract class UIScrollViewItem<T> : MonoBehaviour, IPoolableItem
{
    public GameObject PoolableItem { get; set; }

    public abstract void SetData(T data);
}
