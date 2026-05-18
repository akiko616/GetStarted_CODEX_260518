using UnityEngine;

public class RelativeLayout : MonoBehaviour
{
    const float ReferenceW = 1920f;
    const float ReferenceH = 1080f;

    public float x;
    public float y;
    public float width;
    public float height;

    RectTransform rect;

    public void SetLayout()
    {
        if (rect == null) rect = GetComponent<RectTransform>();

        rect.localScale = Vector3.one;

        float anchorMinX = x / ReferenceW;
        float anchorMaxX = (x + width) / ReferenceW;

        float anchorMaxY = 1f - (y / ReferenceH);
        float anchorMinY = 1f - ((y + height) / ReferenceH);

        rect.anchorMin = new Vector2(anchorMinX, anchorMinY);
        rect.anchorMax = new Vector2(anchorMaxX, anchorMaxY);

        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void OnValidate()
    {
        SetLayout();
    }
}