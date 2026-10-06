using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace GhostDeck;

/// <summary>
/// The taskbar side of GhostDeck (roadmap #88): the jump list behind a right-click on the
/// taskbar button or on the pinned icon, and the icon Windows gives that pin. Three shell facts
/// shape the code:
/// - a jump list hangs on an Application User Model ID, so the process and the main window carry
///   an explicit one (<see cref="AppId"/>) and the list is written under it;
/// - Windows takes a pinned shortcut's icon from the exe unless the window names another one
///   (System.AppUserModel.RelaunchIconResource), so the chosen icon style is written as an .ico
///   next to the settings and the window points at it;
/// - a pin made before 1.38 carries no ID and would sit next to the running window as a second
///   button, so pinned shortcuts that target this exe are given the ID on start (the same
///   "heal" the autostart task gets when the exe moved).
/// Every entry launches this exe with a CLI command line; with the app running, that unelevated
/// launch hands the command over the pipe (see <see cref="Elevation"/>). Nothing here needs
/// administrator rights, and every call is best-effort: a shell that refuses leaves the app as
/// it was.
/// </summary>
public static class Taskbar
{
    public const string AppId = "Wygodad.GhostDeck";

    /// <summary>One entry of the list: its label, the command line behind it and its icon file.</summary>
    public sealed record Entry(string Label, string Args, string IconPath);

    private static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    // ---------------- icon files ----------------

    /// <summary>The window icon of the active style, as a file the pin and the list can point at.</summary>
    public static string AppIconFile => Path.Combine(AppSettings.Dir, "taskbar.ico");

    /// <summary>A profile's icon in the active style and the profile's colour.</summary>
    public static string ProfileIconFile(ProfileId id) =>
        Path.Combine(AppSettings.Dir, "taskbar-" + Profiles.Get(id).Key.ToLowerInvariant() + ".ico");

    private static string _written = "";

    /// <summary>
    /// (Re)writes the icon files when the style or a profile colour changed, or a file is
    /// missing. True when something was written - the list is then rebuilt so the shell drops
    /// its cached images.
    /// </summary>
    public static bool WriteIcons(AppSettings s)
    {
        string sig = TrayIconFactory.Style + "|" + string.Join(",", Profiles.Order.Select(id => s.ColorFor(id).ToArgb()));
        bool missing = !File.Exists(AppIconFile) || Profiles.Order.Any(id => !File.Exists(ProfileIconFile(id)));
        if (sig == _written && !missing) return false;
        try
        {
            Directory.CreateDirectory(AppSettings.Dir);
            File.WriteAllBytes(AppIconFile, TrayIconFactory.AppIconBytes());
            foreach (var id in Profiles.Order)
                File.WriteAllBytes(ProfileIconFile(id), TrayIconFactory.ProfileIconBytes(s.ColorFor(id)));
            _written = sig;
            NotifyChanged(AppIconFile);
            foreach (var id in Profiles.Order) NotifyChanged(ProfileIconFile(id));
            return true;
        }
        catch { return false; }
    }

    private static void NotifyChanged(string path)
    {
        // the shell caches icons by path - tell it this file is new
        IntPtr p = Marshal.StringToHGlobalUni(path);
        try { SHChangeNotify(0x2000 /* SHCNE_UPDATEITEM */, 0x0005 /* SHCNF_PATHW */, p, IntPtr.Zero); }
        catch { }
        finally { Marshal.FreeHGlobal(p); }
    }

    // ---------------- identity ----------------

    /// <summary>Before the first window: every window of this process belongs to <see cref="AppId"/>.</summary>
    public static void SetProcessId()
    {
        try { SetCurrentProcessExplicitAppUserModelID(AppId); } catch { }
    }

