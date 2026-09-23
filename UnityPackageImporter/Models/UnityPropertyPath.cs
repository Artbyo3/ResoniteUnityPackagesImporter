#nullable disable
using System;
using System.Collections;
using System.Globalization;
using System.Reflection;

namespace UnityPackageImporter.Models;

// Unity override paths traverse fields/properties, dictionary keys, and Array.data[n].
internal static class UnityPropertyPath
{
    private const int MaxArraySize = 65536;

    public static bool TrySet(object target, string path, string scalar, object reference, out string error)
    {
        try
        {
            if (target == null || string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Target and property path are required.");
            var segments = path.Split('.');
            if (segments.Length > 128) throw new ArgumentException("Property path is too deep.");
            Set(target, target.GetType(), segments, 0, scalar, reference);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
            FormatException or OverflowException or InvalidCastException or MissingMemberException or TargetInvocationException or MemberAccessException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static object Set(object current, Type type, string[] path, int index, string scalar, object reference)
    {
        if (index == path.Length)
        {
            if (reference != null && type.IsInstanceOfType(reference)) return reference;
            if (type == typeof(string)) return scalar ?? string.Empty;
            var valueType = Nullable.GetUnderlyingType(type) ?? type;
            if (valueType == typeof(bool) && scalar is "0" or "1") return scalar == "1";
            if (valueType.IsEnum) return Enum.Parse(valueType, scalar);
            if (scalar == null || (scalar.Length == 0 && !valueType.IsValueType))
                throw new InvalidOperationException("No compatible object reference was supplied for " + type.Name);
            return Convert.ChangeType(scalar, valueType, CultureInfo.InvariantCulture);
        }

        if (typeof(IList).IsAssignableFrom(type))
        {
            if (path[index] != "Array" || index + 1 >= path.Length)
                throw new ArgumentException("Expected Array.size or Array.data[index].");
            var elementType = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
            var original = (IList)current;
            var operation = path[index + 1];
            int size = original?.Count ?? 0;
            int elementIndex = -1;
            if (operation == "size" && index + 2 == path.Length)
                size = int.Parse(scalar, CultureInfo.InvariantCulture);
            else if (operation.StartsWith("data[", StringComparison.Ordinal) && operation.EndsWith("]", StringComparison.Ordinal))
            {
                elementIndex = int.Parse(operation.Substring(5, operation.Length - 6), CultureInfo.InvariantCulture);
                if (elementIndex < 0 || elementIndex >= MaxArraySize) throw new ArgumentOutOfRangeException(nameof(path));
                size = Math.Max(size, elementIndex + 1);
            }
            else throw new ArgumentException("Unsupported array override: " + operation);
            if (size < 0 || size > MaxArraySize) throw new ArgumentOutOfRangeException(nameof(scalar));

            // Build a replacement so invalid values never remove an existing element.
            var replacement = type.IsArray ? (IList)Array.CreateInstance(elementType, size) : (IList)Activator.CreateInstance(type);
            for (int i = 0; i < size; i++)
            {
                var value = original != null && i < original.Count ? original[i] : Default(elementType);
                if (i == elementIndex) value = Set(value, elementType, path, index + 2, scalar, reference);
                if (type.IsArray) replacement[i] = value;
                else replacement.Add(value);
            }
            return replacement;
        }

        current ??= Activator.CreateInstance(type);
        if (current is IDictionary dictionary)
        {
            var arguments = type.GetGenericArguments();
            if (arguments.Length != 2 || arguments[0] != typeof(string))
                throw new ArgumentException("Only string-keyed Unity dictionaries are supported.");
            var key = path[index];
            dictionary[key] = Set(dictionary.Contains(key) ? dictionary[key] : null, arguments[1], path, index + 1, scalar, reference);
            return current;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        var field = type.GetField(path[index], flags);
        if (field != null && !field.IsInitOnly)
        {
            field.SetValue(current, Set(field.GetValue(current), field.FieldType, path, index + 1, scalar, reference));
            return current;
        }
        var property = type.GetProperty(path[index], flags);
        if (property != null && property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
        {
            property.SetValue(current, Set(property.GetValue(current), property.PropertyType, path, index + 1, scalar, reference));
            return current;
        }
        throw new MissingMemberException(type.Name, path[index]);
    }

    private static object Default(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;
}
