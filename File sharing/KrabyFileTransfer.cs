using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Photon;
using System.Collections;

/// <summary>
/// KrabyFileTransfer allows sending files throught PhotonNetwork. Keep only 1 KrabyFileTransfer component in a scene
/// </summary>
public class KrabyFileTransfer : Photon.MonoBehaviour
{
    #region Events
    /// <summary>
    /// The delegate which handle event about a FileTransferInfo
    /// </summary>
    /// <param name="transferInfo">The FileTransferInfo concerned</param>
    public delegate void FileTransferEventHandler(FileTransferInfo transferInfo);

    /// <summary>
    /// The delegate which handle event about a FileTransferInfo error
    /// </summary>
    /// <param name="transferInfo">The FileTransferInfo concerned</param>
    /// <param name="errorInfo">The error which cause the event to be called</param>
    public delegate void FileTransferErrorEventHandler(FileTransferInfo transferInfo, string errorInfo);

    /// <summary>
    /// Event called when a file has been downloaded
    /// </summary>
    public event FileTransferEventHandler OnFileDownloaded;

    /// <summary>
    /// Event called when a file has been uploaded
    /// </summary>
    public event FileTransferEventHandler OnFileUploaded;

    /// <summary>
    /// Event called when a file download failed
    /// </summary>
    public event FileTransferErrorEventHandler OnFileDownloadFailed;


    /// <summary>
    /// Event called when a file upload failed
    /// </summary>
    public event FileTransferErrorEventHandler OnFileUploadFailed;
    #endregion

    #region Constants

    //Default parameters
    public const float SecondsBetweenClientReadyCheck = 0.1f;
    public const int BytesPerRpcDefault = 10000;
    public const int RpcPerSecondDefault = 10;
    public const float ServerTimeoutTimeDefault = 5f;
    public const float ClientTimeoutTimeDefault = 15f;
    #endregion

    #region Public variables

    /// <summary>
    /// The KrabyFileTransfer singleton
    /// </summary>
    public static KrabyFileTransfer singleton;

    /// <summary>
    /// List of received files
    /// </summary>
    public static List<FileTransferInfo> Downloads = new List<FileTransferInfo>();

    /// <summary>
    /// List of sent files
    /// </summary>
    public static List<FileTransferInfo> Uploads = new List<FileTransferInfo>();
    #endregion

    #region Private variables

    //Store temporarly the received bytes
    private static Dictionary<FileTransferInfo, Dictionary<int, byte[]>> DownloadsReceived = new Dictionary<FileTransferInfo, Dictionary<int, byte[]>>();
    #endregion


    private void Start()
    {
        //Check for existing singleton
        if (singleton == null)
        {
            singleton = this;
        }
        else //If singleton already exist, destroy this
        {
            Debug.LogError("[KrabyFileTransfer]A KrabyFileTransfer component has been added while another one already exists (only 1 KrabyFileTransfer should exists). The new one was destroyed");
            Destroy(this);
        }
    }

    #region Public functions

