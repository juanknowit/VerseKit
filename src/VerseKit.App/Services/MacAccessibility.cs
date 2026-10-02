using System.Runtime.InteropServices;

namespace VerseKit.App.Services;

/// <summary>
/// Reads macOS accessibility display preferences (System Settings →
/// Accessibility → Display) via the Objective-C runtime.
/// </summary>
public static class MacAccessibility
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIntPtr(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendBool(IntPtr receiver, IntPtr selector);

    /// <summary>
    /// True when the user has "Reduce motion" turned on. Motion should then
    /// fall back to plain cross-fades. Returns false off macOS or on failure.
    /// </summary>
    public static bool ShouldReduceMotion()
    {
        if (!OperatingSystem.IsMacOS())
            return false;

        try
        {
            var workspace = SendIntPtr(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace"));
            return workspace != IntPtr.Zero
                && SendBool(workspace, sel_registerName("accessibilityDisplayShouldReduceMotion"));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }
}
