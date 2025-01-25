using DV.Common;
using DV.JObjectExtstensions;
using DV.Scenarios.Common;
using DV.UserManagement;
using DV.UserManagement.Data;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Multiplayer.Components.SaveGame;

public class Client_GameSession : IGameSession, IThing, IDisposable
{
    private string _gameMode;
    private JObject _gameData = new JObject();
    public static void SetCurrent(IGameSession session)
    {
        try
        {
            PropertyInfo currentSession = typeof(User).GetProperty("CurrentSession");
            currentSession?.SetValue(UserManager.Instance.CurrentUser, session);
        }
        catch (Exception ex)
        {
            Multiplayer.Log($"Client_GameSession.SetCurrent() failed: \r\n{ex.ToString()}");
        }
    }
    public Client_GameSession(string GameMode, IDifficulty difficulty)
    {
        _gameMode = GameMode;
        _gameData.SetBool("Difficulty_picked", true);
        Saves = new ReadOnlyObservableCollection<ISaveGame>(new ObservableCollection<ISaveGame>());

        this.SetDifficulty(difficulty);
    }

    string IGameSession.GameMode => _gameMode;

    string IGameSession.World => null;

    int IGameSession.SessionID => int.MaxValue;

    JObject IGameSession.GameData => _gameData;

    IUserProfile IGameSession.Owner =>   null;

    string IGameSession.BasePath => null;

    public ReadOnlyObservableCollection<ISaveGame> Saves { get; private set; }

    ISaveGame IGameSession.LatestSave => null;

    string IThing.Name { get => "Multiplayer Session"; set => throw new NotImplementedException(); }

    int IThing.DataVersion => 1; //might need to extract this from the Vanilla GameSession

    public void Save()
    {
        //do nothing
    }

    void IGameSession.DeleteSaveGame(ISaveGame save)
    {
        //do nothing
    }

    void IDisposable.Dispose()
    {
        //do nothing
    }

    int IGameSession.GetSavesCountByType(SaveType type)
    {
        return 0;
    }

    void IGameSession.MakeCurrent()
    {
        //do nothing
    }

    ISaveGame IGameSession.SaveGame(SaveType type, JObject data, Texture2D thumbnail, List<(int Type, byte[] Data)> customChunks, ISaveGame overwrite)
    {
        return null;
    }

    int IGameSession.TrimSaves(SaveType type, int maxCount, ISaveGame excluded)
    {
        return 0;
    }
}
