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
using static CrossStitchMadness.CrossStitchMadnessInfo;

namespace CrossStitchMadness;

[BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
public class CrossStitchMadnessPlugin : BaseUnityPlugin
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
        Logger.LogInfo($"Plugin {PLUGIN_GUID} is loaded!");
        harmony = new Harmony(PLUGIN_GUID);
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
    [HarmonyPatch]
    public class ToolHudIconPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ToolHudIcon), "GetIsEmpty")]
        static bool GetIsEmptyPrefix(ToolHudIcon __instance, ref bool __result)
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
    [HarmonyPatch]
    public class HeroControllerPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(HeroController), "CanThrowTool", [typeof(ToolItem), typeof(AttackToolBinding), typeof(bool)])]
        static bool CanThrowToolPrefix(HeroController __instance, ToolItem tool, ref bool __result)
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
        [HarmonyPostfix]
        [HarmonyPatch(typeof(HeroController), "Awake")]
        static void AwakePostfix(HeroController __instance)
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
        [HarmonyPrefix]
        [HarmonyPatch(typeof(HeroController), "SilkGain", [typeof(HitInstance)])]
        static bool SilkGainPrefix(HitInstance hitInstance)
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
        [HarmonyPrefix]
        [HarmonyPatch(typeof(HeroController), "ThrowTool")]
        static void ThrowToolPrefix(HeroController __instance, ref ToolItem ___willThrowTool)
        {
            HeroController me = __instance;
            TOOL_FOR_COST = ___willThrowTool;
        }
        [HarmonyPostfix]
        [HarmonyPatch(typeof(HeroController), "ThrowTool")]
        static void ThrowToolPostfix(HeroController __instance, ref ToolItem ___willThrowTool)
        {
            HeroController me = __instance;
            if (GetSilkCost(TOOL_FOR_COST) != null)
            {
                EventRegister.SendEvent("SILK REFRESHED");
            }
            TOOL_FOR_COST = null;
        }
        [HarmonyPrefix]
        [HarmonyPatch(typeof(HeroController), "GetWillThrowTool")]
        static bool GetWillThrowToolPrefix(HeroController __instance, ref ToolItem ___willThrowTool, ref bool __result)
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
        [HarmonyPostfix]
        [HarmonyPatch(typeof(HeroController), "FixedUpdate")]
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
                    inventoryToolCrestList.CurrentCrest.crestSubmitAudio.SpawnAndPlayOneShot(Audio.DefaultUIAudioSourcePrefab, me.transform.position, null);
                }
            }
        }
    }
    [HarmonyPatch]
    public class ToolItemManagerPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ToolItemManager), "AutoEquip", [typeof(ToolItem)])]
        static bool AutoEquipPrefix(ToolItem tool)
        {
            if (tool != PARRY && tool.Type == ToolItemType.Skill)
            {
                return false;
            }
            return true;
        }
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ToolItemManager), "GetAttackToolBinding")]
        static bool GetAttackToolBindingPrefix(ToolItemManager __instance, ToolItem tool, ref AttackToolBinding? __result)
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
    public class PlayerDataPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(PlayerData), "SilkSkillCost", MethodType.Getter)]
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
        [HarmonyPrefix]
        [HarmonyPatch(typeof(PlayerData), "TakeHealth")]
        static void TakeHealthPrefix(ref int amount)
        {
            if (extraDamage.Value)
            {
                amount += 1;
            }
        }
    }
    [HarmonyPatch]
    public class HealthManagerPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(HealthManager), "TakeDamage", [typeof(HitInstance)])]
        static void TakeDamagePostfix(HealthManager __instance, HitInstance hitInstance)
        {
            HealthManager me = __instance;
            if (parryGivesSilk.Value)
            {
                HitInstance hit = hitInstance;
                if (me.IsImmuneTo(hit, true))
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
    static InventoryToolCrestList inventoryToolCrestList;
    public class InventoryToolCrestListPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(InventoryToolCrestList), "Awake")]
        static void AwakePrefix(InventoryToolCrestList __instance)
        {
            inventoryToolCrestList = __instance;
        }
    }
}
