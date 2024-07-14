using System;
using System.Collections.Generic;
using System.Linq;
using DV;
using DV.UI;
using Multiplayer.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Text.RegularExpressions;
using DV.Common;
using System.Collections;
using Multiplayer.Networking.Managers.Server;
using Multiplayer.Components.Networking.Player;
using static System.Net.Mime.MediaTypeNames;


namespace Multiplayer.Components.Networking.UI;

//[RequireComponent(typeof(Canvas))]
//[RequireComponent(typeof(CanvasScaler))]
[RequireComponent(typeof(RectTransform))]
public class ChatGUI : MonoBehaviour
{
    private const float PANEL_LEFT_MARGIN = 20f;    //How far to inset the chat window from the left edge of the screen
    private const float PANEL_BOTTOM_MARGIN = 50f;  //How far to inset the chat window from the bottom of the screen
    private const float PANEL_FADE_DURATION = 1f;
    private const float MESSAGE_INSET = 15f;        //How far to inset the message text from the edge of chat the window

    private const int MESSAGE_MAX_HISTORY = 50;     //Maximum messages to keep in the queue
    private const int MESSAGE_TIMEOUT = 10;         //Maximum time to show an incoming message before fade
    private const int MESSAGE_MAX_LENGTH = 500;     //Maximum length of a single message
    private const int MESSAGE_RATE_LIMIT = 10;      //Limit how quickly a user can send messages (also enforced server side)

    private const int SEND_MAX_HISTORY = 10;        //How many previous messages to remember

    private GameObject messagePrefab;

    private List<GameObject> messageList = new List<GameObject>();
    private List<string> sendHistory = new List<string>();

    private TMP_InputField chatInputIF;
    private ScrollRect scrollRect;
    private RectTransform chatPanel;
    private CanvasGroup canvasGroup;

    private GameObject panelGO;
    private GameObject textInputGO;
    private GameObject scrollViewGO;

    private bool isOpen = false;
    private bool showingMessage = false;

    private int sendHistoryIndex = -1;
    private bool whispering = false;
    private string lastRecipient;

    //private CustomFirstPersonController player;
    //private HotbarController hotbarController;

    private float timeOut; //time-out counter for hiding the messages
    //private float testTimeOut;

    private GameFeatureFlags.Flag denied;

    private void Awake()
    {
        Multiplayer.Log("ChatGUI Awake() called");

        SetupOverlay(); //sizes and positions panel

        BuildUI();      //Creates input fields and scroll area

        panelGO.SetActive(false); //We don't need this to be visible when the game launches
        textInputGO.SetActive(false);

        //Find the player and toolbar so we can block input
        /*
        player = GameObject.FindObjectOfType<CustomFirstPersonController>();
        if(player == null)
        {
            Multiplayer.Log("Failed to find CustomFirstPersonController");
            return;
        }

        hotbarController = GameObject.FindObjectOfType<HotbarController>();
        if (hotbarController == null)
        {
            Multiplayer.Log("Failed to find HotbarController");
            return;
        }
        */

    }

    private void OnEnable()
    {
        chatInputIF.onSubmit.AddListener(Submit);
        chatInputIF.onValueChanged.AddListener(ChatInputChange);
        
    }

    private void OnDisable()
    {
        chatInputIF.onSubmit.RemoveAllListeners();
        chatInputIF.onValueChanged.RemoveAllListeners();
    }

