using System;
using DV.Common;
using DV.UI;
using DV.UI.PresetEditors;
using DV.UIFramework;
using HarmonyLib;
using Multiplayer.Components.MainMenu;
using Multiplayer.Utils;
using UnityEngine;
using UnityEngine.UI;


namespace Multiplayer.Patches.MainMenu;

[HarmonyPatch(typeof(LauncherController))]
public static class LauncherController_Patch
{
    private const int PADDING = 10;
    
    private static GameObject goHost;
    private static LauncherController lcInstance;
    


    [HarmonyPostfix]
    [HarmonyPatch(typeof(LauncherController), "OnEnable")]
    private static void OnEnable(LauncherController __instance)
    {

        Multiplayer.Log("LauncherController_Patch()");

        if (goHost != null)
            return;

        GameObject goRun = __instance.FindChildByName("ButtonTextIcon Run");

        if(goRun != null)
        {
            goRun.SetActive(false);
            goHost = GameObject.Instantiate(goRun);
            goRun.SetActive(true);

            goHost.name = "ButtonTextIcon Host";
            goHost.transform.SetParent(goRun.transform.parent, false);

            RectTransform btnHostRT = goHost.GetComponentInChildren<RectTransform>();

            Vector3 curPos = btnHostRT.localPosition;
            Vector2 curSize = btnHostRT.sizeDelta;

            btnHostRT.localPosition = new Vector3(curPos.x - curSize.x - PADDING, curPos.y,curPos.z);

            Sprite arrowSprite = GameObject.FindObjectOfType<MainMenuController>().continueButton.FindChildByName("icon").GetComponent<Image>().sprite;
            __instance.transform.gameObject.UpdateButton("ButtonTextIcon Host", "ButtonTextIcon Host", Locale.SERVER_BROWSER__HOST_KEY, null, arrowSprite);

            // Set up event listeners
            Button btnHost = goHost.GetComponent<ButtonDV>();

            btnHost.onClick.AddListener(HostAction);

            goHost.SetActive(true);

            Multiplayer.Log("LauncherController_Patch() complete");
        }
    }
 
    [HarmonyPostfix]
    [HarmonyPatch(typeof(LauncherController), "SetData", new Type[] { typeof(ISaveGame), typeof(AUserProfileProvider) , typeof(AScenarioProvider) , typeof(LauncherController.UpdateRequest) })]
    private static void SetData(LauncherController __instance, ISaveGame saveGame, AUserProfileProvider userProvider, AScenarioProvider scenarioProvider, LauncherController.UpdateRequest updateCallback)
    {
        if (RightPaneController_OnEnable_Patch.hgpInstance == null)
            return;

        RightPaneController_OnEnable_Patch.hgpInstance.saveGame = saveGame;
        RightPaneController_OnEnable_Patch.hgpInstance.userProvider = userProvider;
        RightPaneController_OnEnable_Patch.hgpInstance.scenarioProvider = scenarioProvider;
  

    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(LauncherController), "SetData", new Type[] { typeof(UIStartGameData), typeof(AUserProfileProvider), typeof(AScenarioProvider), typeof(LauncherController.UpdateRequest) })]
    private static void SetData(LauncherController __instance, UIStartGameData startGameData, AUserProfileProvider userProvider, AScenarioProvider scenarioProvider, LauncherController.UpdateRequest updateCallback)
    {
        if (RightPaneController_OnEnable_Patch.hgpInstance == null)
            return;

        RightPaneController_OnEnable_Patch.hgpInstance.startGameData = startGameData;
        RightPaneController_OnEnable_Patch.hgpInstance.userProvider = userProvider;
        RightPaneController_OnEnable_Patch.hgpInstance.scenarioProvider = scenarioProvider;
       
    }

    private static void HostAction()
    {
        //Debug.Log("Host button clicked.");

        RightPaneController_OnEnable_Patch.uIMenuController.SwitchMenu(RightPaneController_OnEnable_Patch.hostMenuIndex);

    }
}
