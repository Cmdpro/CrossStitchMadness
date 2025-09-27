using BepInEx;
using BepInEx.Logging;
using GlobalEnums;
using GlobalSettings;
using HarmonyLib;
using System;
using UnityEngine;

namespace CrossStitchMadness;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    public static int PARRY_COST = 0;
    Harmony harmony;
    public static bool IS_PARRY_COST = false;
    public static ToolItem PARRY
    {
        get
        {
            return ToolItemManager.GetToolByName("Parry");
        }
    }
    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
        harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll();
    }
    [HarmonyPatch(typeof(ToolHudIcon), "GetIsEmpty")]
    public class ToolHudIconPatch
    {
        [HarmonyPrefix]
        static bool Prefix(ToolHudIcon __instance, ref bool __result)
        {
            ToolHudIcon me = __instance;
            PlayerData instance = PlayerData.instance;
            if (me.CurrentTool == PARRY)
            {
                __result = instance.silk < PARRY_COST;
                return false;
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
            if (tool == PARRY)
            {
                if (me.playerData.silk >= PARRY_COST)
                {
                    __result = true;
                    return false;
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
            if (PARRY)
            {
                if (!PARRY.IsUnlocked)
                {
                    PARRY.Unlock();
                    ToolItemManager.AutoEquip(PARRY);
                }
            }
        }
    }
    [HarmonyPatch(typeof(PlayerData), "SilkSkillCost", MethodType.Getter)]
    public class SilkSkillCostPatch
    {
        [HarmonyPrefix]
        static bool Prefix(ref int __result)
        {
            if (IS_PARRY_COST)
            {
                __result = PARRY_COST;
                return false;
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
            if (___willThrowTool == PARRY)
            {
                IS_PARRY_COST = true;
            }
        }
        [HarmonyPostfix]
        static void Postfix(HeroController __instance, ref ToolItem ___willThrowTool)
        {
            HeroController me = __instance;
            if (___willThrowTool == PARRY)
            {
                IS_PARRY_COST = false;
                EventRegister.SendEvent("SILK REFRESHED");
            }
        }
    }
    [HarmonyPatch(typeof(HeroController), "SilkGain", [typeof(HitInstance)])]
    public class HeroControllerSilkGainPatch
    {
        [HarmonyPrefix]
        static bool Prefix(HitInstance hitInstance)
        {
            if (hitInstance.AttackType == AttackTypes.Nail && !(hitInstance.Source && (hitInstance.Source.name.StartsWith("Harpoon Damager") || hitInstance.Source.name.StartsWith("Harpoon Dash Damager"))))
            {
                return false;
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
            HitInstance hit = hitInstance;
            if (ReversePatch.IsImmuneTo(me, hit, true))
            {
                return;
            }
            if (hit.Source.transform.parent.gameObject.name.StartsWith("Hornet_parry_stab_cross_slash_style"))
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
    [HarmonyPatch(typeof(PlayerData), "TakeHealth")]
    public class PlayerDataTakeHealthPatch
    {
        [HarmonyPrefix]
        static void Prefix(ref int amount)
        {
            amount += 1;
        }
    }
    [HarmonyPatch]
    public class ReversePatch
    {
        [HarmonyReversePatch]
        [HarmonyPatch(typeof(HealthManager), "IsImmuneTo")]
        public static bool IsImmuneTo(HealthManager instance, HitInstance hitInstance, bool wasFullHit) => throw new NotImplementedException();
    }
}