    /// <summary>
    /// Once the main window has a handle: the ID again (the shell reads it per window), and what
    /// a pin made from this window launches and shows - this exe without arguments, named
    /// GhostDeck, with the icon file of the chosen style.
    /// </summary>
    public static void TagWindow(IntPtr hwnd)
    {
        try
        {
            var iid = typeof(IPropertyStore).GUID;
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) != 0 || store == null) return;
            try
            {
                SetString(store, PKEY_AppUserModel_ID, AppId);
                SetString(store, PKEY_RelaunchCommand, "\"" + ExePath + "\"");
                SetString(store, PKEY_RelaunchDisplayName, "GhostDeck");
                SetString(store, PKEY_RelaunchIcon, AppIconFile + ",0");
                store.Commit();
            }
            finally { Marshal.ReleaseComObject(store); }
        }
        catch { }
    }

    /// <summary>
    /// Pinned taskbar shortcuts that target this exe but carry no ID (made before 1.38, or by
    /// hand from the exe) get the ID and the icon file, so they match the running window.
    /// The taskbar reads a changed pin on its next start (sign-in), not at once.
    /// </summary>
    public static void HealPins()
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                      @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
            if (!Directory.Exists(dir)) return;
            foreach (string lnk in Directory.GetFiles(dir, "*.lnk"))
            {
                object? link = null;
                try
                {
                    link = new ShellLink();
                    var sl = (IShellLinkW)link;
                    ((IPersistFile)link).Load(lnk, 0);
                    var sb = new StringBuilder(1024);
                    sl.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                    if (!string.Equals(sb.ToString(), ExePath, StringComparison.OrdinalIgnoreCase)) continue;
                    var store = (IPropertyStore)link;
                    if (GetString(store, PKEY_AppUserModel_ID) == AppId) continue;
                    SetString(store, PKEY_AppUserModel_ID, AppId);
                    store.Commit();
                    if (File.Exists(AppIconFile)) sl.SetIconLocation(AppIconFile, 0);
                    ((IPersistFile)link).Save(lnk, true);
                }
                catch { }
                finally { if (link != null) Marshal.ReleaseComObject(link); }
            }
        }
        catch { }
    }

    // ---------------- the list ----------------

    /// <summary>
    /// Writes the list: a "profiles" category, a "scenes" category and the task group, each
    /// skipped when empty; all empty = the list is removed. An entry the user removed from the
    /// list earlier makes the shell refuse the category, so a refusal clears the list once and
    /// writes it again.
    /// </summary>
    public static void Rebuild(string profilesTitle, IReadOnlyList<Entry> profiles,
                               string scenesTitle, IReadOnlyList<Entry> scenes, IReadOnlyList<Entry> tasks)
    {
        try
        {
            if (profiles.Count == 0 && scenes.Count == 0 && tasks.Count == 0) { Delete(); return; }
            if (TryWrite(profilesTitle, profiles, scenesTitle, scenes, tasks)) return;
            Delete();
            TryWrite(profilesTitle, profiles, scenesTitle, scenes, tasks);
        }
        catch { }
    }

    private static void Delete()
    {
        var list = (ICustomDestinationList)new DestinationList();
        try { list.DeleteList(AppId); }
        catch { }
        finally { Marshal.ReleaseComObject(list); }
    }

    private static bool TryWrite(string profilesTitle, IReadOnlyList<Entry> profiles,
                                 string scenesTitle, IReadOnlyList<Entry> scenes, IReadOnlyList<Entry> tasks)
    {
        var list = (ICustomDestinationList)new DestinationList();
        try
        {
            list.SetAppID(AppId);
            var iid = typeof(IObjectArray).GUID;
            int hr = list.BeginList(out _, ref iid, out IntPtr removed);
            if (removed != IntPtr.Zero) Marshal.Release(removed);
            if (hr < 0) return false;
            if (profiles.Count > 0 && list.AppendCategory(profilesTitle, Collection(profiles)) < 0) { list.AbortList(); return false; }
            if (scenes.Count > 0 && list.AppendCategory(scenesTitle, Collection(scenes)) < 0) { list.AbortList(); return false; }
            if (tasks.Count > 0 && list.AddUserTasks(Collection(tasks)) < 0) { list.AbortList(); return false; }
            list.CommitList();
            return true;
        }
        catch
        {
            try { list.AbortList(); } catch { }
            return false;
        }
        finally { Marshal.ReleaseComObject(list); }
    }

    private static IObjectArray Collection(IReadOnlyList<Entry> entries)
    {
        var col = (IObjectCollection)new EnumerableObjectCollection();
        foreach (var e in entries) col.AddObject(Link(e));
        return (IObjectArray)col;
    }

    private static object Link(Entry e)
    {
        object link = new ShellLink();
        var sl = (IShellLinkW)link;
        sl.SetPath(ExePath);
        sl.SetArguments(e.Args);
        sl.SetWorkingDirectory(Path.GetDirectoryName(ExePath) ?? "");
        sl.SetIconLocation(File.Exists(e.IconPath) ? e.IconPath : ExePath, 0);
        var store = (IPropertyStore)link;
        SetString(store, PKEY_Title, e.Label);
        store.Commit();
        return link;
    }

    // ---------------- property store ----------------

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid Fmtid;
        public uint Pid;
        public PropertyKey(string fmtid, uint pid) { Fmtid = new Guid(fmtid); Pid = pid; }
    }

    // vt, three reserved words, then the union at offset 8 (24 bytes in all on x64)
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Vt;
        [FieldOffset(8)] public IntPtr P;
    }

    private const ushort VT_LPWSTR = 31;

    private static readonly PropertyKey PKEY_Title                = new("F29F85E0-4FF9-1068-AB91-08002B27B3D9", 2);
    private static readonly PropertyKey PKEY_AppUserModel_ID      = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3", 5);
    private static readonly PropertyKey PKEY_RelaunchCommand      = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3", 2);
    private static readonly PropertyKey PKEY_RelaunchIcon         = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3", 3);
    private static readonly PropertyKey PKEY_RelaunchDisplayName  = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3", 4);

    private static void SetString(IPropertyStore store, PropertyKey key, string value)
    {
        var pv = new PropVariant { Vt = VT_LPWSTR, P = Marshal.StringToCoTaskMemUni(value) };
        try { store.SetValue(ref key, ref pv); }   // the store copies the string
        finally { Marshal.FreeCoTaskMem(pv.P); }
    }

    private static string? GetString(IPropertyStore store, PropertyKey key)
    {
        store.GetValue(ref key, out var pv);
        try { return pv.Vt == VT_LPWSTR && pv.P != IntPtr.Zero ? Marshal.PtrToStringUni(pv.P) : null; }
        finally { PropVariantClear(ref pv); }
    }

    // ---------------- shell interop ----------------

    [DllImport("shell32.dll")]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    [ComImport, Guid("77F10CF0-3DB5-4966-B520-B7C54FD35ED6")]
    private class DestinationList { }

    [ComImport, Guid("2D3468C1-36A7-43B6-AC24-D3F02FD9607A")]
    private class EnumerableObjectCollection { }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, Guid("6332DEBF-87B5-4670-90C0-5E57B408A49E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        [PreserveSig] int BeginList(out uint minSlots, ref Guid riid, out IntPtr removed);
        [PreserveSig] int AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string category, IObjectArray items);
        void AppendKnownCategory(int category);
        [PreserveSig] int AddUserTasks(IObjectArray items);
        void CommitList();
        void GetRemovedDestinations(ref Guid riid, out IntPtr removed);
        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string appId);
        void AbortList();
    }

    [ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        void GetCount(out uint count);
        void GetAt(uint index, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object item);
    }

    [ComImport, Guid("5632B1A4-E38A-400A-928A-D4CD63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectCollection
    {
        // IObjectArray
        void GetCount(out uint count);
        void GetAt(uint index, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object item);
        // IObjectCollection
        void AddObject([MarshalAs(UnmanagedType.IUnknown)] object item);
        void AddFromArray(IObjectArray source);
        void RemoveObjectAt(uint index);
        void Clear();
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr findData, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out ushort hotkey);
        void SetHotkey(ushort hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int cch, out int icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pathRel, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }
}
