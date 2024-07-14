using System;
using DV.UI;
using DV.UIFramework;
using Multiplayer.Components.MainMenu.ServerBrowser;
using UnityEngine;
using UnityEngine.UI;

namespace Multiplayer.Components.MainMenu
{
    [RequireComponent(typeof(ContentSizeFitter))]
    [RequireComponent(typeof(VerticalLayoutGroup))]
    // 
    public class ServerBrowserGridView : AGridView<IServerBrowserGameDetails>
    {
         
        private void Awake()
        {
            Multiplayer.Log("serverBrowserGridview Awake");

            //swap controller
            this.viewElementPrefab.SetActive(false);
            this.dummyElementPrefab = Instantiate(this.viewElementPrefab);

            GameObject.Destroy(this.viewElementPrefab.GetComponent<SaveLoadViewElement>());
            GameObject.Destroy(this.dummyElementPrefab.GetComponent<SaveLoadViewElement>());

            this.viewElementPrefab.AddComponent<ServerBrowserElement>();
            this.dummyElementPrefab.AddComponent<ServerBrowserDummyElement>();

            this.viewElementPrefab.name = "prefabServerBrowserElement";
            this.dummyElementPrefab.name = "prefabServerBrowserDummyElement";

            this.viewElementPrefab.SetActive(true);
            this.dummyElementPrefab.SetActive(true);

        }
    }
}
