using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GlobalEnums;
using GlobalSettings;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using static UnityEngine.GridBrushBase;

namespace CrossStitchMadness;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    public static int PARRY_COST = 0;
    Harmony harmony;
    public static ToolItem TOOL_FOR_COST = null;

    private static ConfigEntry<bool>? freeParry;
    private static ConfigEntry<bool>? parryAlwaysUnlocked;
    private static ConfigEntry<bool>? parryGivesSilk;
    private static ConfigEntry<bool>? extraDamage;
    private static ConfigEntry<bool>? noSilkFromNormalAttack;

    public static ToolItem PARRY
    {
        get
        {
            return ToolItemManager.GetToolByName("Parry");
        }
    }
    public static ToolItem SILK_SPEAR
    {
        get
        {
            return ToolItemManager.GetToolByName("Silk Spear");
        }
    }
    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
        harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll();
        freeParry = Config.Bind("Parrying", "No Cost", true);
        parryAlwaysUnlocked = Config.Bind("Parrying", "Always Unlocked", true);
        parryGivesSilk = Config.Bind("Parrying", "Gives Silk", true);
        extraDamage = Config.Bind("Extra Balance", "Increased Enemy Damage", true);
        noSilkFromNormalAttack = Config.Bind("Extra Balance", "No Silk From Normal Attacking", true);
    }
    public static bool FreeParryEnabled()
    {
        return freeParry.Value;
    }
    public static int? GetSilkCost(ToolItem tool)
    {
        if (FreeParryEnabled() && tool == PARRY)
        {
            return PARRY_COST;
        }
        return null;
    }
    [HarmonyPatch(typeof(ToolHudIcon), "GetIsEmpty")]
    public class ToolHudIconPatch
    {
        [HarmonyPrefix]
        static bool Prefix(ToolHudIcon __instance, ref bool __result)
        {
            ToolHudIcon me = __instance;
            PlayerData instance = PlayerData.instance;
            if (me.CurrentTool.Type == ToolItemType.Skill)
            {
                int? silkCost = GetSilkCost(me.CurrentTool);
                if (silkCost != null)
                {
                    __result = instance.silk < silkCost;
                    return false;
                }
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(HeroController), "CanThrowTool", [typeof(ToolItem), typeof(AttackToolBinding), typeof(bool)])]
    public class HeroControllerPatch
    {
        [HarmonyPrefix]
        static bool Prefix(HeroController __instance, ToolItem tool, ref bool __result)
        {
            HeroController me = __instance;
            if (tool.Type == ToolItemType.Skill)
            {
                int? silkCost = GetSilkCost(tool);
                if (silkCost != null)
                {
                    if (me.playerData.silk >= silkCost)
                    {
                        __result = true;
                        return false;
                    }
                }
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(HeroController), "Awake")]
    public class HeroControllerAwakePatch
    {
        [HarmonyPostfix]
        static void Postfix(HeroController __instance)
        {
            HeroController me = __instance;
            if (parryAlwaysUnlocked.Value && PARRY)
            {
                if (!PARRY.IsUnlocked)
                {
                    PARRY.Unlock();
                    ToolItemManager.AutoEquip(PARRY);
                }
            }
        }
    }
    [HarmonyPatch(typeof(ToolItemManager), "AutoEquip", [typeof(ToolItem)])]
    public class AutoEquipToolPatch
    {
        [HarmonyPrefix]
        static bool Prefix(ToolItem tool)
        {
            if (tool != PARRY && tool.Type == ToolItemType.Skill)
            {
                return false;
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(PlayerData), "SilkSkillCost", MethodType.Getter)]
    public class SilkSkillCostPatch
    {
        [HarmonyPrefix]
        static bool Prefix(ref int __result)
        {
            if (TOOL_FOR_COST != null)
            {
                if (TOOL_FOR_COST.Type == ToolItemType.Skill)
                {
                    int? silkCost = GetSilkCost(TOOL_FOR_COST);
                    if (silkCost != null)
                    {
                        __result = silkCost.Value;
                        return false;
                    }
                }
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(HeroController), "ThrowTool")]
    public class HeroControllerThrowToolPatch
    {
        [HarmonyPrefix]
        static void Prefix(HeroController __instance, ref ToolItem ___willThrowTool)
        {
            HeroController me = __instance;
            TOOL_FOR_COST = ___willThrowTool;
        }
        [HarmonyPostfix]
        static void Postfix(HeroController __instance, ref ToolItem ___willThrowTool)
        {
            HeroController me = __instance;
            if (GetSilkCost(TOOL_FOR_COST) != null)
            {
                EventRegister.SendEvent("SILK REFRESHED");
            }
            TOOL_FOR_COST = null;
        }
    }
    [HarmonyPatch(typeof(HeroController), "SilkGain", [typeof(HitInstance)])]
    public class HeroControllerSilkGainPatch
    {
        [HarmonyPrefix]
        static bool Prefix(HitInstance hitInstance)
        {
            if (noSilkFromNormalAttack.Value)
            {
                if (hitInstance.AttackType == AttackTypes.Nail && !(hitInstance.Source && (hitInstance.Source.name.StartsWith("Harpoon Damager") || hitInstance.Source.name.StartsWith("Harpoon Dash Damager"))))
                {
                    return false;
                }
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(HealthManager), "TakeDamage", [typeof(HitInstance)])]
    public class HealthManagerTakeDamagePatch
    {
        [HarmonyPostfix]
        static void Postfix(HealthManager __instance, HitInstance hitInstance)
        {
            HealthManager me = __instance;
            if (parryGivesSilk.Value)
            {
                HitInstance hit = hitInstance;
                if (ReversePatch.IsImmuneTo(me, hit, true))
                {
                    return;
                }
                if (hit.Source && hit.Source.transform.parent && hit.Source.transform.parent.gameObject.name.StartsWith("Hornet_parry_stab_cross_slash_style"))
                {
                    bool flag = hit.DamageDealt <= 0 && hit.HitEffectsType != EnemyHitEffectsProfile.EffectsTypes.LagHit;
                    if (hit.SilkGeneration == HitSilkGeneration.None) hit.SilkGeneration = hit.IsFirstHit ? HitSilkGeneration.FirstHit : HitSilkGeneration.Full;
                    if (!flag)
                    {
                        HeroController.instance.SilkGain(hit);
                    }
                }
            }
        }
    }
    [HarmonyPatch(typeof(PlayerData), "TakeHealth")]
    public class PlayerDataTakeHealthPatch
    {
        [HarmonyPrefix]
        static void Prefix(ref int amount)
        {
            if (extraDamage.Value)
            {
                amount += 1;
            }
        }
    }
    public static bool IsNearThickSilkVine(GameObject obj)
    {
        bool nearby = false;
        Collider2D[] colliders = Physics2D.OverlapBoxAll(obj.transform.position, new Vector2(6, 3), 0);
        foreach (Collider2D collider in colliders)
        {
            PlayMakerFSM fsm = collider.GetComponent<PlayMakerFSM>();
            if (fsm)
            {
                if (fsm.FsmName == "thick_silk_vine")
                {
                    nearby = true;
                    break;
                }
            }
        }
        return nearby;
    }
    [HarmonyPatch(typeof(HeroController), "GetWillThrowTool")]
    public class GetWillThrowToolPatch
    {
        [HarmonyPrefix]
        static bool Prefix(HeroController __instance, ref ToolItem ___willThrowTool, ref bool __result)
        {
            HeroController me = __instance;
            if (SILK_SPEAR.IsUnlocked && ToolItemManager.IsToolEquipped(PARRY, ToolEquippedReadSource.Active))
            {
                if (SILK_SPEAR_OVERRIDE)
                {
                    ___willThrowTool = SILK_SPEAR;
                    __result = true;
                    return false;
                }
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(ToolItemManager), "GetAttackToolBinding")]
    public class GetAttackToolBindingPatch
    {
        [HarmonyPrefix]
        static bool Prefix(ToolItemManager __instance, ToolItem tool, ref AttackToolBinding? __result)
        {
            ToolItemManager me = __instance;
            if (SILK_SPEAR_OVERRIDE && tool == SILK_SPEAR)
            {
                __result = ToolItemManager.GetAttackToolBinding(PARRY);
                return false;
            }
            return true;
        }
    }
    [HarmonyPatch]
    public class ToolItemManagerGetBoundAttackToolPatch
    {
        [HarmonyPostfix]
        static void Postfix(AttackToolBinding binding, ToolEquippedReadSource readSource, ref AttackToolBinding usedBinding, ref ToolItem __result)
        {
            if (__result == PARRY && SILK_SPEAR_OVERRIDE)
            {
                __result = SILK_SPEAR;
            }
        }
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(ToolItemManager), "GetBoundAttackTool", [typeof(AttackToolBinding), typeof(ToolEquippedReadSource), typeof(AttackToolBinding).MakeByRefType()]);
        }
    }
    static bool SILK_SPEAR_OVERRIDE = false;
    [HarmonyPatch(typeof(HeroController), "FixedUpdate")]
    public class HeroControllerFixedUpdatePatch
    {
        [HarmonyPostfix]
        static void Postfix(HeroController __instance)
        {
            if (SILK_SPEAR.IsUnlocked && ToolItemManager.IsToolEquipped(PARRY, ToolEquippedReadSource.Active) && !ToolItemManager.IsToolEquipped(SILK_SPEAR, ToolEquippedReadSource.Active))
            {
                HeroController me = __instance;
                bool nearbySilk = IsNearThickSilkVine(me.gameObject);
                bool justChanged = SILK_SPEAR_OVERRIDE != nearbySilk;
                SILK_SPEAR_OVERRIDE = nearbySilk;
                if (justChanged)
                {
                    EventRegister.SendEvent("SILK REFRESHED");
                    ReversePatch.GetCrestSubmitAudio(inventoryToolCrestList.CurrentCrest).SpawnAndPlayOneShot(Audio.DefaultUIAudioSourcePrefab, me.transform.position, null);
                }
            }
        }
    }
    static InventoryToolCrestList inventoryToolCrestList;
    [HarmonyPatch(typeof(InventoryToolCrestList), "Awake")]
    public class InventoryToolCrestListAwakePatch
    {
        [HarmonyPrefix]
        static void Prefix(InventoryToolCrestList __instance)
        {
            inventoryToolCrestList = __instance;
        }
    }
    [HarmonyPatch]
    public class ReversePatch
    {
        [HarmonyReversePatch]
        [HarmonyPatch(typeof(HealthManager), "IsImmuneTo")]
        public static bool IsImmuneTo(HealthManager instance, HitInstance hitInstance, bool wasFullHit) => throw new NotImplementedException();
        public static AudioEvent GetCrestSubmitAudio(InventoryToolCrest instance)
        {
            return Traverse.Create(instance).Field("crestSubmitAudio").GetValue<AudioEvent>();
        }
    }
}
