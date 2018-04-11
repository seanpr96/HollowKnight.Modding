﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using GlobalEnums;
using System.Text;
//using MonoMod;
using UnityEngine;
using UnityEngine.SceneManagement;

using MonoModIgnore = MonoMod.MonoModIgnore;
using MonoModReplace = MonoMod.MonoModReplace;
using MonoModOriginalName = MonoMod.MonoModOriginalName;

// ReSharper disable All
//We don't care about XML docs for these as they are being patched into the original code
#pragma warning disable 1591, 0108, 0169, 0649, 114, 0414,0162, IDE1005, IDE1006
namespace Modding.Patches
{
    //These are flat out copied from the game's decompiled source.  We tried doing IL edits, but it was so complicated as to make it not worth it.  If there ever is an easy way to decompile a method, get it as c#, edit, and recompile in monomod, we can remove this.
    public partial class GameManager
    {

        #region SaveGame
        [MonoModIgnore] private GameCameras gameCams;
        [MonoModIgnore] private float sessionTotalPlayTimer;
        [MonoModIgnore] private float sessionStartTime;

        [MonoModIgnore] private extern void UpdateSessionPlayTime();
        [MonoModIgnore] private extern int CheckOldBackups(ref List<string> backupFiles, string backUpSaveSlotPath, bool removeOldest = false);
        [MonoModIgnore] private extern void ResetGameTimer();

        [MonoModReplace]
        public void SaveGame(int saveSlot)
        {
            if( saveSlot >= 0 )
            {
                if( this.gameCams.saveIcon != null )
                {
                    this.gameCams.saveIcon.SendEvent( "GAME SAVED" );
                }
                else
                {
                    GameObject gameObject = GameObject.FindGameObjectWithTag("Save Icon");
                    if( gameObject != null )
                    {
                        PlayMakerFSM playMakerFSM = FSMUtility.LocateFSM(gameObject, "Checkpoint Control");
                        if( playMakerFSM != null )
                        {
                            playMakerFSM.SendEvent( "GAME SAVED" );
                        }
                    }
                }
                this.SaveLevelState();
                if( !this.gameConfig.disableSaveGame )
                {
                    if( this.achievementHandler != null )
                    {
                        this.achievementHandler.FlushRecordsToDisk();
                    }
                    else
                    {
                        Debug.LogError( "Error saving achievements (PlayerAchievements is null)" );
                    }
                    if( this.playerData != null )
                    {
                        if( this.gameState != GameState.PAUSED )
                        {
                            this.UpdateSessionPlayTime();
                        }
                        this.playerData.playTime += this.sessionTotalPlayTimer;
                        this.ResetGameTimer(); 
                        this.playerData.version = "1.3.0.8";
                        this.playerData.profileID = saveSlot;
                        this.playerData.CountGameCompletion();
                    }
                    else
                    {
                        Debug.LogError( "Error updating PlayerData before save (PlayerData is null)" );
                    }
                    try
                    {
                        SaveGameData obj = new SaveGameData(this.playerData, this.sceneData);
                        ModHooks.Instance.OnBeforeSaveGameSave( obj );
                        string text = JsonUtility.ToJson(obj);
                        bool flag = this.gameConfig.useSaveEncryption && !Platform.Current.IsFileSystemProtected;
                        if( flag )
                        {
                            string graph = Encryption.Encrypt(text);
                            BinaryFormatter binaryFormatter = new BinaryFormatter();
                            MemoryStream memoryStream = new MemoryStream();
                            binaryFormatter.Serialize( memoryStream, graph );
                            Platform.Current.WriteSaveSlot( saveSlot, memoryStream.ToArray() );
                            memoryStream.Close();
                        }
                        else
                        {
                            Platform.Current.WriteSaveSlot( saveSlot, Encoding.UTF8.GetBytes( text ) );
                        }
                        //bool flag = this.gameConfig.useSaveEncryption && !Platform.Current.IsFileSystemProtected;
                        //if( flag )
                        //{
                        //    string graph = Encryption.Encrypt(text);
                        //    BinaryFormatter binaryFormatter = new BinaryFormatter();
                        //    MemoryStream memoryStream = new MemoryStream();
                        //    binaryFormatter.Serialize( memoryStream, graph );
                        //    Platform.Current.WriteSaveSlot( saveSlot, memoryStream.ToArray() );
                        //    memoryStream.Close();
                        //}
                        //else
                        //{
                        //    Platform.Current.WriteSaveSlot( saveSlot, Encoding.UTF8.GetBytes( text ) );
                        //}
                    }
                    catch( Exception arg )
                    {
                        Debug.LogError( "GM Save - There was an error saving the game: " + arg );
                    }
                    Modding.ModHooks.Instance.OnSavegameSave( saveSlot );
                }
                else
                {
                    Debug.Log( "Saving game disabled. No save file written." );
                }
            }
            else
            {
                Debug.LogError( "Save game slot not valid: " + saveSlot );
            }
        }
        #endregion

