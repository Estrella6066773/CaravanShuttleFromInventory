using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace CaravanShuttleFromInventory
{
    /// <summary>
    /// 原版 <c>FindShuttle</c> 没找到时，再查一次迷你化的客运穿梭机。
    ///
    /// 联动关系：<c>Caravan.Shuttle</c> 的缓存脏了才会调用 <c>FindShuttle</c>。
    /// 找到的结果交给原版的起飞按钮、加油按钮和「有穿梭机就不能步行」。
    /// 查找实现在 <see cref="PackedShuttle.Find"/>。
    ///
    /// 注意：后缀排在最后，看到的是其他模组改完的结果。已经有穿梭机时不改；被清空时才用背包里的穿梭机补上。
    /// </summary>
    [HarmonyPatch(typeof(CaravanInventoryUtility), nameof(CaravanInventoryUtility.FindShuttle))]
    public static class Patch_FindShuttle
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Caravan caravan, ref Building_PassengerShuttle __result)
        {
            if (__result != null)
                return;

            __result = PackedShuttle.Find(caravan);
        }
    }

    /// <summary>
    /// 缓存里是空的时候，直接查背包。有的模组把穿梭机放进背包，但没有把 <c>hasShuttleDirty</c> 设为 true，原版就一直沿用旧的空结果。
    ///
    /// 联动关系：这里调用 <see cref="PackedShuttle.Find"/>，不调用 <c>FindShuttle</c>。
    /// <c>FindShuttle</c> 会清空 <c>AllInventoryItems</c> 的静态列表。加油按钮遍历这份列表时会读 <c>Caravan.Shuttle</c>，
    /// 如果这里再进 <c>FindShuttle</c>，列表会在遍历中途被清空。
    /// 找到之后写回 <c>cachedShuttle</c>，并把 <c>hasShuttleDirty</c> 清掉，后面的读取走原版缓存。
    ///
    /// 注意：只在这次读取结果为空时补查。原版已经认到的穿梭机保持不动。没找到时不写缓存，下次物品变动仍由原版重新查。
    /// </summary>
    [HarmonyPatch(typeof(Caravan), nameof(Caravan.Shuttle), MethodType.Getter)]
    public static class Patch_CaravanShuttleGetter
    {
        private static readonly AccessTools.FieldRef<Caravan, Building_PassengerShuttle> CachedShuttle =
            AccessTools.FieldRefAccess<Caravan, Building_PassengerShuttle>("cachedShuttle");

        private static readonly AccessTools.FieldRef<Caravan, bool> ShuttleDirty =
            AccessTools.FieldRefAccess<Caravan, bool>("hasShuttleDirty");

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Caravan __instance, ref Building_PassengerShuttle __result)
        {
            if (__result != null || __instance == null)
                return;

            Building_PassengerShuttle found = PackedShuttle.Find(__instance);
            if (found == null)
                return;

            __result = found;
            CachedShuttle(__instance) = found;
            ShuttleDirty(__instance) = false;
        }
    }

    /// <summary>
    /// 远行队里的客运穿梭机，不因为「尚未装载完成」而禁止起飞。燃料、货舱超重、冷却仍按原版判断。
    ///
    /// 联动关系：远行队起飞按钮先走 <c>CaravanShuttleUtility.CanLaunchCaravanShuttle</c>。
    /// 那里先比较整队质量（人员、背包，扣掉穿梭机自身重量）和穿梭机载重，通过之后才调用
    /// <c>CompLaunchable.CanLaunch</c>。原版 <c>CanLaunch</c> 的第一句是装载阶段必须已经就绪。
    /// 穿梭机只是背包物品时，这个阶段经常是关的，按钮会灰掉，后面的燃料和冷却根本轮不到。
    /// 本后缀只在失败原因恰好是这一句时，改跑燃料、货舱载重和冷却。
    /// <c>CompShuttle.CanLaunch</c> 仍会执行：玩家穿梭机没部署在地图上时，它不要求驾驶员和天气。
    ///
    /// 注意：地图上已部署的穿梭机、不在远行队里的穿梭机，原版结果原样返回。
    /// 任务穿梭机走到最后的 <c>CompShuttle.CanLaunch</c> 时，仍会因为没装载完成被拦住。
    /// </summary>
    [HarmonyPatch(typeof(CompLaunchable), nameof(CompLaunchable.CanLaunch))]
    public static class Patch_CanLaunchInCaravan
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(CompLaunchable __instance, float? overrideFuelLevel, ref AcceptanceReport __result)
        {
            if (__result.Accepted || __instance?.parent is not Building_PassengerShuttle shuttle)
                return;

            if (__result.Reason != "CommandLaunchGroupFailNotLoaded".Translate())
                return;

            if (shuttle.Destroyed || shuttle.GetCaravan() == null)
                return;

            __result = FlightReport(shuttle, __instance, overrideFuelLevel);
        }

        private static AcceptanceReport FlightReport(Building_PassengerShuttle shuttle, CompLaunchable launchable, float? overrideFuelLevel)
        {
            if (shuttle.RefuelableComp == null || shuttle.TransporterComp == null || launchable?.Props == null)
                return "CommandLaunchGroupFailNoFuel".Translate();

            float fuel = overrideFuelLevel ?? shuttle.FuelLevel;
            if (fuel < launchable.Props.minFuelCost)
                return "CommandLaunchGroupFailNoFuel".Translate();

            if (shuttle.TransporterComp.OverMassCapacity)
            {
                return "CommandLaunchGroupFailOverMassCapacity".Translate() + ": " + "MassUsageString".Translate(shuttle.TransporterComp.MassUsage.ToString("F0"), shuttle.TransporterComp.MassCapacity.ToString("F0"));
            }

            int cooldownTicks = launchable.Props.cooldownTicks;
            int remaining = cooldownTicks - Find.TickManager.TicksGame + launchable.lastLaunchTick;
            if (cooldownTicks > 0 && launchable.lastLaunchTick > 0 && remaining > 0)
                return "CommandLaunchGroupCooldown".Translate() + " (" + remaining.ToStringTicksToPeriod() + ")";

            if (shuttle.ShuttleComp != null)
                return shuttle.ShuttleComp.CanLaunch;

            return true;
        }
    }

    /// <summary>
    /// 原版起飞逻辑拿走穿梭机之前，先把迷你化外壳拆开。
    ///
    /// 联动关系：见 <see cref="PackedShuttle.SettleIntoInventory"/>。原版随后仍会自己检查
    /// <c>CanLaunch</c>（燃料、冷却，以及本模组放行装载状态之后剩下的那些条件）并扣除燃料。
    ///
    /// 注意：这是原版方法的前缀，只整理持有关系，不跳过原版起飞。
    /// </summary>
    [HarmonyPatch(typeof(CaravanShuttleUtility), nameof(CaravanShuttleUtility.LaunchShuttle))]
    public static class Patch_LaunchShuttle
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static void Prefix(Caravan caravan)
        {
            if (caravan?.Shuttle == null)
                return;

            PackedShuttle.SettleIntoInventory(caravan, caravan.Shuttle);
        }
    }
}
