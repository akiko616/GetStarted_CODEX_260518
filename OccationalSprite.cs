using UnityEngine;

public class OccationalSprite : MonoBehaviour
{
    [SerializeField] private SpriteRenderer[] spriteRenderers;

    private int _representativeSign = 0;

    public int Representative { get => _representativeSign; }
    
    // -1일경우 전부끔
    public void SetActiveInIndex(int index)
    {
        _representativeSign = index;

        foreach (SpriteRenderer spriteRenderer in spriteRenderers)
        {
            spriteRenderer.gameObject.SetActive(false);
        }

        if (index == -1)
            return;

        spriteRenderers[index].gameObject.SetActive(true);
    }

}
