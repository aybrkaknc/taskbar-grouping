namespace TaskbarFolders.Core.Interop;

/// <summary>
/// The one lock every shell call in the process takes. Called from many thread-pool threads at
/// once, <c>SHGetFileInfo</c>/<c>SHGetImageList</c> returned null for 8 of 9 system executables
/// (the blank popup tiles of upstream #27); the installed-apps reads share the same shell state.
/// </summary>
internal static class ShellAccess
{
    /// <summary>Taken around every call into the shell.</summary>
    public static readonly object Lock = new();
}
