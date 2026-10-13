using RimWorld;
using RimWorld.Planet;
using Verse;

namespace CaravanShuttleFromInventory
{
    /// <summary>
    /// 在远行队背包里找出玩家的客运穿梭机。
    ///
    /// 联动关系：<see cref="Patch_FindShuttle"/> 在原版 <c>CaravanInventoryUtility.FindShuttle</c> 没找到时调用这里。
    /// 原版只判断 <c>is Building_PassengerShuttle</c>，迷你化之后背包里是 <see cref="MinifiedThing"/>，原版会漏掉。
    /// 这里不看穿梭机货舱里的货物。自动存储那类模组会把舱内货物追加进物品列表，那些货物不是穿梭机本身。
    ///
    /// 注意：只认玩家客运穿梭机（<see cref="CompShuttle.IsPlayerShuttle"/>）。任务穿梭机、许可穿梭机不从这里获得远行队飞行能力。
    /// </summary>
    public static class PackedShuttle
    {
        public static Building_PassengerShuttle Find(Caravan caravan)
        {
            if (caravan == null)
                return null;

            foreach (Pawn pawn in caravan.PawnsListForReading)
            {
                ThingOwner<Thing> inventory = pawn.inventory?.innerContainer;
                if (inventory == null)
                    continue;

                for (int i = 0; i < inventory.Count; i++)
                {
                    Building_PassengerShuttle shuttle = AsPlayerShuttle(inventory[i]);
                    if (shuttle != null)
                        return shuttle;
                }
            }

            return null;
        }

        /// <summary>
        /// 起飞前把穿梭机放回殖民者背包的直接子项。
        ///
        /// 联动关系：原版 <c>CaravanShuttleUtility.LaunchShuttle</c> 用
        /// <c>GetOwnerOf</c> 找到背包，再 <c>Remove</c> 这架穿梭机，然后交给飞行中的运输船。
        /// 穿梭机若还包在 <see cref="MinifiedThing"/> 里，<c>Remove</c> 拿不到它，外壳随后会随远行队一起销毁，穿梭机跟着丢失。
        /// <see cref="Patch_LaunchShuttle"/> 在原版方法之前调用这里。
        ///
        /// 注意：已经直接躺在背包里时什么都不做。拆壳失败要把穿梭机放回外壳，不能留在没有持有者的状态。
        /// </summary>
        public static void SettleIntoInventory(Caravan caravan, Building_PassengerShuttle shuttle)
        {
            if (caravan == null || shuttle == null || shuttle.Destroyed)
                return;

            Pawn listedOwner = CaravanInventoryUtility.GetOwnerOf(caravan, shuttle);
            if (listedOwner?.inventory?.innerContainer != null && listedOwner.inventory.innerContainer.Contains(shuttle))
                return;

            foreach (Pawn pawn in caravan.PawnsListForReading)
            {
                ThingOwner<Thing> inventory = pawn.inventory?.innerContainer;
                if (inventory == null)
                    continue;

                for (int i = 0; i < inventory.Count; i++)
                {
                    if (inventory[i] is not MinifiedThing mini || mini.InnerThing != shuttle)
                        continue;

                    if (!TryUnwrap(pawn, mini, shuttle))
                        return;

                    caravan.RecacheInventory();
                    return;
                }
            }
        }

        private static Building_PassengerShuttle AsPlayerShuttle(Thing thing)
        {
            // 直接放在背包里的，跟原版一样，不论是不是玩家穿梭机都返回。
            // 迷你化的只认玩家客运穿梭机，避免任务穿梭机的外壳被当成远行队的飞机。
            if (thing is Building_PassengerShuttle direct && !direct.Destroyed)
                return direct;

            if (thing is not MinifiedThing mini)
                return null;

            if (mini.InnerThing is not Building_PassengerShuttle shuttle || shuttle.Destroyed)
                return null;

            CompShuttle comp = shuttle.ShuttleComp;
            if (comp != null && !comp.IsPlayerShuttle)
                return null;

            return shuttle;
        }

        private static bool TryUnwrap(Pawn pawn, MinifiedThing mini, Building_PassengerShuttle shuttle)
        {
            ThingOwner held = mini.GetDirectlyHeldThings();
            if (held == null || !held.Remove(shuttle))
                return false;

            if (!pawn.inventory.innerContainer.TryAdd(shuttle))
            {
                held.TryAdd(shuttle);
                Log.Error("[CaravanShuttleFromInventory] 无法把穿梭机从迷你化外壳放回 " + pawn.LabelShort + " 的背包，已放回外壳。");
                return false;
            }

            if (!mini.Destroyed)
                mini.Destroy(DestroyMode.Vanish);

            return true;
        }
    }
}
