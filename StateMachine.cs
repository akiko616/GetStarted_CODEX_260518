using UnityEngine;

namespace TRAINEE
{
    public class StateMachine
    {
        public StateBase _currentState  =   null;
        
        public EStateType _currentStateType = EStateType.None;
        public void Init(StateBase newState)
        {
            _currentState = newState;
            _currentStateType = _currentState.StateType;
            _currentState.Enter();
        }

        public void Change(StateBase newState)
        {
            if (_currentState == null)
            {
                Debug.Log("Current State is Null");
                return;
            }

            if(_currentState == newState)
            {
                Debug.Log($"Same State : {newState}");
                return;
            }

            if (_currentState != null)
            {
                _currentState.Exit();
            }

            _currentState = newState;
            _currentStateType = _currentState.StateType;
            _currentState.Enter();
        }

    }
}