        #region LoadGame

        [MonoModReplace]
        public bool LoadGame(int saveSlot)
        {
            if( !Platform.IsSaveSlotIndexValid( saveSlot ) )
            {
                Debug.LogErrorFormat( "Cannot load from invalid save slot index {0}", new object[]
                {
                saveSlot
                } );
                return false;
            }
            if( !Platform.Current.IsSaveSlotInUse( saveSlot ) )
            {
                Debug.LogErrorFormat( "Cannot load from empty save slot index {0}", new object[]
                {
                saveSlot
                } );
                return false;
            }
            bool result;
            try
            {
                bool flag = this.gameConfig.useSaveEncryption && !Platform.Current.IsFileSystemProtected;
                string json;
                if( flag )
                {
                    BinaryFormatter binaryFormatter = new BinaryFormatter();
                    MemoryStream serializationStream = new MemoryStream(Platform.Current.ReadSaveSlot(saveSlot));
                    string encryptedString = (string)binaryFormatter.Deserialize(serializationStream);
                    json = Encryption.Decrypt( encryptedString );
                }
                else
                {
                    json = Encoding.UTF8.GetString( Platform.Current.ReadSaveSlot( saveSlot ) );
                }
                Logger.LogFine( "[API] - Loading Game:" + json );
                SaveGameData saveGameData = JsonUtility.FromJson<SaveGameData>(json);
                global::PlayerData instance = saveGameData.playerData;
                SceneData instance2 = saveGameData.sceneData;
                ModHooks.Instance.OnAfterSaveGameLoad( saveGameData );
                global::PlayerData.instance = instance;
                this.playerData = instance;
                SceneData.instance = instance2;
                this.sceneData = instance2;
                this.profileID = saveSlot;
                this.inputHandler.RefreshPlayerData();
                ModHooks.Instance.OnSavegameLoad( saveSlot );
                result = true;
            }
            catch( Exception ex )
            {
                Debug.LogFormat( "Error loading save file for slot {0}: {1}", new object[]
                {
                saveSlot,
                ex
                } );
                result = false;
            }
            return result;
        }

        #endregion

