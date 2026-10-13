using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace CaravanShuttleFromInventory
{
    /// <summary>
    /// 派系已经丢了的王权许可不再往远行队按钮栏里塞东西。
    ///
    /// 联动关系：<c>Caravan.GetGizmos</c> 先逐个调用
    /// <c>RoyalTitlePermitWorker.GetCaravanGizmos</c>，起飞和加油排在这次循环之后。
    /// 许可上的 <c>faction</c> 存成 null 时，<c>Pawn_RoyaltyTracker.GetFavor</c> 会因为字典键是 null 而抛出，
    /// 整个按钮枚举中断，后面的穿梭机按钮不会画出来。
    ///
    /// 注意：这里跳过的是「派系引用已经没了」的许可按钮，有派系的许可仍走原版。
    /// </summary>
    [HarmonyPatch]
    public static class Patch_SkipPermitGizmoWithoutFaction
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (Type type in typeof(RoyalTitlePermitWorker).Assembly.GetTypes())
            {
                if (!typeof(RoyalTitlePermitWorker).IsAssignableFrom(type))
                    continue;

                MethodInfo method = AccessTools.DeclaredMethod(type, nameof(RoyalTitlePermitWorker.GetCaravanGizmos));
                if (method != null)
                    yield return method;
            }
        }

        public static bool Prefix(Faction faction, ref IEnumerable<Gizmo> __result)
        {
            if (faction != null)
                return true;

            __result = Array.Empty<Gizmo>();
            return false;
        }
    }

    /// <summary>
    /// 把起飞和加油放到远行队按钮的最前面，并在后面的按钮抛错时停住，不再把已经生成的按钮一起丢掉。
    ///
    /// 联动关系：原版 <c>GetLaunchGizmo</c> / <c>GetRefuelGizmo</c> 仍负责按钮内容和禁用原因，
    /// 因此燃料、载重、冷却的判断还在 <see cref="Patch_CanLaunchInCaravan"/> 和
    /// <c>CaravanShuttleUtility.CanLaunchCaravanShuttle</c> 里。
    /// 本方法包在其他模组的 <c>GetGizmos</c> 后缀之外（优先级最低，最后执行）。
    /// 原版后面还会再产出一次同样的起飞和加油，这里按按钮文字丢掉重复的那一次。
    ///
    /// 注意：<c>GizmoGridDrawer.DrawGizmoGridFor</c> 只要枚举中途抛错，就会整栏不画。
    /// 所以穿梭机按钮必须先交出去，后面的异常只能截断剩余按钮。
    /// </summary>
    [HarmonyPatch(typeof(Caravan), nameof(Caravan.GetGizmos))]
    public static class Patch_ShowShuttleGizmosFirst
    {
        private static readonly MethodInfo LaunchGizmo = AccessTools.Method(typeof(Caravan), "GetLaunchGizmo");
        private static readonly MethodInfo RefuelGizmo = AccessTools.Method(typeof(Caravan), "GetRefuelGizmo");

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Caravan __instance, ref IEnumerable<Gizmo> __result)
        {
            IEnumerable<Gizmo> original = __result;
            __result = Enumerate(__instance, original);
        }

        private static IEnumerable<Gizmo> Enumerate(Caravan caravan, IEnumerable<Gizmo> original)
        {
            bool showedShuttle = false;
            if (caravan != null && caravan.IsPlayerControlled && ModsConfig.OdysseyActive && caravan.Shuttle != null && Find.WorldSelector.NumSelectedObjects == 1)
            {
                showedShuttle = true;
                if (Invoke(LaunchGizmo, caravan) is Gizmo launch)
                    yield return launch;
                if (Invoke(RefuelGizmo, caravan) is Gizmo refuel)
                    yield return refuel;
            }

            if (original == null)
                yield break;

            IEnumerator<Gizmo> enumerator;
            try
            {
                enumerator = original.GetEnumerator();
            }
            catch (Exception exception)
            {
                Log.Error("[CaravanShuttleFromInventory] 远行队按钮生成失败。\n" + exception);
                yield break;
            }

            using (enumerator)
            {
                while (true)
                {
                    Gizmo current;
                    try
                    {
                        if (!enumerator.MoveNext())
                            yield break;
                        current = enumerator.Current;
                    }
                    catch (Exception exception)
                    {
                        Log.ErrorOnce("[CaravanShuttleFromInventory] 远行队有一个按钮生成失败，其余按钮继续显示。\n" + exception, 0x5E11E606);
                        yield break;
                    }

                    if (showedShuttle && current is Command command && IsVanillaShuttleCommand(command))
                        continue;

                    yield return current;
                }
            }
        }

        private static object Invoke(MethodInfo method, Caravan caravan)
        {
            try
            {
                return method?.Invoke(caravan, null);
            }
            catch (Exception exception)
            {
                Log.Error("[CaravanShuttleFromInventory] 穿梭机按钮生成失败。\n" + exception);
                return null;
            }
        }

        private static bool IsVanillaShuttleCommand(Command command)
        {
            string label = command.defaultLabel;
            if (label.NullOrEmpty())
                return false;

            return label == "CommandLaunchGroup".Translate() || label == "CommandRefuelShuttleFromCargo".Translate();
        }
    }
}
