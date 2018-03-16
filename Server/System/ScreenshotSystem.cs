﻿using LunaCommon;
using LunaCommon.Message.Data.Screenshot;
using LunaCommon.Message.Server;
using Server.Client;
using Server.Context;
using Server.Log;
using Server.Server;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Server.System
{
    public class ScreenshotSystem
    {
        private const string SmallFilePrefix = "small_";
        private static readonly string ScreenshotFolder = Path.Combine(ServerContext.UniverseDirectory, "Screenshots");

        private static readonly ConcurrentDictionary<string, DateTime> LastUploadRequest = new ConcurrentDictionary<string, DateTime>();

        /// <summary>
        /// Saves a received screenshot and creates the miniature
        /// </summary>
        public static void SaveScreenshot(ClientStructure client, ScreenshotDataMsgData data)
        {
            Task.Run(() =>
            {
                var playerFolder = Path.Combine(ScreenshotFolder, client.PlayerName);
                if (!Directory.Exists(playerFolder))
                {
                    Directory.CreateDirectory(playerFolder);
                }

                var lastTime = LastUploadRequest.GetOrAdd(client.PlayerName, DateTime.MinValue);
                if (DateTime.Now - lastTime > TimeSpan.FromMilliseconds(CommonConstants.MinScreenshotMsInterval))
                {
                    var fileName = $"{data.Screenshot.DateTaken}.png";
                    if (!File.Exists(fileName))
                    {
                        var fullPath = Path.Combine(playerFolder, fileName);

                        LunaLog.Normal($"Saving screenshot from: {client.PlayerName}. Size: {data.Screenshot.NumBytes} bytes. Filename: {fileName}");
                        FileHandler.WriteToFile(fullPath, data.Screenshot.Data, data.Screenshot.NumBytes);
                        CreateMiniature(fullPath);
                    }
                    else
                    {
                        LunaLog.Warning($"{client.PlayerName} tried to overwrite a screnshot!");
                    }
                }
                else
                {
                    LunaLog.Warning($"{client.PlayerName} is sending screenshots too fast!");
                }
            });
        }

        /// <summary>
        /// Send the screenshot folders that exist on the server
        /// </summary>
        public static void SendScreenshotFolders(ClientStructure client)
        {
            Task.Run(() =>
            {
                var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<ScreenshotFoldersReplyMsgData>();
                msgData.Folders = Directory.GetDirectories(ScreenshotFolder);
                msgData.NumFolders = msgData.Folders.Length;

                MessageQueuer.SendToClient<ScreenshotSrvMsg>(client, msgData);
            });
        }

        /// <summary>
        /// Sends the screenshots in a folder
        /// </summary>
        public static void SendScreenshotList(ClientStructure client, ScreenshotListRequestMsgData data)
        {
            Task.Run(() =>
            {
                var screenshots = new List<ScreenshotInfo>();
                var folder = Path.Combine(ScreenshotFolder, data.FolderName);

                var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<ScreenshotListReplyMsgData>();
                foreach (var file in Directory.GetFiles(folder).Where(f => f.StartsWith(SmallFilePrefix)))
                {
                    var contents = File.ReadAllBytes(file);
                    if (long.TryParse(Path.GetFileNameWithoutExtension(file).Substring(SmallFilePrefix.Length), out var dateTaken))
                    {
                        screenshots.Add(new ScreenshotInfo
                        {
                            Data = contents,
                            DateTaken = dateTaken,
                            NumBytes = contents.Length
                        });
                    }
                }

                msgData.Screenshots = screenshots.ToArray();
                msgData.NumScreenshots = screenshots.Count;

                MessageQueuer.SendToClient<ScreenshotSrvMsg>(client, msgData);
            });
        }

        /// <summary>
        /// Sends the requested screenshot
        /// </summary>
        public static void SendScreenshot(ClientStructure client, ScreenshotDownloadRequestMsgData data)
        {
            Task.Run(() =>
            {
                var file = Path.Combine(ScreenshotFolder, data.FolderName, $"{data.PhotoId}.png");
                if (File.Exists(file))
                {
                    var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<ScreenshotDataMsgData>();
                    msgData.Screenshot.DateTaken = data.PhotoId;
                    msgData.Screenshot.Data = File.ReadAllBytes(file);
                    msgData.Screenshot.NumBytes = msgData.Screenshot.Data.Length;

                    MessageQueuer.SendToClient<ScreenshotSrvMsg>(client, msgData);
                }
            });
        }

        #region Private methods

        private static void CreateMiniature(string path)
        {
            var fileName = Path.GetFileName(path);
            using (var image = Image.FromFile(path))
            using (var newImage = ScaleImage(image, 120, 120))
            {
                // ReSharper disable once AssignNullToNotNullAttribute
                newImage.Save(Path.Combine(Path.GetDirectoryName(path), $"{SmallFilePrefix}{fileName}"), ImageFormat.Png);
            }
        }

        private static Image ScaleImage(Image image, int maxWidth, int maxHeight)
        {
            var ratioX = (double)maxWidth / image.Width;
            var ratioY = (double)maxHeight / image.Height;
            var ratio = Math.Min(ratioX, ratioY);

            var newWidth = (int)(image.Width * ratio);
            var newHeight = (int)(image.Height * ratio);

            var newImage = new Bitmap(newWidth, newHeight);

            using (var graphics = Graphics.FromImage(newImage))
                graphics.DrawImage(image, 0, 0, newWidth, newHeight);

            return newImage;
        }

        #endregion
    }
}
