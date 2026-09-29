using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using KamiToolKit.Nodes;
using Lumina.Excel.Sheets;

using GearGuide.Base;

namespace GearGuide;

// Does the slow first-time work on a background thread when the plugin loads,
// so the first time the window opens costs what any later open does.
internal static class Prewarm
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static void Run()
    {
        try
        {
            WarmSheets();
            foreach (var assembly in new[] { typeof(Plugin).Assembly, typeof(TextureButtonNode).Assembly })
                Compile(assembly);
        }
        catch (Exception ex)
        {
            Services.Log.Debug(ex, "Prewarm stopped early.");
        }
    }

    // The sheets read when the job profile and stat list are first built.
    private static void WarmSheets()
    {
        foreach (var _ in Services.DataManager.GetExcelSheet<ClassJob>()) { }
        foreach (var _ in Services.DataManager.GetExcelSheet<ClassJobCategory>()) { }
        foreach (var _ in Services.DataManager.GetExcelSheet<EquipRaceCategory>()) { }
        foreach (var _ in Services.DataManager.GetExcelSheet<BaseParam>()) { }
    }

    // JIT-compiles every method up front, otherwise each one is compiled the
    // first time it runs.
    private static void Compile(Assembly assembly)
    {
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }
        foreach (var type in types.Where(t => !t.ContainsGenericParameters))
        {
            foreach (MethodBase method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
            {
                if (method.IsAbstract || method.ContainsGenericParameters) continue;
                try { RuntimeHelpers.PrepareMethod(method.MethodHandle); }
                catch { }
            }
        }
    }
}
