using System.Reflection;
using HarmonyLib;
using Verse;

namespace CaravanShuttleFromInventory
{
    /// <summary>
    /// 模组入口。RimWorld 启动时跑带 StaticConstructorOnStartup 的静态构造函数，用 PatchAll 挂上本程序集里的 Harmony 补丁。
    ///
    /// 联动关系：远行队有没有穿梭机能力，看 <see cref="RimWorld.Planet.Caravan.Shuttle"/>。
    /// 原版这个属性只认背包里直接放着的 <see cref="Building_PassengerShuttle"/>。
    /// <see cref="Patch_FindShuttle"/> 和 <see cref="Patch_CaravanShuttleGetter"/> 把迷你化、以及缓存没刷新的穿梭机也认进来。
    /// 认进来之后，原版自己的起飞按钮、加油按钮、不能步行，都会跟着出现。
    /// <see cref="Patch_CanLaunchInCaravan"/> 只放行「还没进入装载完成状态」这一条；燃料、载重、冷却仍走原版。
    /// <see cref="Patch_LaunchShuttle"/> 在原版把穿梭机从背包拿走之前，先把迷你化外壳拆掉。
    /// <see cref="Patch_ShowShuttleGizmosFirst"/> 把起飞和加油排到按钮栏最前。
    /// 派系引用已经丢了的王权许可会在原版枚举中途抛错，整栏按钮都不会画；
    /// <see cref="Patch_SkipPermitGizmoWithoutFaction"/> 跳过这种许可。
    ///
    /// 注意：HarmonyId 与 About.xml 的 packageId 相同。必须排在奥德赛之后，否则客运穿梭机的类型还没加载。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class ModEntry
    {
        public const string HarmonyId = "Estrella6066.caravanShuttleFromInventory";

        static ModEntry()
        {
            Harmony harmony = new Harmony(HarmonyId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Message("[CaravanShuttleFromInventory] 远行队背包里的客运穿梭机可以提供穿梭机能力。起飞仍检查燃料、载重和冷却。");
        }
    }
}
