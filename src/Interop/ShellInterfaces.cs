using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Shhtartup.Interop;

internal static class ShellGuids
{
    public static readonly Guid CLSID_FileOpenDialog = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
    public static readonly Guid IID_IFileDialog = new("42F85136-DB7E-439C-85F1-E4075D135FC8");
    public static readonly Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    public static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-C000-000000000046");
    public static readonly Guid IID_IShellLinkW = new("000214F9-0000-0000-C000-000000000046");

    public const uint FOS_PICKFOLDERS = 0x00000020;
    public const uint FOS_FORCEFILESYSTEM = 0x00000040;
    public const uint FOS_PATHMUSTEXIST = 0x00000800;

    public const uint SIGDN_FILESYSPATH = 0x80058000;

    // IFileDialog.Show's result when the user cancels: HRESULT_FROM_WIN32(ERROR_CANCELLED).
    public const int HRESULT_CANCELLED = unchecked((int)0x800704C7);
}

// Same rules as CoreAudioInterfaces.cs: vtable slots in the exact original order, unused ones still
// declared (as raw pointers), and every method [PreserveSig].

// IFileDialog with its IModalWindow base (Show) flattened in as the first slot. Trailing slots
// after GetResult are never called and omitted.
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
internal partial interface IFileDialog
{
    [PreserveSig] int Show(nint hwndOwner);
    [PreserveSig] int SetFileTypes_Unused(uint cFileTypes, nint rgFilterSpec);
    [PreserveSig] int SetFileTypeIndex_Unused(uint iFileType);
    [PreserveSig] int GetFileTypeIndex_Unused(out uint piFileType);
    [PreserveSig] int Advise_Unused(nint pfde, out uint pdwCookie);
    [PreserveSig] int Unadvise_Unused(uint dwCookie);
    [PreserveSig] int SetOptions(uint fos);
    [PreserveSig] int GetOptions(out uint pfos);
    [PreserveSig] int SetDefaultFolder_Unused(nint psi);
    [PreserveSig] int SetFolder(IShellItem psi);
    [PreserveSig] int GetFolder_Unused(out nint ppsi);
    [PreserveSig] int GetCurrentSelection_Unused(out nint ppsi);
    [PreserveSig] int SetFileName_Unused(nint pszName);
    [PreserveSig] int GetFileName_Unused(out nint pszName);
    [PreserveSig] int SetTitle(string pszTitle);
    [PreserveSig] int SetOkButtonLabel(string pszText);
    [PreserveSig] int SetFileNameLabel_Unused(nint pszLabel);
    [PreserveSig] int GetResult(out IShellItem ppsi);
}

// Trailing GetAttributes/Compare slots are never called and omitted.
[GeneratedComInterface]
[Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
internal partial interface IShellItem
{
    [PreserveSig] int BindToHandler_Unused(nint pbc, in Guid bhid, in Guid riid, out nint ppv);
    [PreserveSig] int GetParent_Unused(out nint ppsi);

    // ppszName is CoTaskMemAlloc'd; the caller frees it.
    [PreserveSig] int GetDisplayName(uint sigdnName, out nint ppszName);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal partial interface IShellLinkW
{
    [PreserveSig] int GetPath_Unused(nint pszFile, int cch, nint pfd, uint fFlags);
    [PreserveSig] int GetIDList_Unused(out nint ppidl);
    [PreserveSig] int SetIDList_Unused(nint pidl);
    [PreserveSig] int GetDescription_Unused(nint pszName, int cch);
    [PreserveSig] int SetDescription(string pszName);
    [PreserveSig] int GetWorkingDirectory_Unused(nint pszDir, int cch);
    [PreserveSig] int SetWorkingDirectory(string pszDir);
    [PreserveSig] int GetArguments_Unused(nint pszArgs, int cch);
    [PreserveSig] int SetArguments_Unused(nint pszArgs);
    [PreserveSig] int GetHotkey_Unused(out ushort pwHotkey);
    [PreserveSig] int SetHotkey_Unused(ushort wHotkey);
    [PreserveSig] int GetShowCmd_Unused(out int piShowCmd);
    [PreserveSig] int SetShowCmd_Unused(int iShowCmd);
    [PreserveSig] int GetIconLocation_Unused(nint pszIconPath, int cch, out int piIcon);
    [PreserveSig] int SetIconLocation(string pszIconPath, int iIcon);
    [PreserveSig] int SetRelativePath_Unused(nint pszPathRel, uint dwReserved);
    [PreserveSig] int Resolve_Unused(nint hwnd, uint fFlags);
    [PreserveSig] int SetPath(string pszFile);
}

// IPersistFile with its IPersist base (GetClassID) flattened in as the first slot. Obtained by casting
// an IShellLinkW (the generated COM wrapper QueryInterfaces for it).
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("0000010B-0000-0000-C000-000000000046")]
internal partial interface IPersistFile
{
    [PreserveSig] int GetClassID_Unused(out Guid pClassID);
    [PreserveSig] int IsDirty_Unused();
    [PreserveSig] int Load_Unused(nint pszFileName, uint dwMode);
    [PreserveSig] int Save(string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
}
