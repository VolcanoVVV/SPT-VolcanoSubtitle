using BepInEx.Logging;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Common.Utils;
using Subtitle;
using Subtitle.Config;
using Subtitle.Utils;
using SubtitleSystem;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using static Subtitle.Config.Settings;

[HarmonyPatch]
public static class SubtitlePatch
{
    // 临时日志源（发布前可删除）
    private static readonly ManualLogSource s_Log =
        BepInEx.Logging.Logger.CreateLogSource("Subtitle.Debug");
    private static Dictionary<string, string> s_UserRoleMapExact;
    private static List<KeyValuePair<string, string>> s_UserRoleMapPrefix; // key: prefix(lower), value: label
    private static bool s_UserRoleMapLoaded;
    private static string s_UserRoleMapPath;
    private static float s_LastZombieSubtitleTime = -999f;
    private static float s_LastZombieDanmakuTime = -999f;
    private static float s_LastZombieWorld3DTime = -999f;
    private static readonly Dictionary<string, float> s_RecentVoiceOnce = new Dictionary<string, float>();

    // —— 语音事件去重：同一 spkId+netId 在窗口内只处理一次 —— //
    private static float GetDupWindowSec()
    {
        try
        {
            if (Settings.VoiceDedupWindowSec != null)
            {
                float v = Settings.VoiceDedupWindowSec.Value;
                if (v < 0f) v = 0f;
                if (v > 1.0f) v = 1.0f;
                return v;
            }
        }
        catch { }
        return 0.40f;
    }


    // 获取 JSONC 路径
    private static string GetRoleTypeJsoncPath()
    {
        if (!string.IsNullOrEmpty(s_UserRoleMapPath)) return s_UserRoleMapPath;
        try
        {
            // BepInEx\plugins\subtitle\locales\ch\RoleType.jsonc
            var baseDir = BepInEx.Paths.PluginPath;
            var path = Path.Combine(Application.dataPath, "..", "BepInEx", "plugins", "subtitle", "locales", "ch", "RoleType.jsonc");
            s_UserRoleMapPath = path;
            return path;
        }
        catch { return null; }
    }

    // 去掉 JSONC 注释（// 和 /* */），避免依赖外部库
    private static string StripJsonComments(string src)
    {
            if (string.IsNullOrEmpty(src)) return src;
    var sb = new StringBuilder(src.Length);
    bool inStr = false;
            for (int i = 0; i < src.Length; i++)
                {
        char c = src[i];
                    if (c == '"')
                        {
                            // 处理转义引号
            bool escaped = (i > 0 && src[i - 1] == '\\');
                            if (!escaped) inStr = !inStr;
            sb.Append(c);
                        }
                    else if (!inStr && c == '/' && i + 1 < src.Length)
                        {
            char n = src[i + 1];
                            // 行注释 //
                            if (n == '/')
                                {
                i += 2;
                                    while (i < src.Length && src[i] != '\n') i++;
                sb.Append('\n');
                                }
                            // 块注释 /* ... */
                            else if (n == '*')
                                {
                i += 2;
                                    while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/')) i++;
                i++; // 跳过 '/'
                                }
                            else
                                {
                sb.Append(c);
                                }
                        }
                    else
                        {
            sb.Append(c);
                        }
                }
            return sb.ToString();
        }

    // 极简字典解析：匹配 "key": "value" 对（不支持嵌套/转义极端场景，但够用）
    private static Dictionary<string, string> ParseJsoncToDict(string jsonc)
    {
    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(jsonc)) return dict;
    string json = StripJsonComments(jsonc);
    var rx = new Regex("\"([^\"]+)\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.Multiline);
    var m = rx.Matches(json);
            foreach (Match it in m)
                {
        var k = it.Groups[1].Value;
        var v = it.Groups[2].Value;
                    if (!string.IsNullOrEmpty(k)) dict[k] = v ?? "";
                }
            return dict;
        }

