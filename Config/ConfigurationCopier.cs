using System;
using System.Reflection;

namespace Olympus.Config;

/// <summary>
/// Copies configuration values in-place so rotation services keep stable nested object references.
/// </summary>
internal static class ConfigurationCopier
{
    public static Configuration CreateRotationCopy(Configuration source)
    {
        var copy = new Configuration();
        CopyOnto(copy, source);
        return copy;
    }

    public static void CopyOnto(Configuration target, Configuration source)
    {
        CopyObjectOnto(target, source, typeof(Configuration));
    }

    private static void CopyObjectOnto(object target, object source, Type type)
    {
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || !prop.CanWrite)
                continue;

            var sourceValue = prop.GetValue(source);
            if (IsNestedConfigObject(prop.PropertyType))
            {
                var targetValue = prop.GetValue(target);
                if (targetValue is null)
                {
                    prop.SetValue(target, sourceValue);
                    continue;
                }

                if (sourceValue is not null)
                    CopyObjectOnto(targetValue, sourceValue, prop.PropertyType);
                continue;
            }

            prop.SetValue(target, sourceValue);
        }
    }

    private static bool IsNestedConfigObject(Type type) =>
        type.IsClass
        && type != typeof(string)
        && type.Namespace?.StartsWith("Olympus", StringComparison.Ordinal) == true;
}
