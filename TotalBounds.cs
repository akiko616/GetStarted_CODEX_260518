using NaughtyAttributes;
using UnityEngine;

public class TotalBounds : MonoBehaviour
{
    public GameObject bbb;

    [ContextMenu("Debug/Request Bounds size")]
    public void TestBounds()
    {
        GetTotalBounds(bbb);
    }

    public static Bounds GetTotalBounds(GameObject target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
        {
            return new Bounds(target.transform.position, Vector3.zero);
        }

        Bounds totalBounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
        {
            totalBounds.Encapsulate(renderers[i].bounds);
        }

        Debug.Log($"TotalBounds: {totalBounds.size},    Pivot: {totalBounds.size.x / 2}, {totalBounds.size.y / 2}, {totalBounds.size.z / 2}");
        return totalBounds;
    }
}

