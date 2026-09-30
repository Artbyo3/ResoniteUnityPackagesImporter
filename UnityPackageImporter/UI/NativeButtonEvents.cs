using System;
using System.Reflection;
using FrooxEngine;
using FrooxEngine.UIX;

namespace UnityPackageImporter.UI;

internal static class NativeButtonEvents
{
    // Publicizer exposes both the CLR event and its backing field with the same name.
    public static void Pressed(Button button, ButtonEventHandler handler)
    {
        var eventInfo = typeof(Button).GetEvent("LocalPressed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (eventInfo?.GetAddMethod(true) == null) throw new NotSupportedException("Resonite UIX.Button.LocalPressed is unavailable.");
        eventInfo.GetAddMethod(true).Invoke(button, new object[] { handler });
    }
}
