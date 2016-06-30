using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// FileTransferInfo store data about a file's transfer
/// </summary>
public class FileTransferInfo
{
    /// <summary>
    /// Host player (send the file)
    /// </summary>
    public PhotonPlayer Server;

    /// <summary>
    /// Target player (receive the file)
    /// </summary>
    public PhotonPlayer Client;

    /// <summary>
    /// This file's transfer Id (should be unique)
    /// </summary>
    public short Id;

    /// <summary>
    /// The file's path
    /// </summary>
    public string Path;

    /// <summary>
    /// The total number of bytes to send
    /// </summary>
    public long TotalBytes;

    /// <summary>
    /// The number of bytes already sent/received
    /// </summary>
    public long SentBytes;

    /// <summary>
    /// The number of bytes sent at each Rpc (too low = slow, too high = crash)
    /// </summary>
    public int BytesPerRpc;

    /// <summary>
    /// The number of RPC sent by second (aproximatly)
    /// </summary>
    public int RpcPerSecond;

    /// <summary>
    /// The number of RPC to send required to transfer the file
    /// </summary>
    public int TotalSteps { get { return Mathf.CeilToInt((float)((double)TotalBytes / BytesPerRpc)); } }

    /// <summary>
    /// Is the client ready to receive the file 
    /// </summary>
    public bool ClientReady = false;
}

