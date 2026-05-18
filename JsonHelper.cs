using UnityEngine;
using System.Collections.Generic;
using System;
using Newtonsoft.Json;

namespace TRAINEE
{
    public static class JsonHelper
    {
        public static List<T> FromJsonList<T>(string json)
        {
            return JsonConvert.DeserializeObject<List<T>>(json);
        }

        public static T FromJson<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json);
        }
    }
}
