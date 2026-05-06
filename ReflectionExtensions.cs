using System;
using System.Linq;
using System.Reflection;

namespace OnDemandContracts;

internal static class ReflectionExtensions {
    public static void AppendToCollectionProperty(object target, string memberName, object item) {
        var type = target.GetType();
        var prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        object collection = prop != null ? prop.GetValue(target) : field?.GetValue(target);
        if (collection == null) throw new MissingMemberException(type.FullName, memberName);
        var add = collection.GetType().GetMethods().FirstOrDefault(m => m.Name == "Add" && m.GetParameters().Length == 1);
        if (add == null) throw new MissingMethodException(collection.GetType().FullName, "Add");
        object newCollection = add.Invoke(collection, [item]);
        if (prop != null && prop.CanWrite) prop.SetValue(target, newCollection);
        else if (field != null) field.SetValue(target, newCollection);
        else throw new InvalidOperationException("Cannot write back " + memberName);
    }
    public static T UniversalGetPrivateProperty<T>(object obj, string name) where T : class
    {
        if (obj == null)
            return null;

        var type = obj.GetType();

        while (type != null)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
                return field.GetValue(obj) as T;

            var prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (prop != null)
                return prop.GetValue(obj) as T;

            type = type.BaseType;
        }

        return null;
    }
}

