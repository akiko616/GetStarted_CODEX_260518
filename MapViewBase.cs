using System;
using Unity.AI.Navigation;
using UnityEngine;


namespace TRAINEE
{
    public interface IMapView
    {
        public void OnUpdate(float deltaTime);
        public void UpdateView(GameEvent data);
    }
    public abstract class MapViewBase : MonoBehaviour, IMapView
    {
        protected string _id;
        public string GetID { get => _id; }

        public abstract void Init(string id);

        public abstract void UpdateView(GameEvent data);

        public virtual void OnUpdate(float deltaTime)
        {

        }
    }
}
