﻿using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Automation.Peers;
using System.IO;
using System.Threading;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.WindowsAPICodePack.Dialogs;
using static Microsoft.WindowsAPICodePack.Shell.PropertySystem.SystemProperties.System;
using System.Web;
using System.Diagnostics;
using System.Windows.Threading;
using System.Runtime.CompilerServices;
using Microsoft.WindowsAPICodePack.Shell.Interop;
using System.Reflection;
using System.Net;
using System.Security.Policy;
using System.IO.Compression;
using Path = System.IO.Path;
using System.Reflection.Emit;

namespace 郭楠查看器
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        Logger Logger = Logger.Instance;
        public JObject configJson;
        private JObject markJson;
        private FileSystemWatcher watcher = new FileSystemWatcher();
        private apiClient apiClient = new apiClient();
        private string visionTag = "2023.06.08";
        private string updateFolderPath = ".update";
        Boolean watchFlag = false;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void MainWindowLoaded(object sender, RoutedEventArgs e)//窗口加载完成后，初始化并监控rep文件夹
        {
            init();
            checkUpdate();
            watchRepFolder();
        }


        private void init()//初始化
        {
            markJson = JObject.FromObject(new Dictionary<string, Object>());
            configJson = JObject.FromObject(new Dictionary<string, Object>()
            {
                { "wowsRootPath", null } ,
                { "mark",null }
            });
            //如果不存在，创建日志文件夹
            string logFolder = "log";
            if (!Directory.Exists(logFolder))
                Directory.CreateDirectory(logFolder);
            //删除非当日的日志文件
            foreach (string logFile in Directory.GetFiles(logFolder))
                if (!Path.GetFileName(logFile).StartsWith(DateTime.Now.ToString("yyyy-MM-dd")))
                    File.Delete(logFile);

            string configPath = "config.json";
            if (File.Exists(configPath))
            {
                JObject configJson_readFromFile = ReadJson(configPath);//读配置文件
                foreach(KeyValuePair<string, JToken> configKV in configJson)//将读到的配置覆盖到默认值里
                    if(configJson_readFromFile.ContainsKey(configKV.Key))
                        configJson[configKV.Key] = configJson_readFromFile[configKV.Key];
                markJson = JObject.FromObject(configJson["mark"]);
                rootPath.Text = configJson["wowsRootPath"].ToString();
                logShow("已读取配置文件");
            }
            else
            {
                updateConfigFile();
                logShow("首次运行，已生成配置文件");
            }
            if (!String.IsNullOrEmpty(configJson["wowsRootPath"].ToString()))
                checkPath(configJson["wowsRootPath"].ToString());

        }

        private void checkUpdate()//检测客户端升级
        {
            try
            {
                if (Directory.Exists(updateFolderPath))//每次检测时删除更新文件夹，保证没有脏文件
                    Directory.Delete(updateFolderPath, true);
                string releaseCheckUrl = "https://gitee.com/api/v5/repos/bbaoqaq/wowsCheaterViewer/releases/latest";
                JObject releaseCheckResurnJson = apiClient.GetClient(releaseCheckUrl);

                if (releaseCheckResurnJson["tag_name"].ToString() == visionTag)
                {
                    Logger.logWrite("无需更新");
                }
                else
                {
                    Logger.logWrite("需要更新");
                    string updatalog = releaseCheckResurnJson["body"].ToString();

                    Boolean updataFlag = false;
                    updataFlag = System.Windows.MessageBox.Show("检查到新版本，是否进行更新？"+Environment.NewLine+"更新内容：" + Environment.NewLine + updatalog, "更新提示", MessageBoxButton.OKCancel) == MessageBoxResult.OK;
                    if (updataFlag)
                    {
                        watchFlag = false;
                        System.Threading.Tasks.Task.Run(() =>
                        {
                            logShow("确认更新");
                            //确认能够转换GBK编码
                            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                            string updateZipFilePath = Path.Combine(updateFolderPath, releaseCheckResurnJson["assets"][0]["name"].ToString());
                            string downloadUrl = releaseCheckResurnJson["assets"][0]["browser_download_url"].ToString();
                            Directory.CreateDirectory(updateFolderPath);
                            //下载
                            Boolean downloadFlag = false;
                            using (var web = new WebClient())
                            {
                                web.DownloadProgressChanged += (s, e) =>
                                {
                                    string.Format("正在下载文件：{0}%  ({1}/{2})",
                                        String.Format("{0:D2}", e.ProgressPercentage),
                                        e.BytesReceived,
                                        e.TotalBytesToReceive);
                                    logShow("正在下载："+e.ProgressPercentage.ToString()+"%");
                                };
                                web.DownloadFileCompleted += (s, e) =>
                                {
                                    downloadFlag = true;
                                };
                                web.DownloadFileAsync(new Uri(downloadUrl), updateZipFilePath);
                            }
                            while (!downloadFlag) ;
                            Logger.logWrite("下载完成");
                            //解压
                            ZipFile.ExtractToDirectory(updateZipFilePath, updateFolderPath, Encoding.GetEncoding("GBK"));
                            File.Delete(updateZipFilePath);
                            logShow("解压完成");
                            //生成更新批处理脚本
                            string updateBatPath = Path.Combine(updateFolderPath, "update.bat");
                            string copyFromFolderPath = Directory.GetDirectories(updateFolderPath).First();//取解压后的根文件夹
                            string copyToFolderPath = Environment.CurrentDirectory;//取当前文件夹
                            string processName = Assembly.GetExecutingAssembly().GetName().Name;//取项目名称，也是进程名称
                            string batStr = @"chcp 65001" + Environment.NewLine +//用中文编码
                                "taskkill /f /im " + processName + ".exe " + Environment.NewLine +//结束项目进程
                                "xcopy " + copyFromFolderPath.Replace(" ", @""" """) + " " + copyToFolderPath.Replace(" ", @""" """) + " /e /y " + Environment.NewLine +//覆盖所有需要更新的文件
                                "start " + Path.Combine(copyToFolderPath, processName + ".exe").Replace(" ", @""" """);//重启进程
                            File.Delete(updateBatPath);
                            StreamWriter sw = new StreamWriter(updateBatPath);
                            sw.Write(batStr, Encoding.GetEncoding("GBK"));
                            sw.Close();
                            logShow("即将更新");
                            //启动批处理脚本
                            Process Process = new Process();
                            Process.StartInfo.WorkingDirectory = copyToFolderPath;
                            Process.StartInfo.FileName = updateBatPath;
                            Process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                            Process.Start();
                            Process.WaitForExit();
                            //如果没有正常启动则报错
                            throw new Exception("批处理脚本启动失败");
                        });
                    }
                    else
                    {
                        logShow("用户取消更新");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.logWrite("更新失败，" + ex.Message);
                MessageBox.Show("更新失败，" + ex.Message);
            }
        }

        private void logShow(string message)//显示日志
        {
            Dispatcher.Invoke(() => logText.Text = message);
            Logger.logWrite(message);
        }

        private void readmeEvent(object sender, RoutedEventArgs e)//使用与免责声明
        {
            ReadMeWindow window = new ReadMeWindow();
            window.Show();
        }
        private void reflashEvent(object sender, RoutedEventArgs e)//手动刷新事件
        {
            if (watchFlag)
            {
                logShow("刷新当前对局数据");
                teamView(readTempJson());
            }
            else
            {
                logShow("未设定游戏路径，无法刷新。请设定后重试");
            }
        }

        private void resetRootPathEvent(object sender, RoutedEventArgs e)//重设路径事件
        {
            resetRootPath();
        }
        private void readRepEvent(object sender, RoutedEventArgs e)//读取指定rep文件
        {
            string defaultPath = @"C:\";
            if (!String.IsNullOrEmpty(configJson["wowsRootPath"].ToString()))
                if (checkPath(configJson["wowsRootPath"].ToString()))
                    defaultPath = System.IO.Path.Combine(configJson["wowsRootPath"].ToString(), "replays"); 

            string repPath = null;
            CommonOpenFileDialog dlg = new CommonOpenFileDialog();
            dlg.EnsureReadOnly = true;
            dlg.InitialDirectory = defaultPath;
            dlg.Filters.Add(new CommonFileDialogFilter("战舰世界回放文件", "*.wowsreplay"));

            if (dlg.ShowDialog() == CommonFileDialogResult.Ok)
            {
                repPath = dlg.FileName;
                teamView(readRepJson(repPath));
            }
        }
        private void debugPlayerEvent(object sender, RoutedEventArgs e)//使用与免责声明
        {
            JToken item = JToken.Parse("{\r\n  \"shipId\": 3761190608,\r\n  \"relation\": 2,\r\n  \"id\": 537303632,\r\n  \"name\": \"猫猫不流泪 啤酒喝到醉\"\r\n}");
            playerInfo playerInfo = parsePlayerJson(item);
            PropertyInfo[] properties = playerInfo.GetType().GetProperties();
            for (int i= 0; i < properties.Count(); i++)
                Logger.logWrite(string.Format("{0,-20}",properties[i].Name)+ ":"+ properties[i].GetValue(playerInfo));
            logShow("解析完成，请前往日志文件查看");
        }
        private void resetRootPath()//重设路径
        {
            string newFolder = null;
            CommonOpenFileDialog dlg = new CommonOpenFileDialog();
            dlg.IsFolderPicker = true;
            dlg.InitialDirectory = @"C:\";

            if (dlg.ShowDialog() == CommonFileDialogResult.Ok)
                newFolder = dlg.FileName;

            if (!string.IsNullOrEmpty(newFolder))
                if (checkPath(newFolder))
                {
                    configJson["wowsRootPath"] = newFolder;
                    updateConfigFile();
                    logShow("重设路径成功");
                    rootPath.Text = newFolder;
                    watchFlag = true;
                }

            watchRepFolder();
        }
        private void updateConfigFile()//更新配置文件
        {
            configJson["mark"] = markJson;
            StreamWriter sw = new StreamWriter("config.json");
            sw.Write(configJson.ToString());
            sw.Close();
        }
        private Boolean checkPath(string path)//检查游戏路径
        {
            Boolean checkPath = false;

            if (Directory.Exists(path))
            {
                string repFolderPath = System.IO.Path.Combine(path, "replays");
                string wowsExeFilePath = System.IO.Path.Combine(path, "WorldOfWarships.exe");
                if (Directory.Exists(path))
                    if (Directory.GetFiles(path).Contains(wowsExeFilePath) || Directory.GetDirectories(path).Contains(repFolderPath))
                        checkPath = true;

                if (!checkPath)
                    logShow("路径：" + path + "似乎不是游戏根目录，请重新选择");
            }
            else
            {
                logShow("路径：" + path + "检测失败，请重新选择");
            }
            watchFlag = checkPath;
            return checkPath;

        }
        private void watchRepFolder()//监控rep文件夹
        {
            if(watchFlag)
            {
                watcher.EnableRaisingEvents = false;//先停止监控
                watcher.Path = System.IO.Path.Combine(configJson["wowsRootPath"].ToString(), "replays");
                watcher.NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;
                watcher.Filter = "tempArenaInfo.json";
                watcher.Created += (s, e) => 
                {
                    logShow("检测到对局开始，正在读取");
                    teamView(readTempJson());
                };
                watcher.Deleted += (s, e) => 
                {
                    logShow("检测到对局结束，正在监控rep文件夹");
                };
                watcher.EnableRaisingEvents = true;
                logShow("正在监控rep文件夹");
            }
            else
            {
                logShow("请检查路径是否设置");
            }
        }

        private JObject readTempJson()//读取对局中生成的临时文件
        {
            Thread.Sleep(1000);
            JObject infoJson;
            try
            {
                infoJson = ReadJson(System.IO.Path.Combine(configJson["wowsRootPath"].ToString(), "replays", "tempArenaInfo.json"));
            }
            catch (Exception ex)
            {
                infoJson = null;
                logShow("未能成功读取对局文件，" + ex.Message);
            }
            return infoJson;
        }

        private JObject readRepJson(string path)//读取指定的rep文件
        {
            JObject infoJson;
            string infoStr;
            try
            {
                StreamReader sr = new StreamReader(path);
                infoStr = sr.ReadLine();
                sr.Close();

                infoStr = @"{""matchGroup""" +
                    System.Text.RegularExpressions.Regex.Matches(infoStr, @"(?<={""matchGroup"").*(?=""mapBorder"")").First().ToString() +
                    @"""mapBorder"": null}";
                infoJson = Newtonsoft.Json.Linq.JObject.Parse(infoStr);

                logShow("正在读取rep文件，" + path);
            }
            catch (Exception ex)
            {
                infoJson = null;
                logShow("未能成功读取rep文件，" + ex.Message);
            }
            return infoJson;
        }
        private void teamView(JObject infoJson)//解析对局json，把队伍信息绑定到前台表格中
        {
            if (infoJson != null) 
            {
                JObject markJson_beforeProcess = markJson;
                System.Threading.Tasks.Task.Run(() =>{
                    try
                    {
                        //每次读取时禁用刷新按钮
                        Dispatcher.Invoke(() =>
                        {
                            reflashBtn.IsEnabled = false;
                            readRepBtn.IsEnabled = false;
                        });
                        Stopwatch sw = new Stopwatch();
                        sw.Start();
                        
                        List<playerInfo> playerInfo_team1 = new List<playerInfo>();
                        List<playerInfo> playerInfo_team2 = new List<playerInfo>();
                        List<string> failedList = new List<string>();
                        JArray playerJArray = JArray.FromObject(infoJson["vehicles"]);
                        Int32 readCount = 0;

                        //并行执行
                        ParallelLoopResult parallelResult = Parallel.For(0, playerJArray.Count(), i =>
                        //for(int i = 0; i < playerJArray.Count(); i++)
                        {
                            JToken item = playerJArray[i];
                            playerInfo playerInfo = new playerInfo();
                            string playerName = null;
                            try
                            {
                                playerName = item["name"].ToString();
                                playerInfo = parsePlayerJson(item);

                                //0是用户，1是己方，2是敌方
                                if (Convert.ToInt32(item["relation"]) == 0 && playerInfo.clanId == "7000004849")
                                {
                                    MessageBox.Show("此插件禁止[CV-山东]军团用户使用");
                                    throw new Exception("黑名单用户");
                                }
                                    
                                if (Convert.ToInt32(item["relation"]) == 1 || Convert.ToInt32(item["relation"]) == 0)
                                    playerInfo_team1.Add(playerInfo);
                                else
                                    playerInfo_team2.Add(playerInfo);
                            }
                            catch(Exception ex)
                            {
                                if (ex.Message.Contains("黑名单用户"))
                                    throw ex;
                                failedList.Add(playerName);
                                Logger.logWrite("玩家信息读取失败，"+ex.Message+Environment.NewLine+item.ToString());
                            }
                            finally
                            {
                                readCount = readCount + 1;
                                logShow(string.Format("正在读取对局信息({0}/{1})", 
                                    readCount.ToString(), 
                                    playerJArray.Count()));
                            }
                        });
                        //绑定给前台
                        Dispatcher.Invoke(() =>
                        {
                            team1.ItemsSource = playerInfo_team1.OrderByDescending(i => i.shipSort);
                            team2.ItemsSource = playerInfo_team2.OrderByDescending(i => i.shipSort);
                        });

                        sw.Stop();
                        logShow("已成功读取对局文件，耗时" + (sw.ElapsedMilliseconds / 1000).ToString() + "秒" + (failedList.Count == 0 ? "" : "，以下玩家读取失败：" + string.Join(",", failedList)));
                        sw.Reset();
                    }
                    catch (Exception ex)
                    {
                        logShow("解析对局文件失败，" + ex.Message);
                    }
                    finally
                    {
                        //每次读取完成后启用刷新按钮
                        Dispatcher.Invoke(() =>
                        {
                            reflashBtn.IsEnabled = true;
                            readRepBtn.IsEnabled = true;
                        });
                        //如果标记有变动，则更新
                        if (markJson_beforeProcess != markJson)
                            updateConfigFile();
                    }
                });
            };
        }

        private playerInfo parsePlayerJson(JToken item)//解析单个玩家的json数据
        {
            playerInfo playerInfo = new playerInfo();
            playerInfo.name = System.Text.RegularExpressions.Regex.Unescape(item["name"].ToString());
            playerInfo.shipId = item["shipId"].ToString();
            string hiddenMessage = "hidden";

            //取玩家id
            JObject result_getPlayerId = apiClient.GetPlayerId(playerInfo.name);
            playerInfo.playerId = result_getPlayerId["data"][0]["spa_id"].ToString();
            Boolean isHidden = Convert.ToBoolean(result_getPlayerId["data"][0]["hidden"]);

            Parallel.Invoke(
                () =>//根据船id获取船信息
                {
                    JObject result_shipInfo = apiClient.GetShipInfo(playerInfo.shipId);
                    //解析船信息
                    if (result_shipInfo != null)
                    {
                        playerInfo.shipName = String.Format("{0,-4}", IntToRoman(Convert.ToInt32(result_shipInfo["data"]["level"]))) + result_shipInfo["data"]["nameCn"].ToString();
                        playerInfo.shipSort = shipSort(result_shipInfo);
                    }
                },
                () =>//根据玩家id和船id获取排行，顺便帮雨季收集玩家信息
                {
                    JObject result_getPlayerShipRankSort = apiClient.GetPlayerShipRankSort(playerInfo.playerId, playerInfo.shipId);
                },
                () =>//根据玩家id获取yuyuko的ban信息
                {
                    JObject result_getplayerBanInfo_yuyuko = apiClient.GetPlayerBanInfo_yuyuko(playerInfo.playerId);
                    //解析yuyuko ban信息
                    if (result_getplayerBanInfo_yuyuko != null)
                    {
                        //解析ban信息
                        playerInfo.banMatch_fullStr = result_getplayerBanInfo_yuyuko["data"]["voList"].ToString();
                        playerInfo.banColor = "White";
                        List<Int32> banMatch_matchCountList = new List<Int32>();
                        foreach (JToken banInfo in result_getplayerBanInfo_yuyuko["data"]["voList"] as JArray) //将每一项的封禁匹配数加到list里
                            banMatch_matchCountList.Add(Convert.ToInt32(banInfo["banNameNamesake"]));
                        if (banMatch_matchCountList.Contains(1))//如果有匹配值是1的，标记为红色
                            playerInfo.banColor = "Red";
                        playerInfo.banMatch = string.Join(",", banMatch_matchCountList);
                    }
                },
                () =>//根据玩家id获取官方接口和yuyuko机器人的概览
                {
                    if (!isHidden)
                    {
                        //JObject result_getplayerInfo_official = apiClient.GetPlayerInfo_official(playerInfo.playerId);
                        JObject result_getplayerInfo_yuyuko = apiClient.GetPlayerInfo_yuyuko(playerInfo.playerId);
                        if (result_getplayerInfo_yuyuko != null)
                        {

                            playerInfo.battleCount_pvp = result_getplayerInfo_yuyuko["data"]["pvp"]["battles"].ToString();
                            playerInfo.winRate_pvp = result_getplayerInfo_yuyuko["data"]["pvp"]["wins"].ToString() + "%";
                            playerInfo.battleCount_rank = result_getplayerInfo_yuyuko["data"]["rankSolo"]["battles"].ToString();
                            playerInfo.winRate_rank = result_getplayerInfo_yuyuko["data"]["rankSolo"]["wins"].ToString() + "%";
                            playerInfo.playerPrColor = result_getplayerInfo_yuyuko["data"]["pr"]["color"].ToString();
                            //解析军团信息
                            if (!string.IsNullOrEmpty(result_getplayerInfo_yuyuko["data"]["clanInfo"]["tag"].ToString()))
                            {
                                playerInfo.clanTag = "[" + result_getplayerInfo_yuyuko["data"]["clanInfo"]["tag"].ToString() + "]";
                                playerInfo.clanColor = result_getplayerInfo_yuyuko["data"]["clanInfo"]["colorRgb"].ToString();
                                playerInfo.clanId = result_getplayerInfo_yuyuko["data"]["clanInfo"]["clanId"].ToString();
                            }
                        }
                        else
                        {
                            playerInfo.winRate_pvp = hiddenMessage;
                            playerInfo.winRate_rank = hiddenMessage;
                        }
                    }
                }, 
                () =>//根据船id获取yuyuko机器人中的船信息和玩家单船信息
                {
                    if (!isHidden)
                    {
                        JObject result_getPlayerShipInfo_yuyuko = apiClient.GetPlayerShipInfo_yuyuko(playerInfo.playerId, playerInfo.shipId);
                        //解析玩家单船信息
                        if (result_getPlayerShipInfo_yuyuko != null)
                        {
                            playerInfo.battleCount_ship = result_getPlayerShipInfo_yuyuko["data"]["shipInfo"]["battles"].ToString();
                            playerInfo.winRate_ship = result_getPlayerShipInfo_yuyuko["data"]["shipInfo"]["wins"].ToString() + "%";
                        }
                    }
                    else
                    {
                        playerInfo.winRate_ship = hiddenMessage;
                    }
                }, 
                () =>//检查配置文件中是否有对该玩家的标记信息，并赋值
                {
                    if (markJson.ContainsKey(playerInfo.playerId))
                    {
                        JArray markArray = new JArray();
                        try
                        {
                             markArray = JArray.FromObject(markJson[playerInfo.playerId]);
                        }
                        catch
                        {
                            //如果获取标记失败了，就重新建立map。如果有需要变更标记的才顺便更新
                            Dictionary<string, object> markInfo = new Dictionary<string, object>() {
                                { "markTime",DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")},
                                { "clanTag",playerInfo.clanTag},
                                { "name",playerInfo.name},
                                { "markMessage",markJson[playerInfo.playerId]}
                            };
                            markArray.Add(JObject.FromObject(markInfo));
                            markJson[playerInfo.playerId] = markArray;
                        }
                        markArray = JArray.FromObject(markArray.OrderByDescending(i => Convert.ToDateTime(i["markTime"])));
                        playerInfo.markMessage = markArray.First()["markMessage"].ToString();
                        playerInfo.lastMarkMessage = "上次标记时间："+ markArray.First()["markTime"].ToString()+Environment.NewLine+
                                                     "上次标记时的军团："+ markArray.First()["clanTag"].ToString() + Environment.NewLine +
                                                     "上次标记时的名称：" + markArray.First()["name"].ToString() + Environment.NewLine +
                                                     "上次标记时的内容：" + markArray.First()["markMessage"].ToString();
                    }
                }
            );
            return playerInfo;
        }

        private JObject ReadJson(string path)//读取json文件
        {
            //读取json文件
            JObject infoJson = new JObject();
            using (System.IO.StreamReader file = System.IO.File.OpenText(path))
            {
                using (JsonTextReader reader = new JsonTextReader(file))
                {
                    infoJson = (JObject)JToken.ReadFrom(reader);
                }
            }
            return infoJson;
        }
        private string CalculateWinRate(JObject playerInfo_official, string playerId)//通过官方玩家信息计算pvp胜率
        {
            string winRate = null;
            if (!Convert.ToBoolean(playerInfo_official["data"][playerId]["hidden_profile"]))
            {
                //打rank显示rank胜率，其他显示随机胜率

                string winRate_pvp = null;
                try
                {
                    double playerInfo_sumBattleCount_pvp = Convert.ToDouble(playerInfo_official["data"][playerId]["statistics"]["pvp"]["battles_count"]);
                    double playerInfo_winBattleCount_pvp = Convert.ToDouble(playerInfo_official["data"][playerId]["statistics"]["pvp"]["wins"]);
                    winRate_pvp = (playerInfo_winBattleCount_pvp / playerInfo_sumBattleCount_pvp * 100).ToString("N2") + "%";
                }
                catch { }
                string winRate_rank = null;
                try
                {
                    double playerInfo_sumBattleCount_rank = Convert.ToDouble(playerInfo_official["data"][playerId]["statistics"]["rank_solo"]["battles_count"]);
                    double playerInfo_winBattleCount_rank = Convert.ToDouble(playerInfo_official["data"][playerId]["statistics"]["rank_solo"]["wins"]);
                    winRate_rank = (playerInfo_winBattleCount_rank / playerInfo_sumBattleCount_rank * 100).ToString("N2") + "%";
                }
                catch { }

                winRate = "pvp:  " + winRate_pvp + Environment.NewLine +
                            "rank: " + winRate_rank;
            }
            else
            {
                winRate = "hidden";
            }
            return winRate;
        }
        private string IntToRoman(int num)//数字转罗马字符
        {
            int[] nums = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] romans = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            StringBuilder result = new StringBuilder();
            for (int i = 0; i < 13; i++)
            {
                while (num >= nums[i])
                {
                    result.Append(romans[i]);
                    num -= nums[i];
                }
            }
            return result.ToString();
        }

        private int shipSort(JObject result_shipInfo)//船只排序
        {
            //船只排序：舰种(2)+等级(2)+不知道(2)

            //cv=4,bb=3,ca=2,dd=1,ss=0,default=-1
            string type = result_shipInfo["data"]["shipType"].ToString();
            string[] typeSort = { "Submarine", "Destroyer", "Cruiser", "Battleship", "AirCarrier" };
            type = Array.IndexOf(typeSort, type).ToString();

            string level = result_shipInfo["data"]["level"].ToString();

            string unknowSort = null;


            string shipSortStr = string.Format("{0}{1}{2}",
                String.Format("{0:D2}", type),
                String.Format("{0:D2}", level),
                String.Format("{0:D2}", unknowSort));
            return Convert.ToInt32(shipSortStr);
        }


        private void markMessageChangedEvent(object sender, RoutedEventArgs e)
        {
            playerInfo currentPlayerInfo = new playerInfo();
            if (this.team1.SelectedIndex >= 0)
                currentPlayerInfo = (playerInfo)this.team1.Items[this.team1.SelectedIndex];
            else if (this.team2.SelectedIndex >= 0)
                currentPlayerInfo = (playerInfo)this.team2.Items[this.team2.SelectedIndex];
            else
                logShow("更新玩家标记失败，未能定位到玩家所在队伍");

            if(currentPlayerInfo!=null)
            {
                //config更新
                JArray markArray = new JArray();
                string curruntMarkMessage = currentPlayerInfo.markMessage;
                string lastMarkMessage = "";
                if (markJson.ContainsKey(currentPlayerInfo.playerId))
                {
                    markArray = JArray.FromObject(markJson[currentPlayerInfo.playerId]);
                    markArray = JArray.FromObject(markArray.OrderByDescending(i => Convert.ToDateTime(i["markTime"])));
                    lastMarkMessage = markArray.First()["markMessage"].ToString();
                }
                //标记与上次不同，且都不为空时才进行更新
                if (curruntMarkMessage != lastMarkMessage && !(string.IsNullOrEmpty(curruntMarkMessage) && string.IsNullOrEmpty(lastMarkMessage)))
                {
                    Dictionary<string, object> markInfo = new Dictionary<string, object>() {
                        { "markTime",DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")},
                        { "clanTag",currentPlayerInfo.clanTag},
                        { "name",currentPlayerInfo.name},
                        { "markMessage",currentPlayerInfo.markMessage}
                    };
                    markArray.Add(JObject.FromObject(markInfo));
                    markJson[currentPlayerInfo.playerId] = markArray;
                    updateConfigFile();
                    logShow("已更新标记玩家：" + currentPlayerInfo.playerId + "，标记内容：" + currentPlayerInfo.markMessage);
                }
            }
        }
    }

    public class playerInfo
    {
        public string name { get; set; }
        public string playerId { get; set; }
        public string playerPrColor { get; set; }
        public string shipId { get; set; }
        public string clanTag { get; set; }
        public string clanId { get; set; }
        public string clanColor { get; set; }
        public string shipName { get; set; }
        public int shipSort { get; set; }
        public string banMatch { get; set; }
        public string banMatch_fullStr { get; set; }
        public string banColor { get; set; }
        public string battleCount_ship { get; set; }
        public string winRate_ship { get; set; }
        public string battleCount_pvp { get; set; }
        public string winRate_pvp { get; set; }
        public string battleCount_rank { get; set; }
        public string winRate_rank { get; set; }
        public string markMessage { get; set; }
        public string lastMarkMessage { get; set; }
    }
}