using PartyManagerPlugin;
using TRAINEE;
using UnityEngine;

namespace DrillSergeant
{
    public interface ISergeantPopups
    {
        public static ISergeantPopups Active { get; set; }

        public GameObject CamScreen { get; }

        public void Refresh();


    }
}