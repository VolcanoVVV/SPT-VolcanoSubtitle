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
using System.Reflection;
using UnityEngine;
using static Subtitle.Config.Settings;
#if GAME_4_1
using SpeakerClass = EFT.BaseSpeaker;
#else
using SpeakerClass = PhraseSpeakerClass;
#endif

[HarmonyPatch]
public static class SubtitlePatch
{
    // 调试日志源（输出均由 EnableDebugTools / DanmakuDebugVerbose 等开关控制）
    private static readonly ManualLogSource s_Log =
        BepInEx.Logging.Logger.CreateLogSource("Subtitle.Debug");
    private static float s_LastZombieSubtitleTime = -999f;
    private static float s_LastZombieDanmakuTime = -999f;
    private static float s_LastZombieWorld3DTime = -999f;

    // —— 语音事件去重：同一 spkId+netId/trigger 在窗口内只处理一次 ——
    // key 结构：高 32 位 = spkId；低 31 位 = netId/trigger 值；bit31=1 表示 netId 键（避免字符串分配）
    private static readonly Dictionary<long, float> s_RecentVoiceOnce = new Dictionary<long, float>();

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


    private static string TryGetAccountId(IPlayer p)
    {
        try
        {
            if (p == null) return null;
            // 强类型：IPlayer.AccountId（4.1.x）
            var direct = p.AccountId;
            if (!string.IsNullOrEmpty(direct)) return direct;

            // 再从 Profile 上拿（反射兜底）
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

    private static BotOwner GetBotOwnerByPlayer(IPlayer p)
    {
        if (p == null) return null;
        try
        {
            // AIData.BotOwner 直取（4.1.x 强类型）
            var bo = p.AIData != null ? p.AIData.BotOwner : null;
            if (bo != null) return bo;
        }
        catch { }

        // 兜底：遍历在场玩家，用 BotOwner.GetPlayer 反查
        // （旧实现反射 Property("Player")，该属性不存在恒为 null —— 这里修正为 GetPlayer）
        try
        {
            var gw = Singleton<GameWorld>.Instance;
            var list = gw != null ? gw.AllAlivePlayersList : null;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var pl = list[i];
                    if (pl == null || pl.AIData == null) continue;
                    var bo = pl.AIData.BotOwner;
                    if (bo != null && object.ReferenceEquals(bo.GetPlayer, p)) return bo;
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

        // 1) 强类型：Profile.Info.Settings.Role（4.x 可直接用）
        try
        {
            var role = p.Profile.Info.Settings.Role;   // WildSpawnType
            return role.ToString();
        }
        catch { }

        // 2) 备选：AIData.BotOwner.Profile.Info.Settings.Role（强类型）
        try
        {
            var botOwner = p.AIData != null ? p.AIData.BotOwner : null;
            if (botOwner != null)
            {
                var role = botOwner.Profile.Info.Settings.Role;
                return role.ToString();
            }
        }
        catch { }

        // 3) 反射兜底：BotOwner.WildSpawnType / Role
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
        return Settings.GetRoleLabel(aiTypeRaw, aiTypeRaw);
    }

    // voiceKey → 标签 的映射函数
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

        // 1) 先用 Profile.Nickname（玩家/AI 都常有，4.1.x 强类型）
        try
        {
            var prof = p.Profile;
            if (prof != null && !string.IsNullOrEmpty(prof.Nickname)) return prof.Nickname;
        }
        catch { }

        // 2) AI：尝试从 PlayerOwner/BotOwner 取昵称/名字
        if (p.IsAI)
        {
            try
            {
                // PlayerOwner.Nickname（保留反射，IAIData.PlayerOwner 无强类型保证）
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

                // BotOwner.Profile.Nickname（强类型；BotOwner 上不存在 "Name" 属性，旧探测恒空，已移除）
                var botOwner = GetBotOwnerByPlayer(p);
                if (botOwner != null)
                {
                    var boProf = botOwner.Profile;
                    if (boProf != null && !string.IsNullOrEmpty(boProf.Nickname)) return boProf.Nickname;
                }
            }
            catch { }
        }

        // 3) 兜底：AccountId → ProfileId → Profile.Id
        var acc = TryGetAccountId(p);
        if (!string.IsNullOrEmpty(acc)) return acc;

        try
        {
            var pid = p.ProfileId;
            if (!string.IsNullOrEmpty(pid)) return pid;
        }
        catch { }

        var profId = p.Profile != null ? Traverse.Create(p.Profile).Property("Id")?.GetValue() : null;
        if (profId != null) return profId.ToString();

        return "Unknown";
    }

    // 实际说话层：只做 Postfix
    [HarmonyPatch(typeof(SpeakerClass), "Play")]
    [HarmonyPostfix]
    public static void PhraseSpeakerPlayPostfix(
        SpeakerClass __instance,
        EPhraseTrigger trigger,
        ETagStatus tags,
        bool demand,
        int? importance,
        ref TagBank __result)
    {
        // 1) 失败/忽略直接退出
        if (__result == null) return;
        // 2) 取到已选中的具体剪辑（BaseSpeaker.Clip 是公开字段）
        TaggedClip clip = null;
        try { clip = __instance.Clip; } catch { }
        if (clip == null)
        {
            // 反射兜底（防字段变动）
            try { clip = Traverse.Create(__instance).Field("Clip").GetValue() as TaggedClip; } catch { }
        }
        if (clip == null) return;

        GameWorld gw = Singleton<GameWorld>.Instance;

        // 3) 解析说话者（优先：对象索引 → 再兜底）
        IPlayer speakerPlayer = SpeakerIndex.TryGetBySpeaker(__instance);
        if (speakerPlayer == null && gw != null)
        {
            // 极小概率：注册时没抓到，说话时再从当前已知玩家补一次索引
            var list = gw.AllAlivePlayersList; // List<Player>（4.1.x 强类型）
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] != null) SpeakerIndex.IndexPlayer(list[i]);
                }
                speakerPlayer = SpeakerIndex.TryGetBySpeaker(__instance);
            }
        }

        // 失败再走 Id 映射 / 强解析 / 兜底解析
        if (speakerPlayer == null) speakerPlayer = TryResolveByProfileMap(__instance);
        if (speakerPlayer == null) speakerPlayer = SpeakerResolver.TryResolveStrong(__instance);
        if (speakerPlayer == null) speakerPlayer = SpeakerResolver.TryResolveFallback(__instance);

        // 4) 三键：voiceKey / trigger / netId（trigger.ToString() 只算一次）
        string netIdStr = clip.NetId.ToString();
        string trigStr = trigger.ToString();

        // 玩家优先的智能解析
        string voiceKey = ResolveVoiceKeySmart(speakerPlayer, __instance);

        // 5) 三键查字幕
        string textSub = PhraseSubtitle.GetSubtitleForChannel("Subtitle", voiceKey, trigStr, netIdStr);
        string textDm = PhraseSubtitle.GetSubtitleForChannel("Danmaku", voiceKey, trigStr, netIdStr);
        string textW3d = PhraseSubtitle.GetSubtitleForChannel("World3D", voiceKey, trigStr, netIdStr);
        if (string.IsNullOrEmpty(textSub) && string.IsNullOrEmpty(textDm) && string.IsNullOrEmpty(textW3d)) return;

        // 6) 次信息与过滤：距离 & 友军判定（每事件各算一次）
        IPlayer mainPlayer = gw != null ? gw.MainPlayer as IPlayer : null;
        bool isLocalSpeaker = (speakerPlayer != null && speakerPlayer.IsYourPlayer);
        bool isFriendly = (!isLocalSpeaker) && (speakerPlayer != null && speakerPlayer.IsFriendlyToMain());

        // 7) 玩家元数据每事件只解析一次，后续全部复用
        string aiTypeRaw = GetAITypeOrPlayer(speakerPlayer);   // player / WildSpawnType / ai
        string nameForShow = GetDisplayName(speakerPlayer);    // 玩家/AI 昵称优先

        // 8) 调试日志：仅调试开关开启时才拼字符串
        if (Settings.EnableDebugTools != null && Settings.EnableDebugTools.Value)
        {
            try
            {
                s_Log.LogInfo(
                    "[SubtitleDbg] voiceKey=" + voiceKey +
                    " trigger=" + trigger +
                    " tags=" + tags +
                    " netId=" + netIdStr +
                    " len=" + clip.Length.ToString("F2") + "s " +
                    " bank=" + __result.name +
                    " aiType=" + aiTypeRaw +
                    " name=" + nameForShow +
                    " friendly=" + (isFriendly ? "1" : "0"));
            }
            catch (Exception e)
            {
                s_Log.LogWarning("[SubtitleDbg] log failed: " + e);
            }
        }

        // 9) 时长用已选 Clip 的长度（ EmitPhrase 内 +0.5s 缓冲）
        float clipLength;
        try { clipLength = clip.Length; } catch { clipLength = -1f; }

        // 10) 统一输出管线（Play 路径在投递前去重，丧尸规则由本机统一应用）
        EmitPhrase(__instance, speakerPlayer, voiceKey, netIdStr, trigger,
            textSub, textDm, textW3d,
            isLocalSpeaker, isFriendly, aiTypeRaw, nameForShow, mainPlayer,
            clipLength, true);
    }

    // —— 统一输出管线：Play（本地）与 PlayDirect（远端复刻）两条路径共用 ——
    // 丧尸显示与冷却始终读取当前客户端配置；localPlayPath 仅区分去重时机与调试日志。
    private static void EmitPhrase(
        SpeakerClass speakerInstance,
        IPlayer speakerPlayer,
        string voiceKey,
        string netIdStr,
        EPhraseTrigger trigger,
        string textSub, string textDm, string textW3d,
        bool isLocalSpeaker, bool isFriendly,
        string aiTypeRaw, string nameForShow,
        IPlayer mainPlayer,
        float clipLength,
        bool localPlayPath)
    {
        // —— 配置快照：本次事件每项只读一次（保留“条目为 null”时的原语义）——
        bool showPmcNameSub = Settings.SubtitleShowPmcName != null && Settings.SubtitleShowPmcName.Value;
        bool showPmcNameDm = Settings.DanmakuShowPmcName != null && Settings.DanmakuShowPmcName.Value;
        bool showPmcNameW3d = Settings.World3DShowPmcName != null && Settings.World3DShowPmcName.Value;
        bool showScavNameSub = Settings.SubtitleShowScavName != null && Settings.SubtitleShowScavName.Value;
        bool showScavNameDm = Settings.DanmakuShowScavName != null && Settings.DanmakuShowScavName.Value;
        bool showScavNameW3d = Settings.World3DShowScavName != null && Settings.World3DShowScavName.Value;

        bool showRoleSub = Settings.SubtitleShowRoleTag == null ? true : Settings.SubtitleShowRoleTag.Value;
        bool showRoleDm = Settings.DanmakuShowRoleTag == null ? true : Settings.DanmakuShowRoleTag.Value;
        bool showRoleW3d = Settings.World3DShowRoleTag == null ? true : Settings.World3DShowRoleTag.Value;

        // 距离上限：条目为 null 时不过滤（MaxValue 等价于不过滤）
        float limitSub = Settings.SubtitleMaxDistanceMeters != null ? Settings.SubtitleMaxDistanceMeters.Value : float.MaxValue;
        float limitDm = Settings.DanmakuMaxDistanceMeters != null ? Settings.DanmakuMaxDistanceMeters.Value : float.MaxValue;
        float limitW3d = Settings.World3DMaxDistanceMeters != null ? Settings.World3DMaxDistanceMeters.Value : float.MaxValue;

        bool showDistSub = Settings.SubtitleShowDistance != null && Settings.SubtitleShowDistance.Value;
        bool showDistDm = Settings.DanmakuShowDistance != null && Settings.DanmakuShowDistance.Value;
        bool showDistW3d = Settings.World3DShowDistance != null && Settings.World3DShowDistance.Value;

        // === 四分法：按说话者类别 × 频道 ===
        // 统一入口：仅用 Settings 的分类器判 AI
        var kind = Settings.GuessRoleKindFromAiType(aiTypeRaw);

        // 玩家/队友覆盖（保证自己/友军永远归到 Player/Teammate）
        if (isLocalSpeaker) kind = Settings.RoleKind.Player;
        else if (isFriendly) kind = Settings.RoleKind.Teammate;

        Color colorSub = Settings.GetTextColor(kind, Settings.Channel.Subtitle);
        Color colorDm = Settings.GetTextColor(kind, Settings.Channel.Danmaku);
        Color colorW3d = Settings.GetTextColor(kind, Settings.Channel.World3D);

        // 1) 先拿“基准 roletag”（不含冒号，按频道区分代称）；元数据全部复用调用方已解析的结果
        string baseRoleSub = GetRoleTagFromPlayer(speakerPlayer, Settings.Channel.Subtitle, speakerInstance, aiTypeRaw, nameForShow, voiceKey, isFriendly);
        string baseRoleDm = GetRoleTagFromPlayer(speakerPlayer, Settings.Channel.Danmaku, speakerInstance, aiTypeRaw, nameForShow, voiceKey, isFriendly);
        string baseRoleW3d = GetRoleTagFromPlayer(speakerPlayer, Settings.Channel.World3D, speakerInstance, aiTypeRaw, nameForShow, voiceKey, isFriendly);

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
                if (speakerPlayer.Side == EPlayerSide.Bear || speakerPlayer.Side == EPlayerSide.Usec) isPMC = true;
                if (speakerPlayer.Side == EPlayerSide.Savage) isSCAV = true;
            }
        }
        catch { }

        // 3) 依据频道选项决定“是否用名字替代 roletag”
        string roleTagSubText = baseRoleSub;   // 字幕 roletag 原文
        string roleTagDmText = baseRoleDm;     // 弹幕 roletag 原文
        string roleTagW3dText = baseRoleW3d;   // World3D roletag 原文

        if (isPMC)
        {
            if (showPmcNameSub) roleTagSubText = string.IsNullOrEmpty(nameForShow) ? baseRoleSub : nameForShow;
            if (showPmcNameDm) roleTagDmText = string.IsNullOrEmpty(nameForShow) ? baseRoleDm : nameForShow;
            if (showPmcNameW3d) roleTagW3dText = string.IsNullOrEmpty(nameForShow) ? baseRoleW3d : nameForShow;
        }
        if (isSCAV)
        {
            if (showScavNameSub) roleTagSubText = string.IsNullOrEmpty(nameForShow) ? baseRoleSub : nameForShow;
            if (showScavNameDm) roleTagDmText = string.IsNullOrEmpty(nameForShow) ? baseRoleDm : nameForShow;
            if (showScavNameW3d) roleTagW3dText = string.IsNullOrEmpty(nameForShow) ? baseRoleW3d : nameForShow;
        }

        // 4) 再各自上色 + 拼入正文
        string roleColoredSub = Settings.WrapRoleTag(roleTagSubText + "：", kind, Settings.Channel.Subtitle);
        string roleColoredDm = Settings.WrapRoleTag(roleTagDmText + "：", kind, Settings.Channel.Danmaku);
        string roleColoredW3d = Settings.WrapRoleTag(roleTagW3dText + "：", kind, Settings.Channel.World3D);

        string fullSub = string.IsNullOrEmpty(textSub) ? null : (showRoleSub ? (roleColoredSub + textSub) : textSub);
        string fullDm = string.IsNullOrEmpty(textDm) ? null : (showRoleDm ? (roleColoredDm + textDm) : textDm);
        string fullW3d = string.IsNullOrEmpty(textW3d) ? null : (showRoleW3d ? (roleColoredW3d + textW3d) : textW3d);

        // 仅对“非本地玩家/AI”应用距离过滤
        float? distMeters = (!isLocalSpeaker) ? ComputeDistanceMeters(speakerPlayer, mainPlayer) : (float?)null;
        bool allowSubtitle = !string.IsNullOrEmpty(textSub);
        bool allowDanmaku = !string.IsNullOrEmpty(textDm);
        bool allowWorld3d = !string.IsNullOrEmpty(textW3d);
        if (Settings.EnableWorld3D != null && !Settings.EnableWorld3D.Value)
            allowWorld3d = false;
        if (isLocalSpeaker && Settings.World3DShowSelf != null && !Settings.World3DShowSelf.Value)
            allowWorld3d = false;

        // —— 距离过滤：只在“非本地且拿到距离”时调整 allowXxx ——
        if (!isLocalSpeaker && distMeters.HasValue)
        {
            float d = distMeters.Value;

            if (d > limitSub) allowSubtitle = false;
            if (d > limitDm) allowDanmaku = false;
            if (d > limitW3d) allowWorld3d = false;

            // 距离过滤调试日志（仅 Play 路径原有此日志）
            if (localPlayPath && (!allowSubtitle || !allowDanmaku))
            {
                try
                {
                    s_Log.LogInfo("[SubtitleDbg] filtered by distance: d=" + Mathf.RoundToInt(d) + "m"
                        + " sub<=" + limitSub
                        + " dm<=" + limitDm);
                }
                catch { }
            }
        }

        // —— 丧尸（不含 infectedtagilla）过滤 & 冷却节流 ——
        // Fika 只同步语音事件；每个客户端在这里独立应用自己的三频道设置。
        var aiLC = (aiTypeRaw ?? "").ToLowerInvariant();
        bool isZombieNonTagilla = (kind == Settings.RoleKind.Zombie) && (aiLC.IndexOf("tagilla") < 0);

        if (isZombieNonTagilla)
        {
            float nowUnscaled = Time.unscaledTime;

            if (Settings.SubtitleZombieEnabled != null && !Settings.SubtitleZombieEnabled.Value)
                allowSubtitle = false;

            int subCd = (Settings.SubtitleZombieCooldownSec != null) ? Settings.SubtitleZombieCooldownSec.Value : 0;
            if (subCd > 0 && (nowUnscaled - s_LastZombieSubtitleTime) < subCd)
                allowSubtitle = false;

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
            if (showDistSub && allowSubtitle)
                fullSub += distanceSuffix;
            if (showDistDm && allowDanmaku)
                fullDm += distanceSuffix;
            if (showDistW3d && allowWorld3d)
                fullW3d += distanceSuffix;
        }

        // —— 去重：Play 路径在投递前去重；PlayDirect 路径已在构建前去重 ——
        if (localPlayPath && SuppressDuplicate(speakerInstance, netIdStr, trigger)) return;

        // —— 最终投递 + 成功后更新时间戳 ——
        try
        {
            // 计算本次建议显示时长：用已选 Clip 的长度 + 0.5s 缓冲；无 Clip 时给 0.8s 兜底
            float dur = clipLength >= 0f ? Mathf.Max(0f, clipLength) + 0.5f : 0.8f;

            if (Settings.EnableSubtitle.Value && allowSubtitle)
            {
                SubtitleManager.Instance.AddSubtitle(fullSub, colorSub, dur, kind);
                if (isZombieNonTagilla) s_LastZombieSubtitleTime = Time.unscaledTime;
            }

            if (Settings.EnableDanmaku.Value && allowDanmaku)
            {
                if (Settings.DanmakuDebugVerbose.Value)
                {
                    // 两条路径原有的日志文案差异，按原样保留
                    s_Log.LogInfo(localPlayPath
                        ? "[Danmaku] -> call AddDanmaku | text=\"" + fullDm + "\" color=" + colorDm
                        : "[Danmaku] -> AddDanmaku | text=\"" + fullDm + "\"");
                }
                SubtitleManager.Instance.AddDanmaku(fullDm, colorDm);
                if (isZombieNonTagilla) s_LastZombieDanmakuTime = Time.unscaledTime;
            }
            if (allowWorld3d && speakerPlayer != null)
            {
                SubtitleManager.Instance.AddWorld3D(speakerPlayer, fullW3d, colorW3d, dur);
                if (isZombieNonTagilla) s_LastZombieWorld3DTime = Time.unscaledTime;
            }
        }
        catch (Exception e)
        {
            s_Log.LogWarning(localPlayPath
                ? "[Subtitle] AddSubtitle/Danmaku failed: " + e
                : "[Subtitle] PlayDirect output failed: " + e);
        }
    }

    // ========== 通过 BaseSpeaker.Id 直连 GameWorld 的 ProfileId→Player 映射 ==========
    private static IPlayer TryResolveByProfileMap(SpeakerClass speaker)
    {
        try
        {
            if (speaker == null) return null;

            // 先拿 speaker 的 Id（BaseSpeaker.Id 是公开属性）
            int spkId = 0;
            try { spkId = speaker.Id; } catch { }
            if (spkId == 0)
            {
                // 反射兜底
                var tr = Traverse.Create(speaker);
                object idObj = tr.Field("Id")?.GetValue() ?? tr.Property("Id")?.GetValue();
                if (idObj is int i0) spkId = i0;
                else if (idObj is string s0 && int.TryParse(s0, out var iv0)) spkId = iv0;
            }
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

            // 若没有字典，降级：遍历 AllAlivePlayersList 比 Id / ProfileId
            var list = gw.AllAlivePlayersList;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var ip = list[i];
                    if (ip == null) continue;

                    // 强类型：IPlayer.Id（speaker.Id 即玩家 Id）
                    try { if (ip.Id == spkId) return ip; } catch { }

                    // 兼容：ProfileId 是可转的字符串
                    try
                    {
                        var pidObj = Traverse.Create(ip).Property("ProfileId")?.GetValue()
                                  ?? Traverse.Create(ip.Profile).Property("Id")?.GetValue();
                        if (pidObj is int pi && pi == spkId) return ip;
                        if (pidObj is string ps && int.TryParse(ps, out var piv) && piv == spkId) return ip;
                    }
                    catch { }
                }
            }
        }
        catch { }
        return null;
    }

    // —— 说话者解析：合并原 Play / PlayDirect 两套实现，保留双方全部解析策略 ——
    private static class SpeakerResolver
    {
        // A. 强解析：从 Speaker 的常见 Owner/Player 成员取 IPlayer
        public static IPlayer TryResolveStrong(SpeakerClass sp)
        {
            if (sp == null) return null;
            try
            {
                var tv = Traverse.Create(sp);

                // 常见字段/属性名（取两套实现的并集）：Owner / _owner / Player / IPlayer
                object owner =
                    tv.Property("Owner")?.GetValue() ??
                    tv.Field("_owner")?.GetValue() ??
                    tv.Field("Owner")?.GetValue() ??
                    tv.Property("Player")?.GetValue() ??
                    tv.Field("Player")?.GetValue() ??
                    tv.Property("IPlayer")?.GetValue() ??
                    tv.Field("IPlayer")?.GetValue();

                // 直接是 IPlayer（Player 或 ObservedPlayer）
                var ip = owner as IPlayer;
                if (ip != null) return ip;

                // Owner 可能是 BotOwner → 取其 GetPlayer
                if (owner != null)
                {
                    var bo = owner as BotOwner;
                    if (bo != null)
                    {
                        try { return bo.GetPlayer; } catch { }
                    }
                    // 反射兜底：注意 4.1.x 是 GetPlayer 属性（旧代码取 "Player" 恒为 null）
                    var maybePlayer = Traverse.Create(owner).Property("GetPlayer")?.GetValue()
                                   ?? Traverse.Create(owner).Property("Player")?.GetValue();
                    return maybePlayer as IPlayer;
                }
            }
            catch { }
            return null;
        }

        // B. 兜底：枚举玩家列表，比较“玩家的 Speaker 与当前实例是否同一引用”，再比 Speaker.Id
        public static IPlayer TryResolveFallback(SpeakerClass sp)
        {
            if (sp == null) return null;
            try
            {
                var gw = Singleton<GameWorld>.Instance;
                if (gw == null) return null;

                // B1. 强类型快路径：AllAlivePlayersList（List<Player>）比 Player.Speaker 引用
                var list = gw.AllAlivePlayersList;
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var pl = list[i];
                        if (pl != null && object.ReferenceEquals(pl.Speaker, sp))
                            return pl;
                    }

                    int spkId = SafeGetSpeakerIdFromObj(sp);
                    if (spkId != 0)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            var pl = list[i];
                            if (pl != null && pl.Speaker != null && pl.Speaker.Id == spkId)
                                return pl;
                        }
                    }
                }

                // B2. 反射兜底：兼容非 Player 的 IPlayer 实现（如 Fika 的观察玩家）
                foreach (var p in GetAllPlayersCompat(gw))
                {
                    if (p == null) continue;

                    var spkObj = GetPlayerSpeakerObject(p);
                    if (spkObj != null && object.ReferenceEquals(spkObj, sp))
                        return p;
                }

                int spkId2 = SafeGetSpeakerIdFromObj(sp);
                if (spkId2 != 0)
                {
                    foreach (var p in GetAllPlayersCompat(gw))
                    {
                        var psObj = GetPlayerSpeakerObject(p);
                        int pid = SafeGetSpeakerIdFromObj(psObj);
                        if (pid != 0 && pid == spkId2) return p;
                    }
                }
            }
            catch { }

            // C. 兜底2：TrackTransform 根对象比对（原 PlayDirect 路径独有策略，现两条路径共享）
            return TryResolveByTrackRoot(sp);
        }

        // C. 兜底2：通过 TrackTransform 的根对象去比对玩家的 transform.root（极端情况下使用）
        private static IPlayer TryResolveByTrackRoot(SpeakerClass sp)
        {
            try
            {
                if (sp == null || sp.TrackTransform == null) return null;

                // 1) BifacialTransform.Original 即 UnityEngine.Transform（强类型）
                UnityEngine.Transform tf = null;
                try { tf = sp.TrackTransform.Original; } catch { }
                if (tf == null)
                {
                    // 反射兜底
                    var trB = Traverse.Create(sp.TrackTransform);
                    object tObj =
                        trB.Property("Original")?.GetValue() ??
                        trB.Field("Original")?.GetValue() ??
                        trB.Property("Transform")?.GetValue() ??
                        trB.Property("Anchor")?.GetValue();
                    tf = tObj as UnityEngine.Transform;
                }
                if (tf == null) return null;

                var root = tf.root;

                // 2) 遍历玩家，找 transform.root 相同者
                var gw = Singleton<GameWorld>.Instance;
                if (gw == null) return null;

                // 强类型快路径
                var list = gw.AllAlivePlayersList;
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var pl = list[i];
                        if (pl == null) continue;

                        UnityEngine.Transform pt = null;
                        try { pt = pl.Transform != null ? pl.Transform.Original : null; } catch { }
                        if (pt != null && pt.root == root)
                            return pl;
                    }
                }

                // 反射兜底（非 Player 的 IPlayer 实现）
                foreach (var p in GetAllPlayersCompat(gw))
                {
                    if (p == null) continue;

                    UnityEngine.Transform pt =
                        Traverse.Create(p).Property("Transform")?.GetValue() as UnityEngine.Transform;
                    if (pt == null)
                    {
                        var go = Traverse.Create(p).Property("gameObject")?.GetValue() as UnityEngine.GameObject
                              ?? Traverse.Create(p).Field("gameObject")?.GetValue() as UnityEngine.GameObject;
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

    private static object GetPlayerSpeakerObject(IPlayer p)
    {
        if (p == null) return null;
        try
        {
            // 强类型：Player.Speaker 是公开字段
            var pl = p as Player;
            if (pl != null) return pl.Speaker;

            // 反射兜底（非 Player 实现）
            return Traverse.Create(p).Property("Speaker")?.GetValue()
                ?? Traverse.Create(p).Field("Speaker")?.GetValue()
                ?? Traverse.Create(p).Field("_speaker")?.GetValue();
        }
        catch { return null; }
    }

    private static int SafeGetSpeakerIdFromObj(object spkObj)
    {
        if (spkObj == null) return 0;
        try
        {
            // 强类型：BaseSpeaker.Id 是公开属性
            var bs = spkObj as SpeakerClass;
            if (bs != null) return bs.Id;

            var tr = Traverse.Create(spkObj);
            object idObj =
                tr.Property("Id")?.GetValue() ??
                tr.Field("Id")?.GetValue() ??
                tr.Field("_id")?.GetValue();

            if (idObj is int i) return i;

            int iv;
            if (idObj != null && int.TryParse(idObj.ToString(), out iv)) return iv;
        }
        catch { }
        return 0;
    }

    // —— 语音事件去重：long 复合键，避免每条语音分配字符串 key ——
    private static bool SuppressDuplicate(SpeakerClass speaker, string netIdStr, EPhraseTrigger trigger)
    {
        int spkId = SafeGetSpeakerIdFromObj(speaker);
        if (spkId == 0) return false;

        int netId = 0;
        bool hasNet = !string.IsNullOrEmpty(netIdStr) && int.TryParse(netIdStr, out netId);
        long keyNet = hasNet ? (((long)spkId << 32) | 0x80000000L | ((long)netId & 0x7FFFFFFFL)) : 0L;
        long keyTrig = ((long)spkId << 32) | ((long)(int)trigger & 0x7FFFFFFFL);
        float now = Time.unscaledTime;

        float win = GetDupWindowSec();
        if (win <= 0f)
            return false; // 允许通过（关闭去重）

        bool verbose = Settings.DanmakuDebugVerbose != null && Settings.DanmakuDebugVerbose.Value;
        float last;

        // —— 调试：打印当前检查的 Key 组合 ——
        if (verbose)
        {
            try
            {
                s_Log.LogInfo("[DeDup] check spk=" + spkId
                    + " keyNet=" + (hasNet ? ("N:" + netId) : "-")
                    + " keyTrig=T:" + (int)trigger
                    + " win=" + win.ToString("0.00") + "s");
            }
            catch { }
        }

        // 命中任一键，都视为重复
        if (hasNet && s_RecentVoiceOnce.TryGetValue(keyNet, out last) && now - last < win)
        {
            if (verbose)
            {
                try { s_Log.LogInfo("[DeDup] HIT " + spkId + "|N:" + netId + " dt=" + (now - last).ToString("0.000") + " <= " + win.ToString("0.000")); } catch { }
            }
            return true;
        }
        if (s_RecentVoiceOnce.TryGetValue(keyTrig, out last) && now - last < win)
        {
            if (verbose)
            {
                try { s_Log.LogInfo("[DeDup] HIT " + spkId + "|T:" + (int)trigger + " dt=" + (now - last).ToString("0.000") + " <= " + win.ToString("0.000")); } catch { }
            }
            return true;
        }

        // 首次记录
        if (hasNet) s_RecentVoiceOnce[keyNet] = now;
        s_RecentVoiceOnce[keyTrig] = now;

        // 轻量清理（仅在超过上限时触发，平时零分配）
        if (s_RecentVoiceOnce.Count > 128)
        {
            var toRemove = new List<long>();
            foreach (var kv in s_RecentVoiceOnce)
                if (now - kv.Value > 2f) toRemove.Add(kv.Key);
            for (int i = 0; i < toRemove.Count; i++) s_RecentVoiceOnce.Remove(toRemove[i]);
        }
        return false;
    }

    // —— 兼容 IPlayer 的角色标签 ——
    // aiTypeRaw / displayName / voiceKey / isFriend 由调用方每事件解析一次后传入复用
    private static string GetRoleTagFromPlayer(IPlayer p, Settings.Channel ch, SpeakerClass spk,
        string aiTypeRaw, string displayName, string voiceKey, bool isFriend)
    {
        if (p == null) return "未知";

        if (p.IsYourPlayer)
        {
            var opt = GetSelfPronounOption(ch, false);

            if (opt == SelfPronounOption.略称) return "你";
            if (opt == SelfPronounOption.玩家名)
            {
                return string.IsNullOrEmpty(displayName) ? "你" : displayName;
            }
            if (opt == SelfPronounOption.声线名)
            {
                var label = MapVoiceKeyLabel(voiceKey);
                return string.IsNullOrEmpty(label) ? "你" : label;
            }
        }

        // 友军玩家（队友）
        if (!p.IsYourPlayer && !p.IsAI && isFriend)
        {
            var optTm = GetSelfPronounOption(ch, true);

            if (optTm == SelfPronounOption.略称)
            {
                // 在队友语境下，“你”按需求展示为“队友”
                return "队友";
            }
            if (optTm == SelfPronounOption.玩家名)
            {
                return string.IsNullOrEmpty(displayName) ? "队友" : displayName;
            }
            if (optTm == SelfPronounOption.声线名)
            {
                var label = MapVoiceKeyLabel(voiceKey);
                return string.IsNullOrEmpty(label) ? "队友" : label;
            }
        }

        if (p.IsAI)
        {
            var label = MapAITypeLabel(aiTypeRaw);
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

    private static string ResolveVoiceKeySmart(IPlayer ip, SpeakerClass speaker)
    {
        // 本地玩家与 AI/观察对象顺序一致：先试档案多路径，再退回说话器
        string key = TryVoiceKeyFromProfile(ip);
        if (string.IsNullOrEmpty(key))
            key = TryVoiceKeyFromSpeaker(speaker);

        if (string.IsNullOrEmpty(key))
            key = "_default";
        return key;
    }

    // 从 Profile 里各种可能的路径尝试拿 Voice
    private static string TryVoiceKeyFromProfile(IPlayer player)
    {
        if (player == null) return null;
        try
        {
            var prof = player.Profile;
            if (prof == null) return null;

            // Profile.Info 上无强类型 Voice 成员（4.1.x 编译验证），走反射多路径探测
            var tp = Traverse.Create(prof);
            var infoObj = tp.Property("Info")?.GetValue() ?? tp.Field("Info")?.GetValue();
            if (infoObj != null)
            {
                var ti = Traverse.Create(infoObj);

                var v1 = ti.Property("Voice")?.GetValue() ?? ti.Field("Voice")?.GetValue();
                if (v1 != null) return v1.ToString();

                var settings = ti.Property("Settings")?.GetValue();
                if (settings != null)
                {
                    var vs = Traverse.Create(settings).Property("Voice")?.GetValue();
                    if (vs != null) return vs.ToString();
                }
            }

            var app = tp.Property("Appearance")?.GetValue();
            if (app != null)
            {
                var va = Traverse.Create(app).Property("Voice")?.GetValue();
                if (va != null) return va.ToString();
            }
        }
        catch { }
        return null;
    }

    // 从 BaseSpeaker 里拿（不同版本字段名可能不同）
    private static string TryVoiceKeyFromSpeaker(SpeakerClass spk)
    {
        if (spk == null) return null;

        // 强类型：BaseSpeaker.PlayerVoice 是公开属性
        try { if (!string.IsNullOrEmpty(spk.PlayerVoice)) return spk.PlayerVoice; } catch { }

        // 反射兜底
        try
        {
            var tr = Traverse.Create(spk);
            object pv =
                tr.Field("PlayerVoice")?.GetValue()
             ?? tr.Field("_playerVoice")?.GetValue()
             ?? tr.Property("Voice")?.GetValue();
            return pv != null ? pv.ToString() : null;
        }
        catch { return null; }
    }

    private static IEnumerable<IPlayer> GetAllPlayersCompat(GameWorld gw)
    {
        if (gw == null) yield break;

        // 强类型优先：AllAlivePlayersList（List<Player>）
        var alive = gw.AllAlivePlayersList;
        if (alive != null)
        {
            for (int i = 0; i < alive.Count; i++)
            {
                if (alive[i] != null) yield return alive[i];
            }
            yield break;
        }

        var t = Traverse.Create(gw);

        // AllPlayers / AllPlayersList
        object listObj =
            t.Property("AllPlayers")?.GetValue() ??
            t.Property("AllPlayersList")?.GetValue();

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

        // MainPlayer
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
                // BotOwner.GetPlayer（4.1.x 强类型；旧版反射 Property("Player") 恒为 null）
                var bo = b as BotOwner;
                if (bo != null)
                {
                    IPlayer gp = null;
                    try { gp = bo.GetPlayer; } catch { }
                    if (gp != null) yield return gp;
                    continue;
                }
                var bp = Traverse.Create(b).Property("GetPlayer")?.GetValue()
                      ?? Traverse.Create(b).Property("Player")?.GetValue();
                if (bp is IPlayer ip3) yield return ip3;
            }
        }
    }

    private static float? ComputeDistanceMeters(IPlayer speaker, IPlayer main)
    {
        if (speaker == null || main == null) return null;

        // 强类型：IPlayer.Position（4.1.x）
        try
        {
            float d = Vector3.Distance(speaker.Position, main.Position);
            if (float.IsNaN(d) || float.IsInfinity(d)) return null;
            return d; // 米
        }
        catch { }

        // 反射兜底：Transform.position
        try
        {
            var trS = Traverse.Create(speaker).Property("Transform")?.GetValue();
            var trM = Traverse.Create(main).Property("Transform")?.GetValue();
            if (trS == null || trM == null) return null;
            var sp = (Vector3)Traverse.Create(trS).Property("position").GetValue();
            var mp = (Vector3)Traverse.Create(trM).Property("position").GetValue();
            float d = Vector3.Distance(sp, mp);
            if (float.IsNaN(d) || float.IsInfinity(d)) return null;
            return d;
        }
        catch { return null; }
    }

    // ========== 正式补丁：统一捕捉“远端复刻语音”的播放入口 ==========
    // Fika 在对端复刻时会调用游戏本体的 BaseSpeaker.PlayDirect(trigger, index)
    // 单机/本地玩家仍由 Play(...) 后缀负责；这里仅处理“非本地玩家”，避免重复
    [HarmonyPatch(typeof(SpeakerClass), "PlayDirect")]
    internal static class Subtitle_PlayDirectPatch
    {
        static void Postfix(SpeakerClass __instance, EPhraseTrigger trigger, int index)
        {
            try
            {
                // 1) 反解说话者 IPlayer（强解析 → 兜底，兜底内含 TrackRoot 策略）
                IPlayer speaker = SpeakerResolver.TryResolveStrong(__instance);
                if (speaker == null) speaker = SpeakerResolver.TryResolveFallback(__instance);
                if (speaker == null) return;

                // 2) 本地玩家仍由 Play(...) 处理，这里只管“他人/AI”
                if (speaker.IsYourPlayer) return;

                // 3) 三键：voiceKey + trigger + netId(index)
                string netIdStr = index.ToString();
                // —— 去重（防护 PlayDirect 自身的重复调用；本地玩家已被提前 return）——
                if (SuppressDuplicate(__instance, netIdStr, trigger)) return;
                string trigStr = trigger.ToString();
                string voiceKey = ResolveVoiceKeySmart(speaker, __instance);
                string textSub = PhraseSubtitle.GetSubtitleForChannel("Subtitle", voiceKey, trigStr, netIdStr);
                string textDm = PhraseSubtitle.GetSubtitleForChannel("Danmaku", voiceKey, trigStr, netIdStr);
                string textW3d = PhraseSubtitle.GetSubtitleForChannel("World3D", voiceKey, trigStr, netIdStr);
                if (string.IsNullOrEmpty(textSub) && string.IsNullOrEmpty(textDm) && string.IsNullOrEmpty(textW3d)) return;

                // 4) 友军判定（每事件一次）
                bool isFriendly = false;
                try { isFriendly = speaker.IsFriendlyToMain(); } catch { }

                // 5) 玩家元数据每事件只解析一次，后续全部复用
                string aiTypeRaw = GetAITypeOrPlayer(speaker);
                string nameForShow = GetDisplayName(speaker);

                // 6) 调试日志（受 EnableDebugTools 开关控制）
                if (Settings.EnableDebugTools != null && Settings.EnableDebugTools.Value)
                {
                    try
                    {
                        s_Log.LogInfo(
                            "[SubtitleNet] voiceKey=" + voiceKey +
                            " trigger=" + trigger +
                            " netId=" + netIdStr +
                            " name=" + nameForShow +
                            " friendly=" + (isFriendly ? "1" : "0"));
                    }
                    catch { }
                }

                // 7) mainPlayer 与 Clip 长度（PlayDirect 后 Clip 通常已就绪）
                var gw = Singleton<GameWorld>.Instance;
                IPlayer mainPlayer = gw != null ? gw.MainPlayer as IPlayer : null;
                float clipLength = -1f;
                try { if (__instance != null && __instance.Clip != null) clipLength = __instance.Clip.Length; } catch { }

                // 8) 统一输出管线（PlayDirect 路径已提前去重，丧尸规则由本机统一应用）
                EmitPhrase(__instance, speaker, voiceKey, netIdStr, trigger,
                    textSub, textDm, textW3d,
                    false, isFriendly, aiTypeRaw, nameForShow, mainPlayer,
                    clipLength, false);
            }
            catch (Exception e)
            {
                s_Log.LogWarning("[Subtitle] PlayDirectPatch failed: " + e);
            }
        }
    }
}