    /// <summary>
    /// Send a file located in path to targetPlayer
    /// </summary>
    /// <param name="path">The file's path</param>
    /// <param name="targetPlayer">The target player</param>
    /// <param name="bytesPerRpc">The number of bytes sent in each RPC </param>
    /// <param name="rpcPerSecond">The number of RPC sent each second (not exact)</param>
    public void SendFile(string path, PhotonPlayer targetPlayer, float timeOutTime = ServerTimeoutTimeDefault, int bytesPerRpc = BytesPerRpcDefault, int rpcPerSecond = RpcPerSecondDefault)
    {
        //Setup the file's transfer info
        FileTransferInfo transferInfo = new FileTransferInfo();
        transferInfo.Path = path;
        transferInfo.BytesPerRpc = bytesPerRpc;
        transferInfo.RpcPerSecond = rpcPerSecond;
        transferInfo.Client = targetPlayer;
        transferInfo.Server = PhotonNetwork.player;
        transferInfo.Id = 1;

        //Gather a clean/unused id for the transfer
        bool cleanId = true;
        do {
            cleanId = true;
            foreach (FileTransferInfo info in Uploads)
            {
                if (info.Client.ID == transferInfo.Client.ID && info.Id == transferInfo.Id)
                {
                    if (transferInfo.Id < 32765)
                        transferInfo.Id++;
                    else
                        transferInfo.Id = (short)UnityEngine.Random.Range(0, 32766);
                    cleanId = false;
                    break;
                }

            }
        } while(!cleanId);

        //Try to get the total bytes amount (it's also used to check if the stream works fine)
        try
        {
            using (FileStream fileStream = new FileStream(transferInfo.Path, FileMode.Open))
            {
                transferInfo.TotalBytes = fileStream.Length;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[KrabyFileTransfer]ERROR: Can't read the target file. Details:" + ex.Message);
            return;
        }

        Uploads.Add(transferInfo);  //Finally add this transferInfo to the uploads list
        StartCoroutine(SendFileProtocol(transferInfo, timeOutTime));
    }

    #endregion

    #region File sending/receiving functions

    //The Coroutine that sends the file
    private IEnumerator SendFileProtocol(FileTransferInfo transferInfo, float timeOutTime) {
        //Send setup informations to the client
        object[] setupParameters = new object[5] { transferInfo.Path, transferInfo.Server.ID, transferInfo.TotalBytes, transferInfo.BytesPerRpc, transferInfo.Id };
        photonView.RPC("RpcSetupTransferInfo", transferInfo.Client, setupParameters);
        //Wait until the client is ready
        float startTime = Time.time;
        bool clientReady = false;
        bool timeOut = false;
        do
        {
            if (transferInfo.ClientReady)
                clientReady = true;
            else if (Time.time - startTime >= timeOutTime)
                timeOut = true;
            yield return new WaitForSeconds(SecondsBetweenClientReadyCheck);
        } while (!clientReady && !timeOut);

        if (timeOut) //If there is no response from the client
        {
            OnFileUploadFailed(transferInfo, "Timeout: no response from the client");
        }
        else //Else, send the file
        {
            //Start sending the file
            List<byte> bytePacket = new List<byte>();
            int step = 0;
            using (FileStream fileStream = new FileStream(transferInfo.Path, FileMode.Open))
            {
                for (int i = 0; i < fileStream.Length; i++)
                {
                    int thisByte = fileStream.ReadByte();
                    bytePacket.Add(Convert.ToByte(thisByte));

                    if (bytePacket.Count == transferInfo.BytesPerRpc || i == fileStream.Length - 1) //If reached max byte per packet or end of the stream
                    {
                        //Send the current byte packet to the client
                        object[] transferParameters = new object[4] { PhotonNetwork.player.ID, transferInfo.Id, step, bytePacket.ToArray() };
                        photonView.RPC("RpcTransferBytes", transferInfo.Client, transferParameters);
                        transferInfo.SentBytes += bytePacket.Count;
                        bytePacket.Clear();
                        step++;
                        yield return new WaitForSeconds((float)1 / transferInfo.RpcPerSecond);
                    }
                }
            }

            if (OnFileUploaded != null)
            {
                OnFileUploaded(transferInfo);
            }
        }

    }

    //The Coroutine that receive the file
    private IEnumerator ReceiveFileProtocol(FileTransferInfo transferInfo)
    {
        int step = 0;
        bool timeout = false;
        float lastReceivedByteTime = Time.time;

        using (FileStream fileStream = new FileStream(transferInfo.Path, FileMode.Create))
        {
            while (step != transferInfo.TotalSteps && !timeout)
            {
                if (DownloadsReceived.ContainsKey(transferInfo) && DownloadsReceived[transferInfo].ContainsKey(step))
                {

                    for (int i = 0; i < DownloadsReceived[transferInfo][step].Length; i++)
                    {
                        fileStream.WriteByte(DownloadsReceived[transferInfo][step][i]);
                    }
                    //Update currently received bytes count, erase the now useless byte data and go to next step
                    transferInfo.SentBytes += DownloadsReceived[transferInfo][step].Length;
                    DownloadsReceived[transferInfo].Remove(step);
                    step++;
                }
                if (Time.time - lastReceivedByteTime > ClientTimeoutTimeDefault)
                {
                    timeout = true;
                    OnFileDownloadFailed(transferInfo, "Timeout: no response from the server for " + ClientTimeoutTimeDefault + " seconds");
                }

                yield return true;
            }
        }

        if (!timeout && OnFileDownloaded != null)
        {
            OnFileDownloaded(transferInfo);
        }
    }
    
    private bool ReceiveFile(FileTransferInfo transferInfo)
    {
        //Should do something to test if the player can edit the file
        StartCoroutine(ReceiveFileProtocol(transferInfo));
        return true;
    }

    #endregion

    #region RPCs
    //Tell the server that the client is ready
    [RPC]
    private void RpcSendReadyInfo(int clientId, short transferId)
    {
        FileTransferInfo info = GetFileTransferInfo(Uploads, clientId, transferId);
        info.ClientReady = true;
    }

    //Send the informations about the file and file's transfer to the client
    [RPC]
    private void RpcSetupTransferInfo(string fileName, int serverId, long totalBytes, int bytesPerRpc, short transferId)
    {
        FileTransferInfo transferInfo = new FileTransferInfo();
        transferInfo.Path = fileName;
        transferInfo.Id = transferId;
        transferInfo.TotalBytes = totalBytes;
        transferInfo.BytesPerRpc = bytesPerRpc;
        foreach (PhotonPlayer player in PhotonNetwork.playerList)
        {
            if (player.ID == serverId)
                transferInfo.Server = player;
        }
        transferInfo.Client = PhotonNetwork.player;

        if (transferInfo.Server == null)
        {
            OnFileDownloadFailed(transferInfo, "Incorrect server ID: the received server ID doesn't match any player");
            return; //Error: server player not found
        }


        Downloads.Add(transferInfo);
        DownloadsReceived.Add(transferInfo, new Dictionary<int, byte[]>());

        bool ready = ReceiveFile(transferInfo);

        if (ready)
        {
            //Tell the server that this client is ready for file reception
            object[] readyParameters = new object[2] { transferInfo.Client.ID, transferInfo.Id };
            photonView.RPC("RpcSendReadyInfo", transferInfo.Server, readyParameters);
        }

        Debug.LogError("Receiving " + transferInfo.Path + " ...");
    }

    //Send the file's bytes to the client
    [RPC]
    private void RpcTransferBytes(int serverId, short transferId, int step, byte[] bytes) {
        FileTransferInfo transferInfo = GetFileTransferInfo(Downloads, serverId, transferId);
        if (transferInfo == null)
        {
            return; //ERROR: Receiving a file not initialized
        }

        DownloadsReceived[transferInfo].Add(step, bytes);
    }

    #endregion

    #region Utility functions
    private static FileTransferInfo GetFileTransferInfo(List<FileTransferInfo> list, int otherId, short transferId)
    {
        foreach (FileTransferInfo info in list)
            if ((info.Server.ID == otherId || info.Client.ID == otherId) && info.Id == transferId)
                return info;
        //If nothing was found, return null
        return null;
    }
    #endregion
}
