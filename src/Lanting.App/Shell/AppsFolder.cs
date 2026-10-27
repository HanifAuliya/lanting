using System.Runtime.InteropServices;

namespace Lanting.App.Shell;

/// <summary>
/// Mencari nama aplikasi dari AppUserModelId lewat folder "Apps" Windows (shell:AppsFolder).
/// Browser seperti Firefox/Zen memakai id berupa kode hex, misalnya "F0DC299D809B9700" untuk Zen.
/// </summary>
internal static class AppsFolder
{
    private const uint SIGDN_NORMALDISPLAY = 0;
    private static readonly Guid FOLDERID_AppsFolder = new("1e87508d-89c2-42f0-8a7e-645a0f50ca58");
    private static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    /// <summary>Nama yang tampil di menu Start, atau <c>null</c> bila id tidak dikenal.</summary>
    public static string? DisplayName(string appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId))
        {
            return null;
        }

        try
        {
            if (SHCreateItemInKnownFolder(FOLDERID_AppsFolder, 0, appUserModelId, IID_IShellItem, out var item) < 0)
            {
                return null;
            }

            try
            {
                return item.GetDisplayName(SIGDN_NORMALDISPLAY, out var name) < 0 || string.IsNullOrWhiteSpace(name) ? null : name;
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        catch (COMException)
        {
            return null;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemInKnownFolder(
        in Guid knownFolder,
        uint flags,
        string item,
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem shellItem);

    // Hanya method sampai GetDisplayName yang dideklarasikan; urutannya harus sama dengan vtable aslinya.
    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, in Guid handler, in Guid riid, out IntPtr result);

        void GetParent(out IShellItem parent);

        [PreserveSig]
        int GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
    }
}