    // 载入用户映射（只做一次；如需热更新可加时间戳判断）
    private static void EnsureUserRoleMapLoaded()
    {
            if (s_UserRoleMapLoaded) return;
    s_UserRoleMapLoaded = true;
    s_UserRoleMapExact = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    s_UserRoleMapPrefix = new List<KeyValuePair<string, string>>();
            try
        {
        var path = GetRoleTypeJsoncPath();
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            var user = ParseJsoncToDict(text);
                            foreach (var kv in user)
                                {
                var key = (kv.Key ?? "").Trim();
                var val = kv.Value ?? "";
                                    if (key.Length == 0) continue;
                                    if (key[key.Length - 1] == '*')
                                        {
                    var prefix = key.Substring(0, key.Length - 1).ToLowerInvariant();
                    s_UserRoleMapPrefix.Add(new KeyValuePair<string, string>(prefix, val));
                                        }
                                    else
                                        {
                    s_UserRoleMapExact[key] = val;
                                        }
                                }
                            // 可选：日志
                            try { s_Log.LogInfo("[Subtitle] RoleType.jsonc loaded: " + user.Count + " entries"); } catch { }
                        }
                    else
                        {
                            try { s_Log.LogInfo("[Subtitle] RoleType.jsonc not found, using defaults"); } catch { }
                        }
                }
            catch (Exception e)
        {
                    try { s_Log.LogWarning("[Subtitle] load RoleType.jsonc failed: " + e); } catch { }
                }
        }

    // 忽略的语音触发类型（仍保留）
    private static string TryGetAccountId(IPlayer p)
    {
        try
        {
            if (p == null) return null;
            // 先直接从 Player 上拿
            var direct = Traverse.Create(p).Property("AccountId")?.GetValue() as string;
            if (!string.IsNullOrEmpty(direct)) return direct;

            // 再从 Profile 上拿
            var prof = p.Profile;
            if (prof != null)
            {
                var tp = Traverse.Create(prof);
                var acc = tp.Property("AccountId")?.GetValue()?.ToString();
                if (!string.IsNullOrWhiteSpace(acc)) return acc;

                // 有些版本用 Id 作为账户/档案唯一标识
                var pid = tp.Property("Id")?.GetValue()?.ToString();
                if (!string.IsNullOrWhiteSpace(pid)) return pid;
            }
        }
        catch { }
        return null;
    }

    private static object GetBotOwnerByPlayer(IPlayer p)
    {
        try
        {
            // AIData.BotOwner 直取
            var aiData = Traverse.Create(p).Property("AIData")?.GetValue() ?? Traverse.Create(p).Field("AIData")?.GetValue();
            var bo = aiData != null
                ? (Traverse.Create(aiData).Property("BotOwner")?.GetValue() ?? Traverse.Create(aiData).Field("BotOwner")?.GetValue())
                : null;
            if (bo != null) return bo;

            // 兜底：从 GameWorld 的 BotOwners 集合里匹配 Player
            var gw = Comfort.Common.Singleton<GameWorld>.Instance;
            if (gw != null)
            {
                var bots = Traverse.Create(gw).Property("BotOwners")?.GetValue() as System.Collections.IEnumerable
                        ?? Traverse.Create(gw).Field("BotOwners")?.GetValue() as System.Collections.IEnumerable
                        ?? Traverse.Create(gw).Field("_allBots")?.GetValue() as System.Collections.IEnumerable;
                if (bots != null)
                {
                    foreach (var b in bots)
                    {
                        var bp = Traverse.Create(b).Property("Player")?.GetValue();
                        if (object.ReferenceEquals(bp, p)) return b;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private static string GetAITypeOrPlayer(IPlayer p)
    {
        if (p == null) return "unknown";
        if (!p.IsAI) return "player";

        // 1) 强类型：Profile.Info.Settings.Role（4.0 可直接用）
        try
        {
            var role = p.Profile.Info.Settings.Role;   // WildSpawnType
            return role.ToString();
        }
        catch { }

        // 2) 备选：BotOwner.WildSpawnType / Role
        try
        {
            var aiData = Traverse.Create(p).Property("AIData")?.GetValue() ?? Traverse.Create(p).Field("AIData")?.GetValue();
            var botOwner = aiData != null
                ? (Traverse.Create(aiData).Property("BotOwner")?.GetValue() ?? Traverse.Create(aiData).Field("BotOwner")?.GetValue())
                : null;

            if (botOwner != null)
            {
                var wst = Traverse.Create(botOwner).Property("WildSpawnType")?.GetValue()
                       ?? Traverse.Create(botOwner).Field("WildSpawnType")?.GetValue()
                       ?? Traverse.Create(botOwner).Property("Role")?.GetValue()
                       ?? Traverse.Create(botOwner).Field("Role")?.GetValue();

                if (wst != null) return wst.ToString();
            }
        }
        catch { }

        return "ai";
    }

    private static string MapAITypeLabel(string aiTypeRaw)
    {
        if (string.IsNullOrEmpty(aiTypeRaw)) return "ai";

        // 先加载用户映射
        EnsureUserRoleMapLoaded();
        // 1) 用户精确映射
        string mapped;
                if (s_UserRoleMapExact != null && s_UserRoleMapExact.TryGetValue(aiTypeRaw, out mapped) && !string.IsNullOrEmpty(mapped))
                        return mapped;
        
                // 2) 用户前缀映射（写法： "followerGluhar*": "Gluhar follower" ）
                if (s_UserRoleMapPrefix != null && s_UserRoleMapPrefix.Count > 0)
                    {
            var lower = aiTypeRaw.ToLowerInvariant();
                        for (int i = 0; i < s_UserRoleMapPrefix.Count; i++)
                            {
                var kv = s_UserRoleMapPrefix[i];
                                if (lower.StartsWith(kv.Key)) return string.IsNullOrEmpty(kv.Value) ? aiTypeRaw : kv.Value;
                            }
                    }

        // 3) 内置默认映射
        if (SubtitleEnum.DEFAULT_AI_TYPE_LABELS.TryGetValue(aiTypeRaw, out mapped))
            return mapped;

        return aiTypeRaw; // 找不到映射就用原始 aiType
    }

    // 在文件中（和 MapAITypeLabel 同级）新增：voiceKey → 标签 的映射函数
    private static string MapVoiceKeyLabel(string voiceKey)
    {
        if (string.IsNullOrEmpty(voiceKey)) return "Voice";
        string mapped;
        if (SubtitleEnum.DEFAULT_VOICE_KEY_LABELS.TryGetValue(voiceKey, out mapped) && !string.IsNullOrEmpty(mapped))
            return mapped;

        // 兜底：做一点点美化（不直接裸露原 key 形态）
        try
        {
            // usec_1 -> USEC-1
            var pretty = voiceKey.Replace('_', '-').ToUpperInvariant();
            return string.IsNullOrEmpty(pretty) ? "Voice" : pretty;
        }
        catch { return "Voice"; }
    }

    private static string GetDisplayName(IPlayer p)
    {
        if (p == null) return "Unknown";

        // 1) 先用 Profile.Nickname（玩家/AI 都常有）
        try
        {
            var prof = p.Profile;
            if (prof != null)
            {
                var nickObj = Traverse.Create(prof).Property("Nickname")?.GetValue();
                var nick = nickObj != null ? nickObj.ToString() : null;
                if (!string.IsNullOrEmpty(nick)) return nick;
            }
        }
        catch { }

        // 2) AI：尝试从 PlayerOwner/BotOwner 取昵称/名字
        if (p.IsAI)
        {
            try
            {
                // PlayerOwner.Nickname（BotDebug 里证实有效）
                var aiData = Traverse.Create(p).Property("AIData")?.GetValue() ?? Traverse.Create(p).Field("AIData")?.GetValue();
                var playerOwner = aiData != null
                    ? (Traverse.Create(aiData).Property("PlayerOwner")?.GetValue() ?? Traverse.Create(aiData).Field("PlayerOwner")?.GetValue())
                    : null;
                if (playerOwner != null)
                {
                    var ownerNickObj = Traverse.Create(playerOwner).Property("Nickname")?.GetValue();
                    var ownerNick = ownerNickObj != null ? ownerNickObj.ToString() : null;
                    if (!string.IsNullOrEmpty(ownerNick)) return ownerNick;
                }

                // BotOwner.Name 或 Profile.Nickname
                var botOwner = GetBotOwnerByPlayer(p);
                if (botOwner != null)
                {
                    var boNameObj = Traverse.Create(botOwner).Property("Name")?.GetValue();
                    var boName = boNameObj != null ? boNameObj.ToString() : null;
                    if (!string.IsNullOrEmpty(boName)) return boName;

                    var boProf = Traverse.Create(botOwner).Property("Profile")?.GetValue();
                    if (boProf != null)
                    {
                        var boNickObj = Traverse.Create(boProf).Property("Nickname")?.GetValue();
                        var boNick = boNickObj != null ? boNickObj.ToString() : null;
                        if (!string.IsNullOrEmpty(boNick)) return boNick;
                    }
                }
            }
            catch { }
        }

        // 3) 兜底：AccountId → ProfileId → Profile.Id
        var acc = TryGetAccountId(p);
        if (!string.IsNullOrEmpty(acc)) return acc;

        var pidObj = Traverse.Create(p).Property("ProfileId")?.GetValue();
        if (pidObj != null) return pidObj.ToString();

        var profId = p.Profile != null ? Traverse.Create(p.Profile).Property("Id")?.GetValue() : null;
        if (profId != null) return profId.ToString();

        return "Unknown";
    }

    // 实际说话层：只做 Postfix
    [HarmonyPatch(typeof(PhraseSpeakerClass), "Play")]
    [HarmonyPostfix]
    public static void PhraseSpeakerPlayPostfix(
    PhraseSpeakerClass __instance,
    EPhraseTrigger trigger,
    ETagStatus tags,
    bool demand,
    int? importance,
    ref TagBank __result)
    {
        // 1) 失败/忽略直接退出
        if (__result == null) return;
        // 2) 取到已选中的具体剪辑
        var clip = Traverse.Create(__instance).Field("Clip").GetValue() as TaggedClip;
        if (clip == null) return;

        // ★ 统一声明一次 GameWorld
        GameWorld gw = Comfort.Common.Singleton<GameWorld>.Instance;

        // 3) 解析说话者（优先：对象索引 → 再兜底）
        var byDict = SpeakerIndex.TryGetBySpeaker(__instance);
        IPlayer speakerPlayer = byDict;
        if (speakerPlayer == null && gw != null)
        {
            // 极小概率：注册时没抓到，说话时再从当前已知玩家补一次索引
            var list = Traverse.Create(gw).Property("AllAlivePlayersList")?.GetValue() as System.Collections.IEnumerable;
            if (list != null)
            {
                foreach (var o in list)
                {
                    var ip = o as IPlayer;
                    if (ip != null) Subtitle.Utils.SpeakerIndex.IndexPlayer(ip);
                }
                speakerPlayer = Subtitle.Utils.SpeakerIndex.TryGetBySpeaker(__instance);
            }
        }

        // 失败再走 Id 映射 / 反射兜底
        if (speakerPlayer == null) speakerPlayer = TryResolveByProfileMap(__instance);
        if (speakerPlayer == null) speakerPlayer = TryResolveSpeakerOwnerStrong(__instance);
        if (speakerPlayer == null) speakerPlayer = TryResolveSpeakerOwnerFallback(__instance);

        // 4) 三键：voiceKey / trigger / netId
        var netIdStr = clip != null ? clip.NetId.ToString() : null;

        // ✅ 这里用“玩家优先”的智能解析
        string voiceKey = ResolveVoiceKeySmart(speakerPlayer, __instance);

        // 5) 三键查字幕
        string textSub = PhraseSubtitle.GetSubtitleForChannel("Subtitle", voiceKey, trigger.ToString(), netIdStr);
        string textDm = PhraseSubtitle.GetSubtitleForChannel("Danmaku", voiceKey, trigger.ToString(), netIdStr);
        string textW3d = PhraseSubtitle.GetSubtitleForChannel("World3D", voiceKey, trigger.ToString(), netIdStr);
        if (string.IsNullOrEmpty(textSub) && string.IsNullOrEmpty(textDm) && string.IsNullOrEmpty(textW3d)) return;

        // —— 把这些前置：mainPlayer / isLocalSpeaker / isFriendly ——
        // 次信息与过滤：距离 & 友军判定（前置）
        IPlayer mainPlayer = gw != null ? gw.MainPlayer as IPlayer : null;
        bool isLocalSpeaker = (speakerPlayer != null && speakerPlayer.IsYourPlayer);
        // 依赖 FriendlyUtils 扩展（已 using Subtitle.Utils;）
        bool isFriendly = (!isLocalSpeaker) && (speakerPlayer != null && speakerPlayer.IsFriendlyToMain());

        // 7) 调试日志：aiType 与 name
        try
        {
            string aiType = GetAITypeOrPlayer(speakerPlayer);   // player / WildSpawnType / ai
            string nameForLog = GetDisplayName(speakerPlayer);  // 玩家/AI 昵称优先

            if (Settings.EnableDebugTools != null && Settings.EnableDebugTools.Value)
            {
                s_Log.LogInfo(
                "[SubtitleDbg] voiceKey=" + voiceKey +
                " trigger=" + trigger +
                " tags=" + tags +
                " netId=" + netIdStr +
                " len=" + clip.Length.ToString("F2") + "s " +
                " bank=" + __result.name +
                " aiType=" + aiType +
                " name=" + nameForLog +
                " friendly=" + (isFriendly ? "1" : "0"));
            }
        }
        catch (Exception e)
        {
            s_Log.LogWarning("[SubtitleDbg] log failed: " + e);
        }

        // === 新颜色/文本拼接（四分法：按说话者类别 × 频道） ===
        string aiTypeRaw = GetAITypeOrPlayer(speakerPlayer);

        // 统一入口：仅用 Settings 的分类器判 AI
        var kind = Subtitle.Config.Settings.GuessRoleKindFromAiType(aiTypeRaw);

        // 玩家/队友覆盖（保证自己/友军永远归到 Player/Teammate）
        if (isLocalSpeaker) kind = Subtitle.Config.Settings.RoleKind.Player;
        else if (isFriendly) kind = Subtitle.Config.Settings.RoleKind.Teammate;

        Color colorSub = Settings.GetTextColor(kind, Settings.Channel.Subtitle);
        Color colorDm = Settings.GetTextColor(kind, Settings.Channel.Danmaku);
        Color colorW3d = Settings.GetTextColor(kind, Settings.Channel.World3D);

        // 1) 先拿“基准 roletag”（不含冒号，按频道区分代称）
        string baseRoleSub = GetRoleTagFromPlayer(speakerPlayer, Settings.Channel.Subtitle, __instance);
        string baseRoleDm = GetRoleTagFromPlayer(speakerPlayer, Settings.Channel.Danmaku, __instance);
        string baseRoleW3d = GetRoleTagFromPlayer(speakerPlayer, Settings.Channel.World3D, __instance);

        // 2) 判定是不是 PMC / Scav（兼容 AI 与玩家）：
        bool isPMC = false, isSCAV = false;
        try
        {
            // 先看 AI 归类（Settings.GuessRoleKindFromAiType 的结果）
            isPMC = (kind == Settings.RoleKind.PmcBear || kind == Settings.RoleKind.PmcUsec);
            isSCAV = (kind == Settings.RoleKind.Scav);

            // 玩家侧再兜底一次——按 Side 区分 PMC/Scav
            if (speakerPlayer != null && !speakerPlayer.IsAI)
            {
                if (speakerPlayer.Side == EFT.EPlayerSide.Bear || speakerPlayer.Side == EFT.EPlayerSide.Usec) isPMC = true;
                if (speakerPlayer.Side == EFT.EPlayerSide.Savage) isSCAV = true;
            }
        }
        catch { }

        // 3) 依据频道选项决定“是否用名字替代 roletag”
        string nameForShow = GetDisplayName(speakerPlayer); // 调试日志里用的同一个名字来源
        string roleTagSubText = baseRoleSub;   // 字幕 roletag 原文
        string roleTagDmText = baseRoleDm;   // 弹幕 roletag 原文
        string roleTagW3dText = baseRoleW3d;  // World3D roletag 原文

        if (isPMC)
        {
            if (Settings.SubtitleShowPmcName != null && Settings.SubtitleShowPmcName.Value) roleTagSubText = string.IsNullOrEmpty(nameForShow) ? baseRoleSub : nameForShow;
            if (Settings.DanmakuShowPmcName != null && Settings.DanmakuShowPmcName.Value) roleTagDmText = string.IsNullOrEmpty(nameForShow) ? baseRoleDm : nameForShow;
            if (Settings.World3DShowPmcName != null && Settings.World3DShowPmcName.Value) roleTagW3dText = string.IsNullOrEmpty(nameForShow) ? baseRoleW3d : nameForShow;
        }
        if (isSCAV)
        {
            if (Settings.SubtitleShowScavName != null && Settings.SubtitleShowScavName.Value) roleTagSubText = string.IsNullOrEmpty(nameForShow) ? baseRoleSub : nameForShow;
            if (Settings.DanmakuShowScavName != null && Settings.DanmakuShowScavName.Value) roleTagDmText = string.IsNullOrEmpty(nameForShow) ? baseRoleDm : nameForShow;
            if (Settings.World3DShowScavName != null && Settings.World3DShowScavName.Value) roleTagW3dText = string.IsNullOrEmpty(nameForShow) ? baseRoleW3d : nameForShow;
        }

        // 4) 再各自上色 + 拼入正文
        string roleColoredSub = Settings.WrapRoleTag(roleTagSubText + "：", kind, Settings.Channel.Subtitle);
        string roleColoredDm = Settings.WrapRoleTag(roleTagDmText + "：", kind, Settings.Channel.Danmaku);
        string roleColoredW3d = Settings.WrapRoleTag(roleTagW3dText + "：", kind, Settings.Channel.World3D);

        bool showRoleSub = Settings.SubtitleShowRoleTag == null ? true : Settings.SubtitleShowRoleTag.Value;
        bool showRoleDm = Settings.DanmakuShowRoleTag == null ? true : Settings.DanmakuShowRoleTag.Value;
        bool showRoleW3d = Settings.World3DShowRoleTag == null ? true : Settings.World3DShowRoleTag.Value;

        string fullSub = string.IsNullOrEmpty(textSub) ? null : (showRoleSub ? (roleColoredSub + textSub) : textSub);
        string fullDm = string.IsNullOrEmpty(textDm) ? null : (showRoleDm ? (roleColoredDm + textDm) : textDm);
        string fullW3d = string.IsNullOrEmpty(textW3d) ? null : (showRoleW3d ? (roleColoredW3d + textW3d) : textW3d);

        // 仅对“非本地玩家/AI”应用距离过滤
        float? distMeters = (!isLocalSpeaker) ? ComputeDistanceMeters(speakerPlayer, mainPlayer) : (float?)null;
        bool allowSubtitle = !string.IsNullOrEmpty(textSub);
        bool allowDanmaku = !string.IsNullOrEmpty(textDm);
        bool allowWorld3d = !string.IsNullOrEmpty(textW3d);
        if (Subtitle.Config.Settings.EnableWorld3D != null && !Subtitle.Config.Settings.EnableWorld3D.Value)
            allowWorld3d = false;
        if (isLocalSpeaker && Subtitle.Config.Settings.World3DShowSelf != null && !Subtitle.Config.Settings.World3DShowSelf.Value)
            allowWorld3d = false;

        // —— 距离过滤（保持不变）：只在“非本地且拿到距离”时调整 allowXxx —— 
        if (!isLocalSpeaker && distMeters.HasValue)
        {
            float d = distMeters.Value;

            if (Subtitle.Config.Settings.SubtitleMaxDistanceMeters != null)
            {
                float limitSub = Subtitle.Config.Settings.SubtitleMaxDistanceMeters.Value;
                if (d > limitSub) allowSubtitle = false;
            }
            if (Subtitle.Config.Settings.DanmakuMaxDistanceMeters != null)
            {
                float limitDm = Subtitle.Config.Settings.DanmakuMaxDistanceMeters.Value;
                if (d > limitDm) allowDanmaku = false;
            }
            if (Subtitle.Config.Settings.World3DMaxDistanceMeters != null)
            {
                float limitW3d = Subtitle.Config.Settings.World3DMaxDistanceMeters.Value;
                if (d > limitW3d) allowWorld3d = false;
            }

            if ((!allowSubtitle || !allowDanmaku))
            {
                try
                {
                    s_Log.LogInfo("[SubtitleDbg] filtered by distance: d=" + Mathf.RoundToInt(d) + "m"
                        + " sub<=" + Subtitle.Config.Settings.SubtitleMaxDistanceMeters.Value
                        + " dm<=" + Subtitle.Config.Settings.DanmakuMaxDistanceMeters.Value);
                }
                catch { }
            }
        }

        // —— 丧尸（不含 infectedtagilla）过滤 & 冷却节流 ——  
        // 注意：这段不依赖距离信息，应当“总是执行”，只修改 allowSubtitle/allowDanmaku
        var aiLC = (aiTypeRaw ?? "").ToLowerInvariant();
        bool isZombieNonTagilla = (kind == Subtitle.Config.Settings.RoleKind.Zombie) && (aiLC.IndexOf("tagilla") < 0);

        // ↓↓↓ 后面沿用你原先的开关/冷却逻辑（变量名照旧）↓↓↓
        float nowUnscaled = Time.unscaledTime;

        if (isZombieNonTagilla)
        {
            if (Settings.SubtitleZombieEnabled != null && !Settings.SubtitleZombieEnabled.Value)
            {
                allowSubtitle = false;
                allowWorld3d = false;
            }

            int subCd = (Settings.SubtitleZombieCooldownSec != null) ? Settings.SubtitleZombieCooldownSec.Value : 0;
            if (subCd > 0 && (nowUnscaled - s_LastZombieSubtitleTime) < subCd)
            {
                allowSubtitle = false;
                allowWorld3d = false;
            }

            if (Settings.DanmakuZombieEnabled != null && !Settings.DanmakuZombieEnabled.Value)
                allowDanmaku = false;

            int dmCd = (Settings.DanmakuZombieCooldownSec != null) ? Settings.DanmakuZombieCooldownSec.Value : 0;
            if (dmCd > 0 && (nowUnscaled - s_LastZombieDanmakuTime) < dmCd)
                allowDanmaku = false;

            if (Settings.World3DZombieEnabled != null && !Settings.World3DZombieEnabled.Value)
                allowWorld3d = false;

            int w3dCd = (Settings.World3DZombieCooldownSec != null) ? Settings.World3DZombieCooldownSec.Value : 0;
            if (w3dCd > 0 && (nowUnscaled - s_LastZombieWorld3DTime) < w3dCd)
                allowWorld3d = false;
        }

        // —— 距离文本后缀（仅非本地、且对应通道仍允许时附加）——
        string distanceSuffix = null;
        if (!isLocalSpeaker && distMeters.HasValue)
        {
            int m = Mathf.RoundToInt(distMeters.Value);
            if (m != 0) distanceSuffix = " <b>·</b>" + m + "m";
        }
        if (!string.IsNullOrEmpty(distanceSuffix))
        {
            if (Settings.SubtitleShowDistance != null && Settings.SubtitleShowDistance.Value && allowSubtitle)
                fullSub += distanceSuffix;

            if (Settings.DanmakuShowDistance != null && Settings.DanmakuShowDistance.Value && allowDanmaku)
                fullDm += distanceSuffix;

            if (Settings.World3DShowDistance != null && Settings.World3DShowDistance.Value && allowWorld3d)
                fullW3d += distanceSuffix;
        }

        // —— 最终投递（全函数唯一一次）+ 成功后更新时间戳 —— 
        if (SuppressDuplicate(__instance, netIdStr, trigger)) return;

        bool pushedSub = false, pushedDm = false;
        try
        {
            // 计算本次建议显示时长：用已选 Clip 的长度 + 0.5s 缓冲
            float dur = 0.8f;
            try { if (clip != null) dur = Mathf.Max(0f, clip.Length) + 0.5f; } catch { }

            if (Subtitle.Config.Settings.EnableSubtitle.Value && allowSubtitle)
            {
                SubtitleSystem.SubtitleManager.Instance.AddSubtitle(fullSub, colorSub, dur);
                pushedSub = true;
                if (kind == Settings.RoleKind.Zombie) s_LastZombieSubtitleTime = Time.unscaledTime;
            }

            if (Subtitle.Config.Settings.EnableDanmaku.Value && allowDanmaku)
            {
                if (Subtitle.Config.Settings.DanmakuDebugVerbose.Value)
                    s_Log.LogInfo("[Danmaku] -> call AddDanmaku | text=\"" + fullDm + "\" color=" + colorDm);
                SubtitleSystem.SubtitleManager.Instance.AddDanmaku(fullDm, colorDm);
                pushedDm = true;
                if (kind == Settings.RoleKind.Zombie) s_LastZombieDanmakuTime = Time.unscaledTime;
            }
            if (allowWorld3d && speakerPlayer != null)
            {
                SubtitleSystem.SubtitleManager.Instance.AddWorld3D(speakerPlayer, fullW3d, colorW3d, dur);
                if (kind == Settings.RoleKind.Zombie) s_LastZombieWorld3DTime = Time.unscaledTime;
            }
        }
        catch (System.Exception e)
        {
            s_Log.LogWarning("[Subtitle] AddSubtitle/Danmaku failed: " + e);
        }
    }


    // ========== ★ 新增：通过 PhraseSpeakerClass.Id 直连 GameWorld 的 ProfileId→Player 映射 ==========
    private static IPlayer TryResolveByProfileMap(PhraseSpeakerClass speaker)
    {
        try
        {
            if (speaker == null) return null;

            // 先拿 speaker 的 Id（字段或属性都试）
            int spkId = 0;
            var tr = Traverse.Create(speaker);
            object idObj = tr.Field("Id")?.GetValue() ?? tr.Property("Id")?.GetValue();
            if (idObj is int i) spkId = i;
            else if (idObj is string s && int.TryParse(s, out var iv)) spkId = iv;
            if (spkId == 0) return null;

            // 再从 GameWorld 拿 allAlivePlayersByID 或 AllAlivePlayersByID 映射
            var gw = Singleton<GameWorld>.Instance;
            if (gw == null) return null;

            var tgw = Traverse.Create(gw);

            // 私有字段或公开属性两种命名都兼容
            object dictObj =
                tgw.Field("allAlivePlayersByID")?.GetValue()
             ?? tgw.Property("AllAlivePlayersByID")?.GetValue()
             ?? tgw.Property("allAlivePlayersByID")?.GetValue();

            if (dictObj is System.Collections.IDictionary dict)
            {
                // 字典 key 可能是 int 或 string，这里都处理
                if (dict.Contains(spkId)) return dict[spkId] as IPlayer;
                var keyStr = spkId.ToString();
                if (dict.Contains(keyStr)) return dict[keyStr] as IPlayer;

                // 有些实现用 KeyValuePair<,> 迭代时更稳
                foreach (System.Collections.DictionaryEntry kv in dict)
                {
                    if (kv.Key == null || kv.Value == null) continue;
                    if (kv.Key is int ki && ki == spkId) return kv.Value as IPlayer;
                    if (kv.Key is string ks && ks == keyStr) return kv.Value as IPlayer;
                }
            }

            // 若没有字典，降级：遍历 AllAlivePlayersList 比 ProfileId
            object listObj =
                tgw.Property("AllAlivePlayersList")?.GetValue()
             ?? tgw.Property("AllPlayersList")?.GetValue();

            if (listObj is System.Collections.IEnumerable en)
            {
                foreach (var o in en)
                {
                    if (o is IPlayer ip)
                    {
                        // ProfileId 可能是 int 或可转的字符串
                        var pidObj = Traverse.Create(ip).Property("ProfileId")?.GetValue()
                                  ?? Traverse.Create(ip.Profile).Property("Id")?.GetValue();
                        if (pidObj is int pi && pi == spkId) return ip;
                        if (pidObj is string ps && int.TryParse(ps, out var piv) && piv == spkId) return ip;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    // ========== 你原有的辅助函数，保持不变（仅在上面调整了调用顺序） ==========

    // 更强的说话者解析：优先从 PhraseSpeakerClass 直接拿 Owner/IPlayer
    private static IPlayer TryResolveSpeakerOwnerStrong(PhraseSpeakerClass speaker)
    {
        if (speaker == null) return null;

        try
        {
            var tv = Traverse.Create(speaker);

            // 常见字段/属性名：Owner / _owner / Player / IPlayer
            object owner =
                tv.Property("Owner")?.GetValue() ??
                tv.Field("_owner")?.GetValue() ??
                tv.Property("Player")?.GetValue() ??
                tv.Property("IPlayer")?.GetValue();

            // 直接是 IPlayer（Player 或 ObservedPlayer）
            if (owner is IPlayer ip) return ip;

            // Owner 可能是 BotOwner -> 取其 Player
            if (owner != null)
            {
                var maybePlayer = Traverse.Create(owner).Property("Player")?.GetValue();
                if (maybePlayer is IPlayer ip2) return ip2;
            }
        }
        catch { }

        // 兜底：枚举 GameWorld 里注册的玩家
        return TryResolveSpeakerOwnerFallback(speaker);
    }

    // 兜底解析：枚举玩家列表匹配 Speaker 或 Speaker.Id（全部用反射，避免 IDissonance 依赖）
    private static IPlayer TryResolveSpeakerOwnerFallback(PhraseSpeakerClass speaker)
    {
        try
        {
            var gw = Comfort.Common.Singleton<GameWorld>.Instance;
            if (gw == null) return null;

            foreach (var p in GetAllPlayersCompat(gw)) // IPlayer
            {
                if (p == null) continue;

                var spkObj = GetPlayerSpeakerObject(p); // object（反射）
                if (spkObj != null && object.ReferenceEquals(spkObj, speaker))
                    return p;
            }

            int spkId = SafeGetSpeakerIdFromObj(speaker);
            if (spkId != 0)
            {
                foreach (var p in GetAllPlayersCompat(gw)) // IPlayer
                {
                    var psObj = GetPlayerSpeakerObject(p);
                    int pid = SafeGetSpeakerIdFromObj(psObj);
                    if (pid != 0 && pid == spkId) return p;
                }
            }
        }
        catch { }
        return null;
    }

    private static object GetPlayerSpeakerObject(IPlayer p)
    {
        if (p == null) return null;
        try
        {
            // 不写 p.Speaker（那会引入 IDissonancePlayer），只反射取值
            return HarmonyLib.Traverse.Create(p).Property("Speaker")?.GetValue();
        }
        catch { return null; }
    }

    private static int SafeGetSpeakerIdFromObj(object spkObj)
    {
        if (spkObj == null) return 0;
        try
        {
            var tr = HarmonyLib.Traverse.Create(spkObj);
            object idObj =
                (tr.Property("Id") != null ? tr.Property("Id").GetValue() : null) ??
                (tr.Field("Id") != null ? tr.Field("Id").GetValue() : null) ??
                (tr.Field("_id") != null ? tr.Field("_id").GetValue() : null);

            if (idObj is int i) return i;

            int iv;
            if (idObj != null && int.TryParse(idObj.ToString(), out iv)) return iv;
        }
        catch { }
        return 0;
    }

    private static bool SuppressDuplicate(object speakerObj, string netIdStr, EPhraseTrigger trigger)
    {
        int spkId = SafeGetSpeakerIdFromObj(speakerObj);
        if (spkId == 0) return false;

        string keyNet = !string.IsNullOrEmpty(netIdStr) ? ("N:" + netIdStr) : null;
        string keyTrig = "T:" + (int)trigger;
        float now = Time.unscaledTime;

        float win = GetDupWindowSec();
        if (win <= 0f)
            return false; // 允许通过（关闭去重）

        float last;

        // —— 调试：打印当前检查的 Key 组合 —— //
        if (Settings.DanmakuDebugVerbose != null && Settings.DanmakuDebugVerbose.Value)
        {
            try
            {
                s_Log.LogInfo("[DeDup] check spk=" + spkId
                    + " keyNet=" + (keyNet ?? "-")
                    + " keyTrig=" + keyTrig
                    + " win=" + win.ToString("0.00") + "s");
            }
            catch { }
        }

        // 命中任一键，都视为重复
        if (keyNet != null && s_RecentVoiceOnce.TryGetValue(spkId + "|" + keyNet, out last) && now - last < win)
        {
            if (Settings.DanmakuDebugVerbose != null && Settings.DanmakuDebugVerbose.Value)
            {
                try { s_Log.LogInfo("[DeDup] HIT " + (spkId + "|" + keyNet) + " dt=" + (now - last).ToString("0.000") + " <= " + win.ToString("0.000")); } catch { }
            }
            return true;
        }
        if (s_RecentVoiceOnce.TryGetValue(spkId + "|" + keyTrig, out last) && now - last < win)
        {
            if (Settings.DanmakuDebugVerbose != null && Settings.DanmakuDebugVerbose.Value)
            {
                try { s_Log.LogInfo("[DeDup] HIT " + (spkId + "|" + keyTrig) + " dt=" + (now - last).ToString("0.000") + " <= " + win.ToString("0.000")); } catch { }
            }
            return true;
        }

        // 首次记录
        if (keyNet != null) s_RecentVoiceOnce[spkId + "|" + keyNet] = now;
        s_RecentVoiceOnce[spkId + "|" + keyTrig] = now;

        // 轻量清理
        if (s_RecentVoiceOnce.Count > 128)
        {
            var toRemove = new List<string>();
            foreach (var kv in s_RecentVoiceOnce)
                if (now - kv.Value > 2f) toRemove.Add(kv.Key);
            for (int i = 0; i < toRemove.Count; i++) s_RecentVoiceOnce.Remove(toRemove[i]);
        }
        return false;
    }



    // —— 兼容 IPlayer 的角色标签 —— 
    private static string GetRoleTagFromPlayer(IPlayer p, Settings.Channel ch, PhraseSpeakerClass spk = null)
    {
        if (p == null) return "未知";

        if (p.IsYourPlayer)
        {
            var opt = GetSelfPronounOption(ch, false);

            if (opt == SelfPronounOption.略称) return "你";
            if (opt == SelfPronounOption.玩家名)
            {
                var name = GetDisplayName(p);
                return string.IsNullOrEmpty(name) ? "你" : name;
            }
            if (opt == SelfPronounOption.声线名)
            {
                string key = ResolveVoiceKeySmart(p, spk);
                var label = MapVoiceKeyLabel(key);
                return string.IsNullOrEmpty(label) ? "你" : label;
            }
        }

        // 友军玩家（队友）
        if (!p.IsYourPlayer && !p.IsAI)
        {
            bool isFriend = false;
            try { isFriend = p.IsFriendlyToMain(); } catch { }

            if (isFriend)
            {
                var optTm = GetSelfPronounOption(ch, true);

                if (optTm == SelfPronounOption.略称)
                {
                    // 在队友语境下，“你”按需求展示为“队友”
                    return "队友";
                }
                if (optTm == SelfPronounOption.玩家名)
                {
                    var name = GetDisplayName(p);
                    return string.IsNullOrEmpty(name) ? "队友" : name;
                }
                if (optTm == SelfPronounOption.声线名)
                {
                    string key = ResolveVoiceKeySmart(p, spk);
                    var label = MapVoiceKeyLabel(key);
                    return string.IsNullOrEmpty(label) ? "队友" : label;
                }
            }
        }

        if (p.IsAI)
        {
            var aiType = GetAITypeOrPlayer(p);
            var label = MapAITypeLabel(aiType);
            return string.IsNullOrEmpty(label) ? "AI" : label;
        }

        switch (p.Side)
        {
            case EPlayerSide.Bear: return "BEAR";
            case EPlayerSide.Usec: return "USEC";
            case EPlayerSide.Savage: return "Scav";
            default: return "未知";
        }
    }

    private static SelfPronounOption GetSelfPronounOption(Settings.Channel ch, bool teammate)
    {
        switch (ch)
        {
            case Settings.Channel.Subtitle:
                if (teammate)
                    return Subtitle.Config.Settings.SubtitleTeammateSelfPronoun != null
                        ? Subtitle.Config.Settings.SubtitleTeammateSelfPronoun.Value
                        : SelfPronounOption.玩家名;
                return Subtitle.Config.Settings.SubtitlePlayerSelfPronoun != null
                    ? Subtitle.Config.Settings.SubtitlePlayerSelfPronoun.Value
                    : SelfPronounOption.玩家名;
            case Settings.Channel.Danmaku:
                if (teammate)
                    return Subtitle.Config.Settings.DanmakuTeammateSelfPronoun != null
                        ? Subtitle.Config.Settings.DanmakuTeammateSelfPronoun.Value
                        : SelfPronounOption.玩家名;
                return Subtitle.Config.Settings.DanmakuPlayerSelfPronoun != null
                    ? Subtitle.Config.Settings.DanmakuPlayerSelfPronoun.Value
                    : SelfPronounOption.玩家名;
            case Settings.Channel.World3D:
                if (teammate)
                    return Subtitle.Config.Settings.World3DTeammateSelfPronoun != null
                        ? Subtitle.Config.Settings.World3DTeammateSelfPronoun.Value
                        : SelfPronounOption.玩家名;
                return Subtitle.Config.Settings.World3DPlayerSelfPronoun != null
                    ? Subtitle.Config.Settings.World3DPlayerSelfPronoun.Value
                    : SelfPronounOption.玩家名;
            default:
                return SelfPronounOption.玩家名;
        }
    }

    private static string ResolveVoiceKeySmart(IPlayer ip, PhraseSpeakerClass speaker)
    {
        bool isLocal = (ip != null && ip.IsYourPlayer);

        // 封装：从 Profile 里各种可能的路径尝试拿 Voice
        Func<IPlayer, string> tryFromProfile = (player) =>
        {
            if (player == null) return null;
            try
            {
                var prof = player.Profile;
                if (prof == null) return null;

                var tp = HarmonyLib.Traverse.Create(prof);

                // Info.Voice
                var info = tp.Property("Info")?.GetValue() ?? tp.Field("Info")?.GetValue();
                if (info != null)
                {
                    var ti = HarmonyLib.Traverse.Create(info);

                    var v1 = ti.Property("Voice")?.GetValue() ?? ti.Field("Voice")?.GetValue();
                    if (v1 != null) return v1.ToString();

                    // Info.Settings.Voice（部分版本）
                    var settings = ti.Property("Settings")?.GetValue();
                    if (settings != null)
                    {
                        var vs = HarmonyLib.Traverse.Create(settings).Property("Voice")?.GetValue();
                        if (vs != null) return vs.ToString();
                    }
                }

                // Appearance.Voice（少数版本）
                var app = tp.Property("Appearance")?.GetValue();
                if (app != null)
                {
                    var va = HarmonyLib.Traverse.Create(app).Property("Voice")?.GetValue();
                    if (va != null) return va.ToString();
                }
            }
            catch { }
            return null;
        };

        // 封装：从 PhraseSpeakerClass 里拿（不同版本字段名可能不同）
        Func<PhraseSpeakerClass, string> tryFromSpeaker = (spk) =>
        {
            try
            {
                var tr = HarmonyLib.Traverse.Create(spk);
                object pv =
                    tr.Field("PlayerVoice")?.GetValue()
                 ?? tr.Field("_playerVoice")?.GetValue()
                 ?? tr.Property("PlayerVoice")?.GetValue()
                 ?? tr.Property("Voice")?.GetValue();
                return pv?.ToString();
            }
            catch { return null; }
        };

        string key = null;

        if (isLocal)
        {
            // 本地玩家：优先个人档案里的 Voice
            key = tryFromProfile(ip);
            if (string.IsNullOrEmpty(key))
                key = tryFromSpeaker(speaker);
        }
        else
        {
            // AI/观察对象：先试档案多路径，再退回说话器
            key = tryFromProfile(ip);
            if (string.IsNullOrEmpty(key))
                key = tryFromSpeaker(speaker);
        }

        if (string.IsNullOrEmpty(key))
            key = "_default";
        return key;
    }

    private static IEnumerable<IPlayer> GetAllPlayersCompat(GameWorld gw)
    {
        if (gw == null) yield break;

        var t = HarmonyLib.Traverse.Create(gw);

        // AllPlayers / AllPlayersList / AllAlivePlayersList
        object listObj =
            t.Property("AllPlayers")?.GetValue() ??
            t.Property("AllPlayersList")?.GetValue() ??
            t.Property("AllAlivePlayersList")?.GetValue();

        if (listObj is System.Collections.IEnumerable en1)
        {
            foreach (var o in en1)
            {
                if (o is IPlayer ip1) yield return ip1;
                else if (o != null && o.GetType().Name.Contains("Player"))
                {
                    var ip2 = o as IPlayer;
                    if (ip2 != null) yield return ip2;
                }
            }
            yield break;
        }

        // RegisteredPlayers: Dictionary<*, Player>（用反射拿值）
        var reg = t.Property("RegisteredPlayers")?.GetValue() as System.Collections.IDictionary;
        if (reg != null)
        {
            foreach (System.Collections.DictionaryEntry kv in reg)
            {
                if (kv.Value is IPlayer ip) yield return ip;
            }
            // 注意不要 return，这里还有 MainPlayer 和 Bots 可尝试
        }

        // ✅ MainPlayer：不要写 gw.MainPlayer（会引入 Player/IDissonance 依赖）
        var mainObj = t.Property("MainPlayer")?.GetValue();
        if (mainObj is IPlayer ipMain) yield return ipMain;

        // 机器人集合：_allBots / Bots / BotOwners
        var bots = t.Field("_allBots")?.GetValue() as System.Collections.IEnumerable
                ?? t.Field("Bots")?.GetValue() as System.Collections.IEnumerable
                ?? t.Property("Bots")?.GetValue() as System.Collections.IEnumerable
                ?? t.Property("BotOwners")?.GetValue() as System.Collections.IEnumerable;

        if (bots != null)
        {
            foreach (var b in bots)
            {
                // BotOwner.Player（反射拿）
                var bp = HarmonyLib.Traverse.Create(b).Property("Player")?.GetValue();
                if (bp is IPlayer ip3) yield return ip3;
            }
        }
    }

    private static float? ComputeDistanceMeters(IPlayer speaker, IPlayer main)
    {
        if (speaker == null || main == null) return null;
        try
        {
            object spObj = Traverse.Create(speaker).Property("Position")?.GetValue();
            object mpObj = Traverse.Create(main).Property("Position")?.GetValue();

            Vector3 sp, mp;
            if (spObj is Vector3) sp = (Vector3)spObj;
            else
            {
                var tr = Traverse.Create(speaker).Property("Transform")?.GetValue();
                if (tr == null) return null;
                sp = (Vector3)Traverse.Create(tr).Property("position").GetValue();
            }

            if (mpObj is Vector3) mp = (Vector3)mpObj;
            else
            {
                var trm = Traverse.Create(main).Property("Transform")?.GetValue();
                if (trm == null) return null;
                mp = (Vector3)Traverse.Create(trm).Property("position").GetValue();
            }

            float d = Vector3.Distance(sp, mp);
            if (float.IsNaN(d) || float.IsInfinity(d)) return null;
            return d; // 米
        }
        catch { return null; }
    }

    // ====== Probe #2: 游戏本体播放口：PhraseSpeakerClass.PlayDirect(trigger, index) ======
    [HarmonyPatch(typeof(PhraseSpeakerClass), "PlayDirect")]
    internal static class Probe_PlayDirect
    {
        static void Postfix(PhraseSpeakerClass __instance, EPhraseTrigger trigger, int index)
        {
            try
            {
                // 解析说话者（沿用你文件里已有的强/弱两套解析）
                IPlayer ip = TryResolveSpeakerOwnerStrong(__instance);
                if (ip == null) ip = TryResolveSpeakerOwnerFallback(__instance);
                if (ip == null) ip = TryResolveByProfileMap(__instance);

                string name = ip != null ? GetDisplayName(ip) : "Unknown";
                bool isSelf = (ip != null && ip.IsYourPlayer);
                bool friendly = (ip != null && !isSelf && ip.IsFriendlyToMain());

                // 有些版本 PlayDirect 里 Clip 可能还没赋值，这里只做轻量日志
                s_Log.LogInfo("[Probe-PlayDirect] trigger=" + trigger
                    + " index=" + index
                    + " name=" + name
                    + " self=" + (isSelf ? "1" : "0")
                    + " friendly=" + (friendly ? "1" : "0"));
            }
            catch (System.Exception e)
            {
                s_Log.LogWarning("[Probe-PlayDirect] failed: " + e);
            }
        }
    }

    // ========== 正式补丁：统一捕捉“远端复刻语音”的播放入口 ==========
    // Fika 在对端复刻时会调用游戏本体的 PhraseSpeakerClass.PlayDirect(trigger, index)
    // 单机/本地玩家仍由你现有的 Play(...) 后缀负责；这里仅处理“非本地玩家”，避免重复
    [HarmonyPatch(typeof(PhraseSpeakerClass), "PlayDirect")]
    internal static class Subtitle_PlayDirectPatch
    {
        static void Postfix(PhraseSpeakerClass __instance, EPhraseTrigger trigger, int index)
        {
            try
            {
                // 1) 反解说话者 IPlayer（尽量强；不行再兜底）
                IPlayer speaker = TryResolveSpeakerOwnerStrong(__instance);
                if (speaker == null) speaker = TryResolveSpeakerOwnerFallback(__instance);
                if (speaker == null) speaker = TryResolveByTrackRoot(__instance);
                if (speaker == null) return;

                // 2) 本地玩家仍由 Play(...) 处理，这里只管“他人/AI”
                if (speaker.IsYourPlayer) return;

                // 3) 三键：voiceKey + trigger + netId(index)
                string netIdStr = index.ToString();
                // —— 去重（防护 PlayDirect 自身的重复调用；本地玩家已被提前 return）——
                if (SuppressDuplicate(__instance, netIdStr, trigger)) return;
                string voiceKey = ResolveVoiceKeySmart(speaker, __instance);
                string textSub = PhraseSubtitle.GetSubtitleForChannel("Subtitle", voiceKey, trigger.ToString(), netIdStr);
                string textDm = PhraseSubtitle.GetSubtitleForChannel("Danmaku", voiceKey, trigger.ToString(), netIdStr);
                string textW3d = PhraseSubtitle.GetSubtitleForChannel("World3D", voiceKey, trigger.ToString(), netIdStr);
                if (string.IsNullOrEmpty(textSub) && string.IsNullOrEmpty(textDm) && string.IsNullOrEmpty(textW3d)) return;

                // 4) 友军判定（独立于动态地图）
                bool isFriendly = false;
                try { isFriendly = speaker.IsFriendlyToMain(); } catch { }

                // 5) roletag（按频道区分代称）
                string baseRoleSub = GetRoleTagFromPlayer(speaker, Settings.Channel.Subtitle, __instance);
                string baseRoleDm = GetRoleTagFromPlayer(speaker, Settings.Channel.Danmaku, __instance);
                string baseRoleW3d = GetRoleTagFromPlayer(speaker, Settings.Channel.World3D, __instance);

                // 6) 调试日志（可保留，便于回归）
                try
                {
                    string nameForLog = GetDisplayName(speaker);
                    s_Log.LogInfo(
                        "[SubtitleNet] voiceKey=" + voiceKey +
                        " trigger=" + trigger +
                        " netId=" + netIdStr +
                        " name=" + nameForLog +
                        " friendly=" + (isFriendly ? "1" : "0"));
                }
                catch { }

                // 7) 颜色与 roletag 显示开关（PlayDirect 里说话者就是 speaker）
                bool isSelf = speaker.IsYourPlayer;                      // 上面已 return 掉 self，这里本应为 false
                bool isFriend = (!isSelf) && speaker.IsFriendlyToMain();
                string aiTypeRaw = GetAITypeOrPlayer(speaker);

                // 统一入口：仅用 Settings 的分类器判 AI
                var kind = Subtitle.Config.Settings.GuessRoleKindFromAiType(aiTypeRaw);

                // 玩家/队友覆盖（PlayDirect 下 isSelf 通常为 false；仍保留覆盖逻辑更稳）
                if (isSelf) kind = Subtitle.Config.Settings.RoleKind.Player;
                else if (isFriend) kind = Subtitle.Config.Settings.RoleKind.Teammate;

                // 整行颜色
                Color colorSub = Settings.GetTextColor(kind, Settings.Channel.Subtitle);
                Color colorDm = Settings.GetTextColor(kind, Settings.Channel.Danmaku);
                Color colorW3d = Settings.GetTextColor(kind, Settings.Channel.World3D);

                // 6.1) 判定是不是 PMC / Scav
                bool isPMC = false, isSCAV = false;
                try
                {
                    isPMC = (kind == Settings.RoleKind.PmcBear || kind == Settings.RoleKind.PmcUsec);
                    isSCAV = (kind == Settings.RoleKind.Scav);

                    if (speaker != null && !speaker.IsAI)
                    {
                        if (speaker.Side == EFT.EPlayerSide.Bear || speaker.Side == EFT.EPlayerSide.Usec) isPMC = true;
                        if (speaker.Side == EFT.EPlayerSide.Savage) isSCAV = true;
                    }
                }
                catch { }

                // 6.2) 依据频道选项决定“是否用名字替代 roletag”
                string nameForShow = GetDisplayName(speaker);
                string roleTagSubText = baseRoleSub;
                string roleTagDmText = baseRoleDm;
                string roleTagW3dText = baseRoleW3d;

                if (isPMC)
                {
                    if (Settings.SubtitleShowPmcName != null && Settings.SubtitleShowPmcName.Value) roleTagSubText = string.IsNullOrEmpty(nameForShow) ? baseRoleSub : nameForShow;
                    if (Settings.DanmakuShowPmcName != null && Settings.DanmakuShowPmcName.Value) roleTagDmText = string.IsNullOrEmpty(nameForShow) ? baseRoleDm : nameForShow;
                    if (Settings.World3DShowPmcName != null && Settings.World3DShowPmcName.Value) roleTagW3dText = string.IsNullOrEmpty(nameForShow) ? baseRoleW3d : nameForShow;
                }
                if (isSCAV)
                {
                    if (Settings.SubtitleShowScavName != null && Settings.SubtitleShowScavName.Value) roleTagSubText = string.IsNullOrEmpty(nameForShow) ? baseRoleSub : nameForShow;
                    if (Settings.DanmakuShowScavName != null && Settings.DanmakuShowScavName.Value) roleTagDmText = string.IsNullOrEmpty(nameForShow) ? baseRoleDm : nameForShow;
                    if (Settings.World3DShowScavName != null && Settings.World3DShowScavName.Value) roleTagW3dText = string.IsNullOrEmpty(nameForShow) ? baseRoleW3d : nameForShow;
                }

                // roletag 着色（分别按频道）
                string roleColoredSub = Settings.WrapRoleTag(roleTagSubText + "：", kind, Settings.Channel.Subtitle);
                string roleColoredDm = Settings.WrapRoleTag(roleTagDmText + "：", kind, Settings.Channel.Danmaku);
                string roleColoredW3d = Settings.WrapRoleTag(roleTagW3dText + "：", kind, Settings.Channel.World3D);

                bool showRoleSub = Settings.SubtitleShowRoleTag == null ? true : Settings.SubtitleShowRoleTag.Value;
                bool showRoleDm = Settings.DanmakuShowRoleTag == null ? true : Settings.DanmakuShowRoleTag.Value;
                bool showRoleW3d = Settings.World3DShowRoleTag == null ? true : Settings.World3DShowRoleTag.Value;

                // 初始文本
                string fullSub = string.IsNullOrEmpty(textSub) ? null : (showRoleSub ? (roleColoredSub + textSub) : textSub);
                string fullDm = string.IsNullOrEmpty(textDm) ? null : (showRoleDm ? (roleColoredDm + textDm) : textDm);
                string fullW3d = string.IsNullOrEmpty(textW3d) ? null : (showRoleW3d ? (roleColoredW3d + textW3d) : textW3d);


                // 8) 距离过滤（仅非本地）
                var gw = Comfort.Common.Singleton<GameWorld>.Instance;
                IPlayer mainPlayer = gw != null ? gw.MainPlayer as IPlayer : null;
                float? distMeters = ComputeDistanceMeters(speaker, mainPlayer);

                bool allowSubtitle = !string.IsNullOrEmpty(textSub);
                bool allowDanmaku = !string.IsNullOrEmpty(textDm);
                bool allowWorld3d = !string.IsNullOrEmpty(textW3d);
                if (Subtitle.Config.Settings.EnableWorld3D != null && !Subtitle.Config.Settings.EnableWorld3D.Value)
                    allowWorld3d = false;
                if (distMeters.HasValue)
                {
                    float d = distMeters.Value;
                    if (Subtitle.Config.Settings.SubtitleMaxDistanceMeters != null)
                    {
                        float limitSub = Subtitle.Config.Settings.SubtitleMaxDistanceMeters.Value;
                        if (d > limitSub) allowSubtitle = false;
                    }
                    if (Subtitle.Config.Settings.DanmakuMaxDistanceMeters != null)
                    {
                        float limitDm = Subtitle.Config.Settings.DanmakuMaxDistanceMeters.Value;
                        if (d > limitDm) allowDanmaku = false;
                    }
                    if (Subtitle.Config.Settings.World3DMaxDistanceMeters != null)
                    {
                        float limitW3d = Subtitle.Config.Settings.World3DMaxDistanceMeters.Value;
                        if (d > limitW3d) allowWorld3d = false;
                    }
                }

                // 9) 距离后缀
                if (distMeters.HasValue)
                {
                    int m = UnityEngine.Mathf.RoundToInt(distMeters.Value);
                    if (m != 0)
                    {
                        string suffix = " <b>·</b>" + m + "m";
                        if (Settings.SubtitleShowDistance != null && Settings.SubtitleShowDistance.Value && allowSubtitle)
                            fullSub += suffix;
                        if (Settings.DanmakuShowDistance != null && Settings.DanmakuShowDistance.Value && allowDanmaku)
                            fullDm += suffix;
                        if (Settings.World3DShowDistance != null && Settings.World3DShowDistance.Value && allowWorld3d)
                            fullW3d += suffix;
                    }
                }

                // 10) 时长：优先用已选 Clip.Length（PlayDirect 后通常已就绪），否则给个轻兜底
                float dur = 0.8f;
                try { if (__instance != null && __instance.Clip != null) dur = UnityEngine.Mathf.Max(0f, __instance.Clip.Length) + 0.5f; } catch { }

                // 11) 输出（与本地路径保持一致）
                try
                {
                    if (Subtitle.Config.Settings.EnableSubtitle.Value && allowSubtitle)
                    {
                        SubtitleSystem.SubtitleManager.Instance.AddSubtitle(fullSub, colorSub, dur);
                        if (kind == Settings.RoleKind.Zombie) s_LastZombieSubtitleTime = Time.unscaledTime;
                    }
                    if (Subtitle.Config.Settings.EnableDanmaku.Value && allowDanmaku)
                    {
                        if (Subtitle.Config.Settings.DanmakuDebugVerbose.Value)
                            s_Log.LogInfo("[Danmaku] -> AddDanmaku | text=\"" + fullDm + "\"");
                        SubtitleSystem.SubtitleManager.Instance.AddDanmaku(fullDm, colorDm);
                        if (kind == Settings.RoleKind.Zombie) s_LastZombieDanmakuTime = Time.unscaledTime;
                    }
                    if (allowWorld3d)
                    {
                        SubtitleSystem.SubtitleManager.Instance.AddWorld3D(speaker, fullW3d, colorW3d, dur);
                        if (kind == Settings.RoleKind.Zombie) s_LastZombieWorld3DTime = Time.unscaledTime;
                    }
                }
                catch (System.Exception e)
                {
                    s_Log.LogWarning("[Subtitle] PlayDirect output failed: " + e);
                }
            }
            catch (System.Exception e)
            {
                s_Log.LogWarning("[Subtitle] PlayDirectPatch failed: " + e);
            }
        }

        // —— 说话者解析：三段兜底 —— //

        // A. 强解析：从 Speaker 的所有常见 Owner/Player 字段/属性取 IPlayer
        private static IPlayer TryResolveSpeakerOwnerStrong(PhraseSpeakerClass sp)
        {
            if (sp == null) return null;
            try
            {
                var tv = HarmonyLib.Traverse.Create(sp);

                object owner =
                    (tv.Property("Owner") != null ? tv.Property("Owner").GetValue() : null) ??
                    (tv.Field("_owner") != null ? tv.Field("_owner").GetValue() : null) ??
                    (tv.Field("Owner") != null ? tv.Field("Owner").GetValue() : null) ??
                    (tv.Property("Player") != null ? tv.Property("Player").GetValue() : null) ??
                    (tv.Field("Player") != null ? tv.Field("Player").GetValue() : null) ??
                    (tv.Property("IPlayer") != null ? tv.Property("IPlayer").GetValue() : null) ??
                    (tv.Field("IPlayer") != null ? tv.Field("IPlayer").GetValue() : null);

                return owner as IPlayer;
            }
            catch { return null; }
        }

        // B. 兜底：遍历场景里的所有玩家，比较“他们的 Speaker 与当前 __instance 是否是同一个引用”
        private static IPlayer TryResolveSpeakerOwnerFallback(PhraseSpeakerClass sp)
        {
            try
            {
                var gw = Comfort.Common.Singleton<GameWorld>.Instance;
                if (gw == null) return null;

                // 取玩家列表（不同版本名不一，用反射尽量兼容）
                System.Collections.IEnumerable players = null;
                var tw = HarmonyLib.Traverse.Create(gw);
                object listObj =
                    (tw.Property("AllPlayers") != null ? tw.Property("AllPlayers").GetValue() : null) ??
                    (tw.Field("AllPlayers") != null ? tw.Field("AllPlayers").GetValue() : null) ??
                    (tw.Property("RegisteredPlayers") != null ? tw.Property("RegisteredPlayers").GetValue() : null) ??
                    (tw.Field("RegisteredPlayers") != null ? tw.Field("RegisteredPlayers").GetValue() : null) ??
                    (tw.Property("AlivePlayers") != null ? tw.Property("AlivePlayers").GetValue() : null) ??
                    (tw.Field("AlivePlayers") != null ? tw.Field("AlivePlayers").GetValue() : null);
                if (listObj is System.Collections.IEnumerable) players = (System.Collections.IEnumerable)listObj;
                if (players == null) return null;

                foreach (object o in players)
                {
                    var p = o as IPlayer;
                    if (p == null) continue;

                    // 取 p 的 Speaker（属性+字段）
                    object ps =
                        HarmonyLib.Traverse.Create(p).Property("Speaker")?.GetValue() ??
                        HarmonyLib.Traverse.Create(p).Field("Speaker")?.GetValue() ??
                        HarmonyLib.Traverse.Create(p).Field("_speaker")?.GetValue();
                    if (ps != null && object.ReferenceEquals(ps, sp))
                        return p;
                }
            }
            catch { }
            return null;
        }

        // C. 兜底2：通过 TrackTransform 的根对象去比对玩家的 transform.root（极端情况下使用）
        // C. 兜底2：通过 TrackTransform 的根对象去比对玩家的 transform.root（极端情况下使用）
        private static IPlayer TryResolveByTrackRoot(PhraseSpeakerClass sp)
        {
            try
            {
                if (sp == null || sp.TrackTransform == null) return null;

                // 1) 从 BifacialTransform 反射拿 UnityEngine.Transform
                UnityEngine.Transform tf = null;
                var trB = HarmonyLib.Traverse.Create(sp.TrackTransform);
                object tObj =
                    (trB.Property("Transform") != null ? trB.Property("Transform").GetValue() : null) ??
                    (trB.Field("Transform") != null ? trB.Field("Transform").GetValue() : null) ??
                    (trB.Property("Original") != null ? trB.Property("Original").GetValue() : null) ??
                    (trB.Field("Original") != null ? trB.Field("Original").GetValue() : null) ??
                    (trB.Property("Anchor") != null ? trB.Property("Anchor").GetValue() : null) ??
                    (trB.Field("Anchor") != null ? trB.Field("Anchor").GetValue() : null);

                tf = tObj as UnityEngine.Transform;
                if (tf == null) return null;

                var root = tf.root;

                // 2) 遍历玩家，找 transform.root 相同者
                var gw = Comfort.Common.Singleton<GameWorld>.Instance;
                if (gw == null) return null;

                System.Collections.IEnumerable players = null;
                var tw = HarmonyLib.Traverse.Create(gw);
                object listObj =
                    (tw.Property("AllPlayers") != null ? tw.Property("AllPlayers").GetValue() : null) ??
                    (tw.Field("AllPlayers") != null ? tw.Field("AllPlayers").GetValue() : null) ??
                    (tw.Property("AlivePlayers") != null ? tw.Property("AlivePlayers").GetValue() : null) ??
                    (tw.Field("AlivePlayers") != null ? tw.Field("AlivePlayers").GetValue() : null);
                if (listObj is System.Collections.IEnumerable) players = (System.Collections.IEnumerable)listObj;
                if (players == null) return null;

                foreach (object o in players)
                {
                    var p = o as IPlayer;
                    if (p == null) continue;

                    UnityEngine.Transform pt =
                        HarmonyLib.Traverse.Create(p).Property("Transform")?.GetValue() as UnityEngine.Transform;
                    if (pt == null)
                    {
                        var go = HarmonyLib.Traverse.Create(p).Property("gameObject")?.GetValue() as UnityEngine.GameObject
                              ?? HarmonyLib.Traverse.Create(p).Field("gameObject")?.GetValue() as UnityEngine.GameObject;
                        if (go != null) pt = go.transform;
                    }

                    if (pt != null && pt.root == root)
                        return p;
                }
            }
            catch { }
            return null;
        }

    }

    internal static class FikaManualPatch
    {
        public static void TryPatchFikaIfPresent(HarmonyLib.Harmony harmony)
        {
            try
            {
                // 反射获取 Fika 类型（不产生编译期依赖）
                var tPkt = HarmonyLib.AccessTools.TypeByName(
                    "Fika.Core.Networking.Packets.Player.Common.SubPackets.PhrasePacket");
                var tFikaPlayer = HarmonyLib.AccessTools.TypeByName(
                    "Fika.Core.Main.Players.FikaPlayer");

                if (tPkt == null || tFikaPlayer == null)
                {
                    if (Settings.DanmakuDebugVerbose != null && Settings.DanmakuDebugVerbose.Value)
                        s_Log.LogInfo("[FikaPatch] Fika types not found, skip patch.");
                    return;
                }

                var executeMi = HarmonyLib.AccessTools.Method(tPkt, "Execute", new System.Type[] { tFikaPlayer });
                if (executeMi == null)
                {
                    if (Settings.DanmakuDebugVerbose != null && Settings.DanmakuDebugVerbose.Value)
                        s_Log.LogInfo("[FikaPatch] PhrasePacket.Execute not found, skip.");
                    return;
                }

                var postfix = HarmonyLib.AccessTools.Method(typeof(FikaManualPatch), "Postfix");
                harmony.Patch(executeMi, postfix: new HarmonyLib.HarmonyMethod(postfix));

                if (Settings.DanmakuDebugVerbose != null && Settings.DanmakuDebugVerbose.Value)
                    s_Log.LogInfo("[FikaPatch] Patched PhrasePacket.Execute(Postfix).");
            }
            catch (Exception e)
            {
                // 不影响其它补丁
                try { s_Log.LogWarning("[FikaPatch] Patch failed: " + e); } catch { }
            }
        }

        // 维持原探针 Postfix 的宽松签名（仅日志/探测用途）
        private static void Postfix(object __instance, object player)
        {
            try
            {
                var tv = HarmonyLib.Traverse.Create(__instance);
                object trigObj = tv.Field("PhraseTrigger") != null ? tv.Field("PhraseTrigger").GetValue() : null;
                object idxObj = tv.Field("PhraseIndex") != null ? tv.Field("PhraseIndex").GetValue() : null;

                string trig = trigObj != null ? trigObj.ToString() : "?";
                int idx = 0; if (idxObj is int ii) idx = ii; else { int iv; if (idxObj != null && int.TryParse(idxObj.ToString(), out iv)) idx = iv; }

                if (Settings.DanmakuDebugVerbose != null && Settings.DanmakuDebugVerbose.Value)
                    s_Log.LogInfo("[FikaProbe] Execute(trigger=" + trig + ", index=" + idx + ")");
            }
            catch { }
        }
    }
}
