﻿using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace wowsCheaterViewer
{
    public class Config
    {
        //用Lazy实现单实例
        private static readonly Lazy<Config> _instance = new Lazy<Config>(() => new Config());
        public static Config Instance => _instance.Value;

        //config，会写入文件的属性
        public string wowsRootPath { get; set; }
        public Dictionary<string, List<MarkInfo>> mark { get; set; } = new Dictionary<string, List<MarkInfo>>();
        public Dictionary<string, ShipInfo> shipInfo { get; set; } = new Dictionary<string, ShipInfo>();

        //config，不写入文件的属性
        Logger Logger = Logger.Instance;
        public static bool watchFlag = false;
        public static string updateFolderPath = ".update";
        public static string tempFolderPath = ".temp";
        private static string configPath = @"config.json";
        private static readonly object writerLock = new object();

        //方法
        public void init()//初始化
        {
            if (File.Exists(configPath))
            {
                string configStr = File.ReadAllText(configPath);
                JObject configJson = JObject.Parse(configStr);

                if (configJson.ContainsKey("wowsRootPath"))
                    wowsRootPath = configJson["wowsRootPath"].ToString();

                if (configJson.ContainsKey("mark"))
                {
                    try
                    {
                        //顺利读取
                        mark = JsonConvert.DeserializeObject<Dictionary<string, List<MarkInfo>>>(configJson["mark"].ToString());
                    }
                    catch
                    {
                        //读取失败时（老版本兼容），按结构新建
                        Dictionary<string, string> mark_old = JsonConvert.DeserializeObject<Dictionary<string, string>>(configJson["mark"].ToString());
                        foreach (KeyValuePair<string, string> item in mark_old)
                            mark[item.Key] = new List<MarkInfo> { new MarkInfo { markMessage = item.Value } };
                        update();
                    }
                }

                if (configJson.ContainsKey("shipInfo"))
                    shipInfo = JsonConvert.DeserializeObject<Dictionary<string, ShipInfo>>(configJson["shipInfo"].ToString());

            }
            else
            {
                update();
            }
            watchFlag = checkRootPath(wowsRootPath);
        }
        public void update()//更新配置文件
        {
            lock (writerLock)
            {
                StreamWriter sw = new StreamWriter(configPath);
                sw.Write(JsonConvert.SerializeObject(this).ToString());
                sw.Close();
            }
        }
        public void resetRootPath()//重设路径
        {
            string newPath = null;
            CommonOpenFileDialog dlg = new CommonOpenFileDialog();
            dlg.IsFolderPicker = true;
            dlg.InitialDirectory = @"C:\";

            if (dlg.ShowDialog() == CommonFileDialogResult.Ok)
                newPath = dlg.FileName;

            if (checkRootPath(newPath))
            {
                wowsRootPath = newPath;
                watchFlag = true;
                update();
                Logger.logWrite("重设路径成功");
            }

        }
        private bool checkRootPath(string path)//检查游戏路径
        {
            bool checkRootPath = false;
            if (!string.IsNullOrEmpty(path))
            {
                string repFolderPath = System.IO.Path.Combine(path, "replays");
                string wowsExeFilePath = System.IO.Path.Combine(path, "WorldOfWarships.exe");
                if (Directory.Exists(path))
                    if (Directory.GetFiles(path).Contains(wowsExeFilePath) || Directory.GetDirectories(path).Contains(repFolderPath))
                        checkRootPath = true;
            }

            if(!checkRootPath)
                Logger.logWrite("路径：" + path + "似乎不是游戏根目录，请重新选择");

            return checkRootPath;
        }

        public void addMarkInfo(playerInfo playerInfo)//新增标记
        {
            MarkInfo MarkInfo = new MarkInfo();
            MarkInfo.markTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            MarkInfo.clanTag = playerInfo.clanTag;
            MarkInfo.name = playerInfo.name;
            MarkInfo.markMessage = playerInfo.markMessage;

            //标记记录玩家的id，如果id未获取到，设为玩家名称
            string markKey = playerInfo.playerId;
            if (playerInfo.playerId == "0")
                markKey = playerInfo.name;

            //已有玩家信息就增加，没有就新建
            if (mark.Keys.Contains(markKey))
                mark[markKey].Add(MarkInfo);
            else
                mark[markKey] = new List<MarkInfo> { MarkInfo };

            Logger.logWrite("已更新标记玩家：" + markKey + "，标记内容：" + playerInfo.markMessage);
            update();
        }

        public void addShipInfo(string shipId, ShipInfo ShipInfo)//新增船信息
        {
            if (!shipInfo.Keys.Contains(shipId))
                shipInfo[shipId] = ShipInfo;
            Logger.logWrite("已新增船id：" + shipId );
            update();
        }
    }


    public class MarkInfo
    {
        public string markTime { get; set; }
        public string clanTag { get; set; }
        public string name { get; set; }
        public string markMessage { get; set; }
    }

    public class ShipInfo
    {
        public string nameCn { get; set; }
        public string nameEnglish { get; set; }
        public int level { get; set; }
        public string shipType { get; set; }
        public string country { get; set; }
        public string shipIndex { get; set; }
        public string groupType { get; set; }
    }
}