using System.Text;
using UnityEngine;


namespace Extensions
{
    public static class StringBuilderExtension
    {
        public static string Build(this StringBuilder builder, params object[] parts)
        {
            builder.Clear();
            foreach (var part in parts)
            {
                builder.Append(part);
            }

            return builder.ToString();
        }
    }
}
