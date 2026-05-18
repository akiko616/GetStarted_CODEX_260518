using UnityEngine;
using UnityEngine.Rendering;


namespace TRAINEE
{
    public class EnvironViewBase : MonoBehaviour
    {
        [SerializeField] private Volume _volume;


        public Volume Volume { get => _volume; set => _volume = value; }
    }
}