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
         
        protected override void Awake()
        {
            //Multiplayer.Log("serverBrowserGridview Awake()");

            //copy the copy
            this.viewElementPrefab.SetActive(false);
            this.dummyElementPrefab = Instantiate(this.viewElementPrefab);

            //swap controllers
            GameObject.Destroy(this.viewElementPrefab.GetComponent<SaveLoadViewElement>());
            GameObject.Destroy(this.dummyElementPrefab.GetComponent<SaveLoadViewElement>());

            this.viewElementPrefab.AddComponent<ServerBrowserElement>();
            this.dummyElementPrefab.AddComponent<ServerBrowserDummyElement>();

            this.viewElementPrefab.name = "prefabServerBrowserElement";
            this.dummyElementPrefab.name = "prefabServerBrowserDummyElement";

            this.viewElementPrefab.SetActive(true);
            this.dummyElementPrefab.SetActive(true);

        }

        public ServerBrowserElement GetElementAt(int index)
        {
            return transform.GetChild(index + indexOffset).GetComponent<ServerBrowserElement>();
        }
    }
}