        #region GetSaveStatsForSlot
        [MonoModReplace]
        public SaveStats GetSaveStatsForSlot(int saveSlot)
        {
            if( !Platform.IsSaveSlotIndexValid( saveSlot ) )
            {
                Debug.LogErrorFormat( "Cannot get save stats for invalid slot {0}", new object[]
                {
                saveSlot
                } );
                return null;
            }
            if( !Platform.Current.IsSaveSlotInUse( saveSlot ) )
            {
                return null;
            }
            SaveStats result;
            try
            {

                bool flag = this.gameConfig.useSaveEncryption && !Platform.Current.IsFileSystemProtected;
                string json;
                if( flag )
                {
                    BinaryFormatter binaryFormatter = new BinaryFormatter();
                    MemoryStream serializationStream = new MemoryStream(Platform.Current.ReadSaveSlot(saveSlot));
                    string encryptedString = (string)binaryFormatter.Deserialize(serializationStream);
                    json = Encryption.Decrypt( encryptedString );
                }
                else
                {
                    json = Encoding.UTF8.GetString( Platform.Current.ReadSaveSlot( saveSlot ) );
                }
                SaveGameData saveGameData = JsonUtility.FromJson<SaveGameData>(json);
                global::PlayerData playerData = saveGameData.playerData;
                SaveStats saveStats = new SaveStats(playerData.maxHealthBase, playerData.geo, playerData.mapZone, playerData.playTime, playerData.MPReserveMax, playerData.permadeathMode, playerData.completionPercentage, playerData.unlockedCompletionRate)
                {
                    Name = saveGameData.Name,
                    LoadedMods = saveGameData.LoadedMods
                };
                result = saveStats;
            }
            catch( Exception ex )
            {
                Debug.LogError( string.Concat( new object[]
                {
                "Error while loading save file for slot ",
                saveSlot,
                " Exception: ",
                ex
                } ) );
                result = null;
            }
            return result;
        }
        #endregion

        #region LoadSceneAdditive

        [MonoModIgnore] private bool tilemapDirty;
        [MonoModIgnore] private bool waitForManualLevelStart;
        [MonoModIgnore] public event GameManager.DestroyPooledObjects DestroyPersonalPools;
        [MonoModIgnore] public event GameManager.UnloadLevel UnloadingLevel;
        [MonoModIgnore] public Scene nextScene { get; private set; }
        [MonoModIgnore] private extern void ManualLevelStart();
        //[MonoModIgnore] public event GameManager.EnterSceneEvent OnFinishedEnteringScene;

        [MonoModReplace]
        public IEnumerator LoadSceneAdditive(string destScene)
        {
            destScene = ModHooks.Instance.BeforeSceneLoad(destScene);
            this.tilemapDirty = true;
            this.startedOnThisScene = false;
            this.nextSceneName = destScene;
            this.waitForManualLevelStart = true;
            if (this.DestroyPersonalPools != null)
            {
                this.DestroyPersonalPools();
            }
            if (this.UnloadingLevel != null)
            {
                this.UnloadingLevel();
            }
            string exitingScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            this.nextScene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(destScene);
            AsyncOperation loadop = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(destScene, LoadSceneMode.Additive);
            loadop.allowSceneActivation = true;
            yield return loadop;
            UnityEngine.SceneManagement.SceneManager.UnloadScene(exitingScene);
            ModHooks.Instance.OnSceneChanged(destScene);
            this.RefreshTilemapInfo(destScene);
            if( this.IsUnloadAssetsRequired( exitingScene, destScene ) )
            {
                Debug.LogFormat( this, "Unloading assets due to zone transition", new object[ 0 ] );
                yield return Resources.UnloadUnusedAssets();
            }
            GCManager.Collect();
            this.SetupSceneRefs( true );
            this.BeginScene();
            this.OnNextLevelReady();
            this.waitForManualLevelStart = false;
            yield break;
        }
        #endregion

        #region LoadFirstScene
        [MonoModReplace]
        public IEnumerator LoadFirstScene()
        {
            yield return new WaitForEndOfFrame();
            this.OnWillActivateFirstLevel();
            this.LoadScene( "Tutorial_01" );
            ModHooks.Instance.OnNewGame();
            yield break;
        }
        #endregion


        #region OnWillActivateFirstLevel
        [MonoModOriginalName( "OnWillActivateFirstLevel" )]
        public void orig_OnWillActivateFirstLevel() { }
                
        public void OnWillActivateFirstLevel()
        {
            orig_OnWillActivateFirstLevel();
            ModHooks.Instance.OnNewGame();
        }
        #endregion
    }
}