    private void Update()
    {
        //Handle keypresses to open/close the chat window
        if (!isOpen && Input.GetKeyDown(KeyCode.Return) && !AppUtil.Instance.IsPauseMenuOpen)
        {
            isOpen = true;              //whole panel is open
            showingMessage = false;     //We don't want to time out

            ShowPanel();
            textInputGO.SetActive(isOpen);

            sendHistoryIndex = sendHistory.Count;

            if (whispering)
            {
                chatInputIF.text = "/w " + lastRecipient + ' ';
                chatInputIF.caretPosition = chatInputIF.text.Length;
            }

            BlockInput(true);
        }
        else if (isOpen)
        {
            //Check for closing window
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return))
            {
                isOpen = false;
                if (!showingMessage)
                {
                    textInputGO.SetActive(isOpen);
                    HidePanel();
                }

                BlockInput(false);
            }else if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                sendHistoryIndex--;
                if (sendHistory.Count > 0 && sendHistoryIndex < sendHistory.Count)
                {
                    chatInputIF.text = sendHistory[sendHistoryIndex];
                    chatInputIF.caretPosition = chatInputIF.text.Length;
                }
            }else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                sendHistoryIndex++;
                if (sendHistory.Count > 0 && sendHistoryIndex >= 0)
                {
                    chatInputIF.text = sendHistory[sendHistoryIndex];
                    chatInputIF.caretPosition = chatInputIF.text.Length;
                }
            }
        } 

        //Maintain focus on the text input field
        if(isOpen && !chatInputIF.isFocused)
        {
            chatInputIF.ActivateInputField();
        }

        //After a message is sent/received, keep displaying it for the timeout period
        //Would be nice to add a fadeout in future
        if (showingMessage && !textInputGO.activeSelf)
        {
            timeOut += Time.deltaTime;

            if (timeOut >= MESSAGE_TIMEOUT)
            {
                showingMessage = false ;
                //panelGO.SetActive(false);
                HidePanel();
            } 
        }

        ////testTimeOut += Time.deltaTime;
        //if (testTimeOut >= 60)
        //{
        //    testTimeOut = 0;
        //    ReceiveMessage("<alpha=#50>Morm:</color> Test TimeOut");
        //}
    }

    public void Submit(string text)
    {
        text = text.Trim();

        if (text.Length > 0)
        {
            //Strip any injected formatting
            text = Regex.Replace(text, "</noparse>", string.Empty, RegexOptions.IgnoreCase);

            //check for whisper
            if(CheckForWhisper(text, out string localMessage, out string recipient))
            {
                whispering = true;
                lastRecipient = recipient;

                if (localMessage == null || localMessage == string.Empty)
                    return;

                if (lastRecipient.Contains(" "))
                {
                    lastRecipient = '"' + lastRecipient + '"';
                }

                AddMessage("<alpha=#50>You (<i>" + recipient + "</i>):</color> <noparse>" + localMessage + "</noparse>");
            }
            else
            {
                whispering = false;
                AddMessage("<alpha=#50>You:</color> <noparse>" + text + "</noparse>");
            }

            //add to send history
            if (sendHistory.Count >= SEND_MAX_HISTORY)
            {
                sendHistory.RemoveAt(0);
            }

            //add to the history - if already there, we'll relocate it to the end
            int exists = sendHistory.IndexOf(text);
            if (exists != -1)
                sendHistory.RemoveAt(exists);

            sendHistory.Add(text);

            //send to server
            NetworkLifecycle.Instance.Client.SendChat(text);

            //reset any timeouts
            timeOut = 0;
            showingMessage = true;
        }

        chatInputIF.text = "";

        textInputGO.SetActive(false);
        BlockInput(false);

        return;
    }

    private void ChatInputChange(string message)
    {
        Multiplayer.Log($"ChatInputChange({message})");

        //allow the user to clear text
        if(Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete))
            return;
        
        if (CheckForWhisper(message, out string localMessage, out string recipient))
        {
            Multiplayer.Log($"ChatInputChange: message: \"{message}\", localMessage: \"{(localMessage == null ? "null" : localMessage)}" +
                $"\", recipient: \"{(recipient == null ? "null" : recipient)}\"");

            if (localMessage == null || localMessage == string.Empty)
            {
                
                string closestMatch = NetworkLifecycle.Instance.Client.PlayerManager.Players
                                                .Where(player => player.Username.ToLower().StartsWith(recipient.ToLower()))
                                                .OrderBy(player => player.Username.Length)
                                                .ThenByDescending(player => player.Username)
                                                .ToList()
                                                .FirstOrDefault().Username;

                /*
                Multiplayer.Log($"ChatInputChange: closesMatch: {(closestMatch == null? "null" : closestMatch.Username)}");

                
                if(closestMatch == null)
                    return;

                bool quoteFlag = false;
                if (match.Contains(' '))
                {
                    match = '"' + match + '"';
                    quoteFlag = true;
                }

                Multiplayer.Log($"ChatInput: recipient {recipient}, qF: {quoteFlag}, match: {match}, compare {recipient == closestMatch}");
                */

                //if we have a match, allow the client to type
                if (closestMatch == null || recipient == closestMatch)
                    return;

                //update the textbox
                chatInputIF.SetTextWithoutNotify("/w " + closestMatch);

                //Multiplayer.Log($"ChatInput: length {chatInputIF.text.Length}, anchor: {"/w ".Length + recipient.Length + (quoteFlag ? 1 : 0)}");

                //select the trailing match chars
                chatInputIF.caretPosition = chatInputIF.text.Length; // Set caret to end of text
                //chatInputIF.selectionAnchorPosition = chatInputIF.text.Length - "/w ".Length - recipient.Length - (quoteFlag?1:0) + 1;
                chatInputIF.selectionAnchorPosition = "/w ".Length + recipient.Length;// + (quoteFlag?1:0);
                

            }
        }

    }

    private bool CheckForWhisper(string message, out string localMessage, out string recipient)
    {
        recipient = "";
        localMessage = "";


        if (message.StartsWith("/") && message.Length > (ChatManager.COMMAND_WHISPER_SHORT.Length + 2))
        {
            Multiplayer.Log("CheckForWhisper() starts with /");
            string command = message.Substring(1).Split(' ')[0];
            switch (command)
            {
                case ChatManager.COMMAND_WHISPER_SHORT:
                    localMessage = message.Substring(ChatManager.COMMAND_WHISPER_SHORT.Length + 2);
                    break;
                case ChatManager.COMMAND_WHISPER:
                    localMessage = message.Substring(ChatManager.COMMAND_WHISPER.Length + 2);
                    break;

                //allow messages that are not whispers to go through
                default:
                    localMessage = message;
                    return false;
            }

            if (localMessage == null || localMessage == string.Empty)
            { 
                localMessage = message;
                return false;
            }

            /*
            //Check if name is in Quotes e.g. '/w "Mr Noname" my message'
            if (localMessage.StartsWith("\""))
            {
                Multiplayer.Log("CheckForWhisper() starts with \"");
                int endQuote = localMessage.Substring(1).IndexOf('"');
                Multiplayer.Log($"CheckForWhisper() starts with \" - indexOf, eQ: {endQuote}");
                if (endQuote <=1)
                {
                    recipient = localMessage.Substring(1);
                    localMessage = string.Empty;//message;
                    return true;
                }

                Multiplayer.Log("CheckForWhisper() remove quote");
                recipient = localMessage.Substring(1, endQuote);
                localMessage = localMessage.Substring(recipient.Length + 3);
            }
            else
            {
            Multiplayer.Log("CheckForWhisper() no quote");
            */
            recipient = localMessage.Split(' ')[0];
            if (localMessage.Length > (recipient.Length + 2))
            {
                localMessage = localMessage.Substring(recipient.Length + 1);
            }
            else
            {
                localMessage = "";
            }
            //}

            return true;
        }

        localMessage = message;
        return false;
    }

    public void ReceiveMessage(string message)
    {

        if (message.Trim().Length > 0)
        {
            //add locally
            AddMessage(message);
        }

        timeOut = 0;
        showingMessage = true;

        ShowPanel();
        //panelGO.SetActive(true);   
    }

    private void AddMessage(string text)
    {
        if (messageList.Count >= MESSAGE_MAX_HISTORY)
        {
            GameObject.Destroy(messageList[0]);
            messageList.RemoveAt(0);
        }

        GameObject newMessage = Instantiate(messagePrefab, chatPanel);
        newMessage.GetComponent<TextMeshProUGUI>().text = text;
        messageList.Add(newMessage);

        scrollRect.verticalNormalizedPosition = 0f; //scroll to the bottom - maybe later we need some logic for this?
    }


    #region UI


    public void ShowPanel()
    {
        StopCoroutine(FadeOut());
        panelGO.SetActive(true);
        canvasGroup.alpha = 1f;
    }

    public void HidePanel()
    {
        StartCoroutine(FadeOut());
    }

    private IEnumerator FadeOut()
    {
        float startAlpha = canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < PANEL_FADE_DURATION)
        {
            elapsed += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, elapsed / PANEL_FADE_DURATION);
            yield return null;
        }

        canvasGroup.alpha = 0f;
        panelGO.SetActive(false);
    }

    private void SetupOverlay()
    {
        //Setup the host object
        RectTransform myRT = this.transform.GetComponent<RectTransform>();
        myRT.sizeDelta = new Vector2(Screen.width, Screen.height);
        myRT.anchorMin = Vector2.zero;
        myRT.anchorMax = Vector2.zero;
        myRT.pivot = Vector2.zero;
        myRT.anchoredPosition = Vector2.zero;


        // Create a Panel
        panelGO = new GameObject("OverlayPanel");
        panelGO.transform.SetParent(this.transform, false);
        RectTransform rectTransform = panelGO.AddComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(Screen.width * 0.25f, Screen.height * 0.25f);
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.zero;
        rectTransform.pivot = Vector2.zero;
        rectTransform.anchoredPosition = new Vector2(PANEL_LEFT_MARGIN, PANEL_BOTTOM_MARGIN);

        canvasGroup = panelGO.AddComponent<CanvasGroup>(); // Add CanvasGroup for fade effect
    }

    private void BuildUI()
    {
        GameObject scrollViewPrefab = null;
        GameObject inputPrefab;

        //get prefabs
        PopupNotificationReferences popup = GameObject.FindObjectOfType<PopupNotificationReferences>();
        SaveLoadController saveLoad = GameObject.FindObjectOfType<SaveLoadController>();

        if (popup == null)
        {
            Multiplayer.Log("Could not find PopupNotificationReferences");
            return;
        }
        else
        {
            inputPrefab = popup.popupTextInput.FindChildByName("TextFieldTextIcon");
        }

        if (saveLoad == null)
        {
            Multiplayer.Log("Could not find SaveLoadController, attempting to instanciate");
            AppUtil.Instance.PauseGame();

            Multiplayer.Log("Paused");

            saveLoad = FindObjectOfType<PauseMenuController>().saveLoadController;

            if (saveLoad == null)
            {
                Multiplayer.Log("Failed to get SaveLoadController");
            }
            else
            {
                Multiplayer.Log("Made a SaveLoadController!");
                scrollViewPrefab = saveLoad.FindChildByName("Scroll View");

                if (scrollViewPrefab == null)
                {
                    Multiplayer.Log("Could not find scrollViewPrefab");
                    
                }
                else
                {
                    scrollViewPrefab = Instantiate(scrollViewPrefab);
                }
            }

            AppUtil.Instance.UnpauseGame();
        }
        else
        {
            scrollViewPrefab = saveLoad.FindChildByName("Scroll View");
        }


        if (inputPrefab == null)
        {
            Multiplayer.Log("Could not find inputPrefab");
            return;
        }
        if (scrollViewPrefab == null)
        {
            Multiplayer.Log("Could not find scrollViewPrefab");
            return;
        }


        //Add an input box
        textInputGO = Instantiate(inputPrefab);
        textInputGO.name = "Chat Input";
        textInputGO.transform.SetParent(panelGO.transform, false);

        //Remove redundant components
        GameObject.Destroy(textInputGO.FindChildByName("icon"));
        GameObject.Destroy(textInputGO.FindChildByName("image select"));
        GameObject.Destroy(textInputGO.FindChildByName("image hover"));
        GameObject.Destroy(textInputGO.FindChildByName("image click"));

        //Position input
        RectTransform textInputRT = textInputGO.GetComponent<RectTransform>();
        textInputRT.pivot = Vector3.zero;
        textInputRT.anchorMin = Vector2.zero;
        textInputRT.anchorMax = new Vector2(1f, 0);

        textInputRT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Bottom, 0, 20f);
        textInputRT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, 0, 1f);

        RectTransform panelRT = panelGO.GetComponent<RectTransform>();
        textInputRT.sizeDelta = new Vector2 (panelRT.rect.width, 40f);

        //Setup input
        chatInputIF = textInputGO.GetComponent<TMP_InputField>();
        chatInputIF.onFocusSelectAll = false;
        chatInputIF.characterLimit = MESSAGE_MAX_LENGTH;
        chatInputIF.richText=false;

        //Setup placeholder
        chatInputIF.placeholder.GetComponent<TMP_Text>().richText = false;
        chatInputIF.placeholder.GetComponent<TMP_Text>().text = "Type a message and press Enter!";
        //Setup input renderer
        TMP_Text chatInputRenderer = textInputGO.FindChildByName("text [noloc]").GetComponent<TMP_Text>();
        chatInputRenderer.fontSize = 18;
        chatInputRenderer.richText = false;
        chatInputRenderer.parseCtrlCharacters = false;



        //Add a new scroll pane
        scrollViewGO = Instantiate(scrollViewPrefab);
        scrollViewGO.name = "Chat Scroll";
        scrollViewGO.transform.SetParent(panelGO.transform, false);

        //Position scroll pane
        RectTransform scrollViewRT = scrollViewGO.GetComponent<RectTransform>();
        scrollViewRT.pivot = Vector3.zero;
        scrollViewRT.anchorMin = Vector2.zero;
        scrollViewRT.anchorMax = new Vector2(1f, 0);

        scrollViewRT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Bottom, textInputRT.rect.height, 20f);
        scrollViewRT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, 0, 1f);

        scrollViewRT.sizeDelta = new Vector2(panelRT.rect.width, panelRT.rect.height - textInputRT.rect.height);


        //Setup scroll pane
        GameObject viewport = scrollViewGO.FindChildByName("Viewport");
        RectTransform viewportRT = viewport.GetComponent<RectTransform>();
        scrollRect = scrollViewGO.GetComponent<ScrollRect>(); 

        viewportRT.pivot = new Vector2(0.5f, 0.5f);
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one; 
        viewportRT.offsetMin = Vector2.zero;
        viewportRT.offsetMax = Vector2.zero;

        scrollRect.viewport = scrollViewRT;

        //set up content
        GameObject.Destroy(scrollViewGO.FindChildByName("GRID VIEW").gameObject);
        GameObject content = new GameObject("Content", typeof(RectTransform), typeof(ContentSizeFitter), typeof(VerticalLayoutGroup));
        content.transform.SetParent(viewport.transform, false);

        ContentSizeFitter contentSF = content.GetComponent<ContentSizeFitter>();
        contentSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        VerticalLayoutGroup contentVLG = content.GetComponent<VerticalLayoutGroup>();
        contentVLG.childAlignment = TextAnchor.LowerLeft;
        contentVLG.childControlWidth = false;
        contentVLG.childControlHeight = true;
        contentVLG.childForceExpandWidth = true;
        contentVLG.childForceExpandHeight = false;

        chatPanel = content.GetComponent<RectTransform>();
        chatPanel.pivot = Vector2.zero;
        chatPanel.anchorMin = Vector2.zero;
        chatPanel.anchorMax = new Vector2(1f, 0f);
        chatPanel.offsetMin = Vector2.zero;
        chatPanel.offsetMax = Vector2.zero;
        scrollRect.content = chatPanel;

        chatPanel.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, MESSAGE_INSET, chatPanel.rect.width - MESSAGE_INSET);

        //Realign vertical scroll bar
        RectTransform scrollBarRT = scrollRect.verticalScrollbar.transform.GetComponent<RectTransform>();
        scrollBarRT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 0, scrollViewRT.rect.height);



        //Build message prefab
        messagePrefab = new GameObject("Message Text", typeof(TextMeshProUGUI));

        RectTransform messagePrefabRT = messagePrefab.GetComponent<RectTransform>();
        messagePrefabRT.pivot = new Vector2(0.5f, 0.5f);
        messagePrefabRT.anchorMin = new Vector2(0f, 1f);
        messagePrefabRT.anchorMax = new Vector2(0f, 1f);
        messagePrefabRT.offsetMin = new Vector2(0f, 0f);
        messagePrefabRT.offsetMax = Vector2.zero;
        messagePrefabRT.sizeDelta = new Vector2(chatPanel.rect.width, messagePrefabRT.rect.height);
      
        TextMeshProUGUI messageTM = messagePrefab.GetComponent<TextMeshProUGUI>();
        messageTM.textWrappingMode = TextWrappingModes.Normal;
        messageTM.fontSize = 18;
        messageTM.text = "Morm: Hurry up!";
    }

    private void BlockInput(bool block)
    {
        //player.Locomotion.inputEnabled = !block;
        //hotbarController.enabled = !block;
        if (block)
        {
            denied = GameFeatureFlags.DeniedFlags;

            GameFeatureFlags.Deny(GameFeatureFlags.Flag.ALL);
            CursorManager.Instance.RequestCursor(this, true);
            //InputFocusManager.Instance.TakeKeyboardFocus();
        }
        else
        {
            GameFeatureFlags.Allow(GameFeatureFlags.Flag.ALL);
            GameFeatureFlags.Deny(denied);
            CursorManager.Instance.RequestCursor(this, false);

            //InputFocusManager.Instance.ReleaseKeyboardFocus();
        }
    }

    #endregion
}
