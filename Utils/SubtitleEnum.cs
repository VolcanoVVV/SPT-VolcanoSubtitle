using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Subtitle.Utils
{
    public static class SubtitleEnum
    {
        // AI名称表格
        public static readonly Dictionary<string, string> DEFAULT_AI_TYPE_LABELS =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // —— PMC —— 
            { "pmcUSEC", "Usec" },
            { "pmcBEAR", "Bear" },
            // —— Boss —— 
            { "followerBigPipe", "Big Pipe" },
            { "followerBirdEye", "Brideye" },
            { "sectantPrizrak", "Ghost" },
            { "bossGluhar", "Glukhar" },
            { "bossBoar", "Kaban" },
            { "bossKilla", "Killa" },
            { "bossKnight", "Knight" },
            { "bossKolontay", "Kolontay" },
            { "sectantOni", "Oni" },
            { "sectantPredvestnik", "Predvestnik" },
            { "bossPartisan", "Partisan" },
            { "bossBully", "Reshala" },
            { "bossSanitar", "Sanitar" },
            { "bossTagillaAgro", "米诺陶洛斯" },
            { "bossKojaniy", "Shturman" },
            { "bossTagilla", "Tagilla" },
            { "bossKillaAgro", "迷宫Killa" },
            { "infectedtagilla", "丧尸Tagilla" },
            { "bossZryachiy", "Zryachiy" },
            { "ravangeZryachiyEvent", "复仇Zryachiy" },
            // —— Scav —— 
            { "assault", "Scav" },
            { "cursedAssault", "诅咒Scav" },
            { "crazyAssaultEvent", "强化Scav" },
            { "skier", "南瓜头" },
            { "infectedpmc", "丧尸PMC" },
            { "infectedlaborant", "丧尸研究员" },
            { "infectedassault", "丧尸Scav" },
            { "infectedcivil", "丧尸平民" },
            // —— 精英AI —— 
            { "followerBoarClose1", "Boar" },
            { "sectantPriest", "邪教徒" },
            { "sectantWarrior", "邪教徒" },
            { "followerGluharAssault", "Glukhar小弟(突击)" },
            { "followerGluharScout", "Glukhar小弟(侦察)" },
            { "followerGluharSecurity", "Glukhar小弟(守卫)" },
            { "followerBoarClose2", "Gus" },
            { "followerBoar", "Kaban小弟" },
            { "bossBoarSniper", "Kaban小弟(狙击)" },
            { "followerKolontayAssault", "Kolontay小弟(突击)" },
            { "followerKolontaySecurity", "Kolontay小弟(守卫)" },
            { "PmcBot", "Raider" },
            { "followerBully", "Reshala小弟" },
            { "ExUsec", "Rogue" },
            { "arenafighterevent", "寻血猎犬" },
            { "followerSanitar", "Sanitar小弟" },
            { "followerKojaniy", "Shturman小弟" },
            { "followerZryachiy", "Zryachiy小弟" },
            { "peacemaker", "维和部队" },
            { "tagillaHelperAgro", "迷宫守卫" }
        };

        // —— 新增：voiceKey → “声线名”默认映射 —— //
        // 例：玩家/AI 档案里的 Voice 通常类似 usec_1 / usec_2 / bear_1 ...
        public static readonly Dictionary<string, string> DEFAULT_VOICE_KEY_LABELS =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // 玩家/PMC 常见声线
            { "Usec_1", "Michael" },
            { "Usec_3", "Josh" },
            { "Usec_5", "Patrick" },
            { "Usec_2", "Chris" },
            { "Usec_4", "Brent" },
            { "Usec_6", "Charlie" },
            { "Bear_2_Eng", "Sergei" },
            { "Bear_1", "Alex" },
            { "Bear_2", "Mikhail" },
            { "Bear_3", "Sergei" },
            { "Bear_1_Eng", "Alex" },
            // 兜底键（如果有自定义默认包）
            { "_default", "Player" }
        };
    }
}
