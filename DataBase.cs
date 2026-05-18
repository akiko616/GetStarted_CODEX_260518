using System;
using UnityEngine;


namespace TRAINEE
{
    public interface IData { string Id { get; set; } }


    public  abstract class DataBase : IData
    {
        [SerializeField]
        private string id;

        public string Id { get => id; set => id = value; }
    }
}
