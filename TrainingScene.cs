using Cysharp.Threading.Tasks;
using DrillSergeant;
using System;
using UnityEngine;

namespace TRAINEE
{
    public class TrainingScene : SceneBase
    {
        [SerializeField] private GameLogic gameLogic;
        public override void Init()
        {
            base.Init();
            gameLogic.Init();


        }
    }
}
