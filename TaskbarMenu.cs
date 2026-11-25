using System;
using System.Runtime.InteropServices;

namespace DisplayRest;

internal static unsafe class TaskbarMenu
{
    private static readonly Guid DestinationClass = new("77f10cf0-3db5-4966-b520-b7c54fd35ed6");
    private static readonly Guid DestinationList = new("6332debf-87b5-4670-90c0-5e57b408a49e");
    private static readonly Guid ShellLinkClass = new("00021401-0000-0000-c000-000000000046");
    private static readonly Guid ShellLink = new("000214f9-0000-0000-c000-000000000046");
    private static readonly Guid PropertyStore = new("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");
    private static readonly Guid CollectionClass = new("2d3468c1-36a7-43b6-ac24-d3f02fd9607a");
    private static readonly Guid ObjectCollection = new("5632b1a4-e38a-400a-928a-d4cd63230295");
    private static readonly Guid ObjectArray = new("92ca9dcd-5622-4bba-a805-5e9f541bd8c9");

    internal static bool SetExitTask(string appId, string title)
    {
        if (CoInitializeEx(0, 2) < 0) // COINIT_APARTMENTTHREADED
            return false;
        nint list = 0, removed = 0, link = 0, store = 0, collection = 0;
        bool begun = false, committed = false;
        try
        {
            if (CoCreateInstance(in DestinationClass, 0, 1, in DestinationList, out list) < 0 ||
                StringMethod(list, 3, appId) < 0) // SetAppID
                return false;
            var begin = (delegate* unmanaged[Stdcall]<nint, uint*, Guid*, nint*, int>)Table(list)[4];
            uint slots;
            Guid iid = ObjectArray;
            if (begin(list, &slots, &iid, &removed) < 0)
                return false;
            begun = true;

            if (CoCreateInstance(in ShellLinkClass, 0, 1, in ShellLink, out link) < 0 ||
                StringMethod(link, 20, Environment.ProcessPath!) < 0 || // SetPath
                StringMethod(link, 11, "--exit") < 0 || // SetArguments
                Query(link, in PropertyStore, out store) < 0 ||
                !SetTitle(store, title) ||
                CoCreateInstance(in CollectionClass, 0, 1, in ObjectCollection, out collection) < 0 ||
                PointerMethod(collection, 5, link) < 0 || // AddObject
                PointerMethod(list, 7, collection) < 0) // AddUserTasks
                return false;
            committed = NoArguments(list, 8) >= 0; // CommitList
            return committed;
        }
        finally
        {
            if (begun && !committed)
                NoArguments(list, 11); // AbortList
            Release(collection);
            Release(store);
            Release(link);
            Release(removed);
            Release(list);
            CoUninitialize();
        }
    }

    private static bool SetTitle(nint store, string title)
    {
        var key = new PropertyKey { Group = new("f29f85e0-4ff9-1068-ab91-08002b27b3d9"), Id = 2 };
        var set = (delegate* unmanaged[Stdcall]<nint, PropertyKey*, PropertyValue*, int>)Table(store)[6];
        fixed (char* text = title)
        {
            var value = new PropertyValue { Type = 31, String = text }; // VT_LPWSTR
            return set(store, &key, &value) >= 0 && NoArguments(store, 7) >= 0;
        }
    }

    private static int Query(nint instance, in Guid iid, out nint result)
    {
        var query = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Table(instance)[0];
        fixed (Guid* type = &iid)
        fixed (nint* value = &result)
            return query(instance, type, value);
    }

    private static int StringMethod(nint instance, int slot, string value)
    {
        var method = (delegate* unmanaged[Stdcall]<nint, char*, int>)Table(instance)[slot];
        fixed (char* text = value)
            return method(instance, text);
    }

    private static int PointerMethod(nint instance, int slot, nint value) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, int>)Table(instance)[slot])(instance, value);

    private static int NoArguments(nint instance, int slot) =>
        ((delegate* unmanaged[Stdcall]<nint, int>)Table(instance)[slot])(instance);

    private static void Release(nint instance)
    {
        if (instance != 0)
            NoArguments(instance, 2);
    }

    private static nint* Table(nint instance) => *(nint**)instance;

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { internal Guid Group; internal uint Id; }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropertyValue
    {
        [FieldOffset(0)] internal ushort Type;
        [FieldOffset(8)] internal char* String;
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint reserved, uint mode);
    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
}
