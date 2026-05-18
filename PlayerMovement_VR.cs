using TRAINEE;
using UnityEngine;
using UnityEngine.EventSystems;

public class PlayerMovement_VR : MovementBase
{

    float _velocity = 0f;

    CharacterController _characterController;
    Vector3 _moveInput = Vector3.zero;

    public override void Init()
    {
        _characterController = GetComponent<CharacterController>();
    }

    public override void Move(Vector2 direction)
    {
    }

    public override void Look(Vector2 mouseDelta)
    {
    }   

    public override void OnUpdate(float deltaTime)
    {
    }

    public override void OnFixedUpdate(float deltaTime)
    {
    }

    public override void OnLateUpdate(float deltaTime)
    {
    }

}
