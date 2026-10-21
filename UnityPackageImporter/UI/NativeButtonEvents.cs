using System;
using FrooxEngine;
using FrooxEngine.UIX;

namespace UnityPackageImporter.UI;

internal static class NativeButtonEvents
{
    // Publicizer exposes both the CLR event and its backing field with the same name.
    public static void Pressed(Button button, ButtonEventHandler handler)
    {
        var addMethod = FrooxInternalBridge.LocalPressedAddMethod;
        if (addMethod == null) throw new NotSupportedException(EngineCompatibilityCheck.MissingMemberMessage("Button.LocalPressed (add accessor)"));
        addMethod.Invoke(button, new object[] { handler });
    }
}
