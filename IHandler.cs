using UnityEngine;

namespace TRAINEE
{
    public interface IHandler
    {
        public void OnUpdate(float deltaTime);
        public void OnFixedUpdate(float deltaTime);

    }
}
