using TRAINEE;
using UnityEngine;

public abstract class UIComponent : MonoBehaviour
{

    public virtual void Awake()
    {
        Init();
    }
    public virtual void Init() 
    {
        var uiBase = GetComponentInParent<UiBase>();
        uiBase?.UIComponentRegister(this);
    }
    protected virtual void Update()
    {

    }
}
