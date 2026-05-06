using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Localization;
using Mafi.Unity.Ui;
using Mafi.Unity.Ui.Hud;
using Mafi.Unity.Ui.Trading;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;

namespace OnDemandContracts;

[HarmonyPatch]
[HarmonyPatchCategory("OnDemandContractsHudEntry")]
public static class InGameContractsButtonPatch
{
    private static readonly HashSet<int> s_addedToTabs = [];
    private static OnDemandContractsWindow s_window;
    private static UiRoot s_uiRoot;

    private static IEnumerable<MethodBase> TargetMethods()
    {
        return AccessTools.GetDeclaredConstructors(typeof(ContractsTab));
    }

    [HarmonyPostfix]
    private static void AddButton(ContractsTab __instance)
    {
        if (__instance == null)
            return;

        int tabId = __instance.GetHashCode();
        if (s_addedToTabs.Contains(tabId))
            return;

        try
        {
            var button = new ButtonText("On-Demand Contracts".AsLoc(), OpenWindow)
            .Compact()
            .Width(200.px());

            button.Tooltip("Open On-Demand Contracts editor. Restart or reload required after saving.".AsLoc());

            // Floating button inside the Contracts tab, near the top-right controls.
            button.AbsolutePosition(right: 250.px(), top: 0.px());

            __instance.Add(button);

            s_addedToTabs.Add(tabId);
            Log.Info("[On-Demand Contracts] Contracts-tab button added.");
        }
        catch (Exception ex)
        {
            Log.Exception(ex, "[On-Demand Contracts] Failed to add Contracts-tab button.");
        }
    }

    public static void SetUiRoot(UiRoot root)
    {
        s_uiRoot = root;
    }

    public static void OpenWindow()
    {
        if (OnDemandContractsMod.StoredProtosDb == null)
        {
            Log.Warning("[On-Demand Contracts] ProtosDb not ready.");
            return;
        }

        if (s_uiRoot == null)
        {
            Log.Warning("[On-Demand Contracts] UiRoot not ready.");
            return;
        }

        try
        {
            if (s_window == null)
            {
                s_window = new OnDemandContractsWindow(OnDemandContractsMod.StoredProtosDb);
                s_window.DarkMask();
            }

            s_window.Open(s_uiRoot);
        }
        catch (Exception ex)
        {
            Log.Exception(ex, "[On-Demand Contracts] Failed to open window.");
        }
    }
}

[HarmonyPatch]
[HarmonyPatchCategory("OnDemandContractsHudEntry")]
public static class HudControllerUiRootPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        return AccessTools.GetDeclaredConstructors(typeof(HudController));
    }

    [HarmonyPostfix]
    private static void CaptureUiRoot(UiContext context)
    {
        if (context != null)
            InGameContractsButtonPatch.SetUiRoot(context.UiRoot);
    }
}