using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.content.figures
{
    internal static class HistoricalFigureCardMinisterSeeds
    {
        public static readonly IReadOnlyList<HistoricalFigureCardDefinition> All =
            Build();

        private static IReadOnlyList<HistoricalFigureCardDefinition> Build()
        {
            var cards = new List<HistoricalFigureCardDefinition>();
            AddPreQin(cards);
            AddHan(cards);
            AddThreeSix(cards);
            AddSuiTang(cards);
            AddFiveSong(cards);
            AddYuanMingQing(cards);
            AddBlueRarityCoverage(cards);
            return cards;
        }

        private static void AddBlueRarityCoverage(
            List<HistoricalFigureCardDefinition> cards)
        {
            M(cards, "qin_zhao_kuo", "\u8d75\u62ec", "\u8d75", "\u6218\u56fd",
                -260, 50, true, "pre_qin_qin");
            M(cards, "qin_yan_hui", "\u989c\u56de", "\u9c81", "\u6625\u79cb",
                -521, 50, false, "pre_qin_qin");
            M(cards, "qin_gongye_chang", "\u516c\u51b6\u957f", "\u9c81", "\u6625\u79cb",
                -500, 50, false, "pre_qin_qin");
            M(cards, "qin_fan_chi", "\u6a0a\u8fdf", "\u9c81", "\u6625\u79cb",
                -500, 50, false, "pre_qin_qin");
            M(cards, "qin_ran_geng", "\u5189\u8015", "\u9c81", "\u6625\u79cb",
                -500, 50, false, "pre_qin_qin");
            M(cards, "qin_ziyou", "\u5b50\u6e38", "\u9c81", "\u6625\u79cb",
                -506, 50, false, "pre_qin_qin");
            M(cards, "qin_zixia", "\u5b50\u590f", "\u9c81", "\u6625\u79cb",
                -507, 50, false, "pre_qin_qin");
            M(cards, "qin_tian_ji", "\u7530\u5fcc", "\u9f50", "\u6218\u56fd",
                -350, 50, true, "pre_qin_qin");
            M(cards, "han_li_yan", "\u674e\u5ef6\u5e74", "\u6c49", "\u897f\u6c49",
                -110, 50, false, "han");
            M(cards, "han_dongfang_shuo", "\u4e1c\u65b9\u6714", "\u6c49", "\u897f\u6c49",
                -130, 50, false, "han");
            M(cards, "han_wang_ji", "\u738b\u5409", "\u6c49", "\u897f\u6c49",
                -80, 50, false, "han");
            M(cards, "han_zhu_maichen", "\u6731\u4e70\u81e3", "\u6c49", "\u897f\u6c49",
                -120, 50, false, "han");
            M(cards, "han_yan_zhu", "\u4e25\u52a9", "\u6c49", "\u897f\u6c49",
                -130, 50, false, "han");
            M(cards, "han_zhong_jun", "\u7ec8\u519b", "\u6c49", "\u897f\u6c49",
                -120, 50, false, "han");
            M(cards, "han_sima_xiangru", "\u53f8\u9a6c\u76f8\u5982", "\u6c49", "\u897f\u6c49",
                -130, 50, false, "han");
            M(cards, "han_mei_cheng", "\u679a\u4e58", "\u6c49", "\u897f\u6c49",
                -140, 50, false, "han");
            M(cards, "three_liu_fang", "\u5218\u653e", "\u9b4f", "\u4e09\u56fd",
                230, 50, false, "three_six_dynasties");
            M(cards, "three_ding_yi", "\u4e01\u4eea", "\u9b4f", "\u4e09\u56fd",
                210, 50, false, "three_six_dynasties");
            M(cards, "three_yang_xiu", "\u6768\u4fee", "\u9b4f", "\u4e09\u56fd",
                210, 50, false, "three_six_dynasties");
            M(cards, "three_chen_lin", "\u9648\u7433", "\u9b4f", "\u4e09\u56fd",
                200, 50, false, "three_six_dynasties");
            M(cards, "three_wang_can", "\u738b\u7cb2", "\u9b4f", "\u4e09\u56fd",
                200, 50, false, "three_six_dynasties");
            M(cards, "three_ruan_ji", "\u962e\u7c4d", "\u9b4f", "\u4e09\u56fd",
                250, 50, false, "three_six_dynasties");
            M(cards, "three_xiang_xiu", "\u5411\u79c0", "\u9b4f", "\u4e09\u56fd",
                250, 50, false, "three_six_dynasties");
            M(cards, "three_shan_tao", "\u5c71\u6d9b", "\u664b", "\u897f\u664b",
                270, 50, false, "three_six_dynasties");
            M(cards, "sui_he_zhizhang", "\u8d3a\u77e5\u7ae0", "\u5510", "\u5510",
                730, 50, false, "sui_tang");
            M(cards, "sui_yu_shinan", "\u865e\u4e16\u5357", "\u5510", "\u5510",
                620, 50, false, "sui_tang");
            M(cards, "sui_chu_suiliang", "\u891a\u9042\u826f", "\u5510", "\u5510",
                650, 50, false, "sui_tang");
            M(cards, "sui_cen_wenben", "\u5c91\u6587\u672c", "\u5510", "\u5510",
                620, 50, false, "sui_tang");
            M(cards, "sui_ma_zhou", "\u9a6c\u5468", "\u5510", "\u5510",
                630, 50, false, "sui_tang");
            M(cards, "sui_xu_jingzong", "\u8bb8\u656c\u5b97", "\u5510", "\u5510",
                630, 50, false, "sui_tang");
            M(cards, "sui_yao_chong_blue", "\u59da\u5d07", "\u5510", "\u5510",
                700, 50, false, "sui_tang");
            M(cards, "sui_song_jing_blue", "\u5b8b\u749f", "\u5510", "\u5510",
                700, 50, false, "sui_tang");
            M(cards, "song_chen_yi", "\u9648\u4e0e\u4e49", "\u5b8b", "\u5357\u5b8b",
                1130, 50, false, "five_song");
            M(cards, "song_wang_yucheng", "\u738b\u79b9\u5041", "\u5b8b", "\u5317\u5b8b",
                990, 50, false, "five_song");
            M(cards, "song_yang_yi", "\u6768\u4ebf", "\u5b8b", "\u5317\u5b8b",
                1000, 50, false, "five_song");
            M(cards, "song_qian_weiyan", "\u94b1\u60df\u6f14", "\u5b8b", "\u5317\u5b8b",
                1000, 50, false, "five_song");
            M(cards, "song_yan_shu", "\u664f\u6b8a", "\u5b8b", "\u5317\u5b8b",
                1020, 50, false, "five_song");
            M(cards, "song_zeng_gong", "\u66fe\u5de9", "\u5b8b", "\u5317\u5b8b",
                1080, 50, false, "five_song");
            M(cards, "song_huang_tingjian", "\u9ec4\u5ead\u575a", "\u5b8b", "\u5317\u5b8b",
                1080, 50, false, "five_song");
            M(cards, "song_liu_kezhuang", "\u5218\u514b\u5e84", "\u5b8b", "\u5357\u5b8b",
                1240, 50, false, "five_song");
            M(cards, "yuan_wu_cheng", "\u5434\u6f84", "\u5143", "\u5143",
                1300, 50, false, "yuan_ming_qing");
            M(cards, "yuan_jiexisi", "\u63ed\u5092\u65af", "\u5143", "\u5143",
                1320, 50, false, "yuan_ming_qing");
            M(cards, "yuan_yuji", "\u865e\u96c6", "\u5143", "\u5143",
                1320, 50, false, "yuan_ming_qing");
            M(cards, "yuan_zhao_mengfu_blue", "\u8d75\u5b5f\u982b", "\u5143", "\u5143",
                1300, 50, false, "yuan_ming_qing");
            M(cards, "ming_song_lian", "\u5b8b\u6fc2", "\u660e", "\u660e",
                1380, 50, false, "yuan_ming_qing");
            M(cards, "ming_fang_xiaoru", "\u65b9\u5b5d\u5b7a", "\u660e", "\u660e",
                1400, 50, false, "yuan_ming_qing");
            M(cards, "ming_xie_jin", "\u89e3\u7f19", "\u660e", "\u660e",
                1400, 50, false, "yuan_ming_qing");
            M(cards, "ming_wang_shizhen", "\u738b\u4e16\u8d1e", "\u660e", "\u660e",
                1550, 50, false, "yuan_ming_qing");
        }

        private static void AddPreQin(List<HistoricalFigureCardDefinition> cards)
        {
            const string crate = "pre_qin_qin";
            M(cards, "qin_guan_zhong", "管仲", "齐", "春秋", -645, 88, false, crate);
            M(cards, "qin_bao_shuya", "鲍叔牙", "齐", "春秋", -640, 72, false, crate);
            M(cards, "qin_yan_ying", "晏婴", "齐", "春秋", -550, 82, false, crate);
            M(cards, "qin_zichan", "子产", "郑", "春秋", -520, 84, false, crate);
            M(cards, "qin_shang_yang", "商鞅", "秦", "战国", -356, 90, false, crate);
            M(cards, "qin_zhang_yi", "张仪", "秦", "战国", -329, 82, false, crate);
            M(cards, "qin_su_qin", "苏秦", "赵", "战国", -320, 78, false, crate);
            M(cards, "qin_fan_ju", "范雎", "秦", "战国", -307, 80, false, crate);
            M(cards, "qin_bai_qi", "白起", "秦", "战国", -260, 98, true, crate);
            M(cards, "qin_wang_jian", "王翦", "秦", "战国", -250, 91, true, crate);
            M(cards, "qin_wang_ben", "王贲", "秦", "战国", -230, 78, true, crate);
            M(cards, "qin_meng_tian", "蒙恬", "秦", "战国", -220, 88, true, crate);
            M(cards, "qin_li_si", "李斯", "秦", "战国", -280, 92, false, crate);
            M(cards, "qin_wei_liao", "尉缭", "秦", "战国", -250, 76, false, crate);
            M(cards, "qin_lu_buwei", "吕不韦", "秦", "战国", -260, 83, false, crate);
            M(cards, "qin_wu_qi", "吴起", "楚", "战国", -410, 89, true, crate);
            M(cards, "qin_sun_wu", "孙武", "吴", "春秋", -500, 98, true, crate);
            M(cards, "qin_sun_bin", "孙膑", "齐", "战国", -330, 86, true, crate);
            M(cards, "qin_lian_po", "廉颇", "赵", "战国", -327, 88, true, crate);
            M(cards, "qin_li_mu", "李牧", "赵", "战国", -245, 98, true, crate);
            M(cards, "qin_le_yi", "乐毅", "燕", "战国", -285, 87, true, crate);
            M(cards, "qin_zhao_she", "赵奢", "赵", "战国", -280, 79, true, crate);
            M(cards, "qin_tian_dan", "田单", "齐", "战国", -280, 82, true, crate);
            M(cards, "qin_xin_ling_jun", "信陵君", "魏", "战国", -275, 84, false, crate);
            M(cards, "qin_mengchang", "孟尝君", "齐", "战国", -280, 80, false, crate);
            M(cards, "qin_pingyuan", "平原君", "赵", "战国", -260, 70, false, crate);
            M(cards, "qin_chunshen", "春申君", "楚", "战国", -250, 73, false, crate);
            M(cards, "qin_mengzi", "孟子", "邹", "战国", -350, 91, false, crate);
            M(cards, "qin_xunzi", "荀子", "赵", "战国", -310, 84, false, crate);
            M(cards, "qin_mozi", "墨子", "宋", "战国", -470, 85, false, crate);
            M(cards, "qin_zhuangzi", "庄子", "宋", "战国", -369, 86, false, crate);
            M(cards, "qin_hanfeizi", "韩非子", "韩", "战国", -280, 88, false, crate);
            M(cards, "qin_shen_buhai", "申不害", "韩", "战国", -400, 75, false, crate);
            M(cards, "qin_shen_dao", "慎到", "赵", "战国", -350, 70, false, crate);
            M(cards, "qin_qu_yuan", "屈原", "楚", "战国", -300, 89, false, crate);
            M(cards, "qin_zou_yang", "邹阳", "齐", "战国", -300, 68, false, crate);
            M(cards, "qin_mao_sui", "毛遂", "赵", "战国", -300, 69, false, crate);
            M(cards, "qin_gongsun_long", "公孙龙", "赵", "战国", -300, 67, false, crate);
            M(cards, "qin_su_dai", "苏代", "燕", "战国", -300, 66, false, crate);
            M(cards, "qin_yan_sui", "燕遂", "赵", "战国", -280, 62, false, crate);
            M(cards, "qin_kongzi", "孔子", "鲁", "春秋", -500, 96, false, crate,
                pBiography: "孔子整理六经并创办私学，曾在鲁国参与政务，主张以礼、义和教育整顿社会秩序。周游列国的经历使其思想超越一国政治，后世儒学由此形成长期的制度与文化传统。");
            M(cards, "qin_laozi", "老子", "周", "春秋", -550, 93, false, crate,
                pBiography: "老子相传任周守藏室史，观察诸侯争战与礼制变迁，形成以道、无为和反强制为核心的思想。其著作《道德经》在战国以后持续影响政治哲学、宗教和养生传统。");
            M(cards, "qin_wuzi", "伍子胥", "吴", "春秋", -506, 88, true, crate,
                pBiography: "伍子胥因楚国政治迫害出奔吴国，辅佐阖闾夺取政权并参与攻楚，主持修筑阖闾城。夫差时期他坚持警惕越国，最终因政见冲突被迫自尽，成为春秋忠谏与复仇叙事的代表。");
            M(cards, "qin_fanli", "范蠡", "越", "春秋", -480, 90, false, crate,
                pBiography: "范蠡辅佐越王勾践完成复国与灭吴，主张长期忍辱、积蓄国力和审慎用兵。功成后离开越国经商，后世把他的经历视为政治谋略、功成身退和商业经营相结合的典型。");
            M(cards, "qin_likui", "李悝", "魏", "战国", -400, 86, false, crate,
                pBiography: "李悝主持魏国变法，推行尽地力、平籴和法经等制度，试图把农业产出、粮价调节和刑法秩序纳入国家治理。他的改革为战国各国的法治与行政竞争提供了重要先例。");
            M(cards, "qin_zouji", "邹忌", "齐", "战国", -350, 82, false, crate,
                pBiography: "邹忌以琴音和讽谏获得齐威王信任，借生活中的受蒙蔽比喻劝君广开言路。齐威王采纳后整顿朝政，邹忌的谏言成为战国政治中以身边事劝戒君主的经典案例。");
            M(cards, "qin_tianji", "田忌", "齐", "战国", -340, 83, true, crate,
                pBiography: "田忌是齐国重要将领，曾采用孙膑的策略在桂陵、马陵等战事中击败魏军。他与邹忌等人的政治关系也反映出战国军功、贵族和相国之间相互牵制的权力结构。");
            M(cards, "qin_le_yang", "乐羊", "魏", "战国", -400, 76, true, crate,
                pBiography: "乐羊奉魏文侯之命攻灭中山，长期承担远征与军粮压力，最终完成魏国北方扩张。魏文侯以中山之地封赏他，同时又以疑心考验其忠诚，体现战国君臣对军功的复杂态度。");
            M(cards, "qin_bai_gui", "白圭", "魏", "战国", -370, 77, false, crate,
                pBiography: "白圭在魏国经营水利与商业，重视根据年成调节粮价和储备，被后世视为早期经济思想的重要人物。他把治水经验、市场交换和国家粮食安全联系起来，体现战国社会的生产与商业变化。");
            M(cards, "qin_shangwen", "商文", "秦", "战国", -320, 64, false, crate,
                pBiography: "商文在秦国地方行政中参与户籍、粮赋和军功登记，推动变法制度向基层落实。虽然正史对其个人记载有限，但这类基层文吏是秦国将法令转化为国家动员能力的重要环节。");
        }

        private static void AddHan(List<HistoricalFigureCardDefinition> cards)
        {
            const string crate = "han";
            M(cards, "han_chen_ping", "陈平", "汉", "西汉", -202, 88, false, crate);
            M(cards, "han_cao_can", "曹参", "汉", "西汉", -195, 84, false, crate);
            M(cards, "han_zhou_bo", "周勃", "汉", "西汉", -195, 82, true, crate);
            M(cards, "han_guan_ying", "灌婴", "汉", "西汉", -202, 78, true, crate);
            M(cards, "han_fan_kuai", "樊哙", "汉", "西汉", -205, 77, true, crate);
            M(cards, "han_peng_yue", "彭越", "汉", "西汉", -205, 80, true, crate);
            M(cards, "han_ying_bu", "英布", "汉", "西汉", -205, 78, true, crate);
            M(cards, "han_wei_qing", "卫青", "汉", "西汉", -106, 98, true, crate);
            M(cards, "han_huo_qubing", "霍去病", "汉", "西汉", -121, 98, true, crate);
            M(cards, "han_li_guang", "李广", "汉", "西汉", -166, 87, true, crate);
            M(cards, "han_zhang_qian", "张骞", "汉", "西汉", -139, 83, false, crate);
            M(cards, "han_ban_gu", "班固", "汉", "东汉", 60, 84, false, crate);
            M(cards, "han_dong_zhongshu", "董仲舒", "汉", "西汉", -130, 86, false, crate);
            M(cards, "han_jia_yi", "贾谊", "汉", "西汉", -174, 82, false, crate);
            M(cards, "han_chao_cuo", "晁错", "汉", "西汉", -154, 81, false, crate);
            M(cards, "han_deng_yu", "邓禹", "汉", "东汉", 25, 84, true, crate);
            M(cards, "han_feng_yi", "冯异", "汉", "东汉", 30, 82, true, crate);
            M(cards, "han_ma_yuan", "马援", "汉", "东汉", 40, 86, true, crate);
            M(cards, "han_dou_xian", "窦宪", "汉", "东汉", 89, 76, true, crate);
            M(cards, "han_dou_wu", "窦武", "汉", "东汉", 160, 70, false, crate);
            M(cards, "han_yang_zhen", "杨震", "汉", "东汉", 110, 76, false, crate);
            M(cards, "han_cai_yong", "蔡邕", "汉", "东汉", 175, 78, false, crate);
            M(cards, "han_zhang_heng", "张衡", "汉", "东汉", 110, 87, false, crate);
            M(cards, "han_du_fu", "杜抚", "汉", "东汉", 80, 65, false, crate);
            M(cards, "han_huangfu_gui", "皇甫规", "汉", "东汉", 155, 72, true, crate);
            M(cards, "han_geng_yan", "耿弇", "汉", "东汉", 30, 80, true, crate);
            M(cards, "han_ren_shang", "任尚", "汉", "东汉", 90, 65, true, crate);
            M(cards, "han_chen_tang", "陈汤", "汉", "西汉", -36, 78, true, crate);
            M(cards, "han_gan_yanshou", "甘延寿", "汉", "西汉", -36, 73, true, crate);
            M(cards, "han_du_zhi", "杜诗", "汉", "东汉", 40, 74, false, crate);
            M(cards, "han_duan_jiong", "段颎", "汉", "东汉", 160, 73, true, crate);
            M(cards, "han_ban_yong", "班勇", "汉", "东汉", 120, 72, true, crate);
            M(cards, "han_li_ying", "李膺", "汉", "东汉", 165, 75, false, crate);
            M(cards, "han_yang_biao", "杨彪", "汉", "东汉", 190, 70, false, crate);
            M(cards, "han_huo_guang", "霍光", "汉", "西汉", -90, 96, false, crate,
                pBiography: "霍光在汉武帝晚年受托辅政，联合金日磾、上官桀等人稳定朝局，并拥立昭帝。其执政时期减轻赋役、恢复民生，但家族权势最终在宣帝即位后被清算。");
            M(cards, "han_sang_hongyang", "桑弘羊", "汉", "西汉", -120, 88, false, crate,
                pBiography: "桑弘羊主持盐铁、均输和平准等财政政策，为汉武帝的边疆战争提供资源。他主张国家经营关键商品，盐铁会议中的争论则留下国家干预与民间生计之间的长期问题。");
            M(cards, "han_zhao_chongguo", "赵充国", "汉", "西汉", -80, 89, true, crate,
                pBiography: "赵充国长期经营西羌地区，主张屯田、安抚与军事打击相结合，反对只靠远征消耗国力。他的奏议保存了东汉以前边疆治理中军粮、移民和部落关系的实际经验。");
            M(cards, "han_wang_mang", "王莽", "新", "西汉末", 10, 86, false, crate,
                pBiography: "王莽以外戚身份掌握西汉中枢，建立新朝后推行王田、币制和官制改革，试图用古制重塑社会。改革执行困难、灾荒与战争并发，最终引发赤眉和绿林等反抗而失败。");
            M(cards, "han_wang_ba", "王霸", "汉", "东汉", 30, 82, true, crate,
                pBiography: "王霸追随刘秀平定河北和关中，长期负责地方军政与边防，善于在战乱后恢复郡县秩序。他的经历代表东汉开国将领从征战转入地方治理的过程。");
            M(cards, "han_zhu_fu", "朱浮", "汉", "东汉", 35, 70, false, crate,
                pBiography: "朱浮任幽州牧时参与东汉北方军政和州郡整顿，曾上疏讨论地方长吏、军镇与中央之间的责任。他与彭宠的冲突也反映开国时期地方权力和中央任命之间的紧张。");
            M(cards, "han_guo_kui", "郭躬", "汉", "东汉", 80, 74, false, crate,
                pBiography: "郭躬担任廷尉时重视律令解释和疑狱复核，反对以苛酷刑罚替代证据审理。他推动东汉司法实践更重视个案平反，是汉代法律官僚传统中的重要人物。");
            M(cards, "han_li_gu", "李固", "汉", "东汉", 140, 78, false, crate,
                pBiography: "李固在外戚、宦官和皇位继承冲突中多次进谏，主张限制权臣并整顿朝纲。他在梁冀斗争中失败被害，显示东汉士大夫试图以名节和公议约束宫廷权力的困境。");
            M(cards, "han_du_shi", "杜诗", "汉", "东汉", 40, 75, false, crate,
                pBiography: "杜诗任南阳太守时兴修水利、推广水排并改善冶铁生产，重视以技术和公共工程减轻民间负担。他的治理实践说明东汉地方官不仅负责刑名赋税，也参与生产技术推广。");
            M(cards, "han_li_zhang", "李章", "汉", "东汉", 70, 63, true, crate,
                pBiography: "李章曾参与东汉边郡军务，负责骑兵调度、烽燧联络和军粮转运。其事迹虽不如名将显赫，却反映边疆官员维持汉帝国日常防线所承担的具体职责。");
        }

        private static void AddThreeSix(List<HistoricalFigureCardDefinition> cards)
        {
            const string crate = "three_six_dynasties";
            M(cards, "three_xun_yu", "荀彧", "魏", "三国", 200, 92, false, crate);
            M(cards, "three_xun_you", "荀攸", "魏", "三国", 205, 84, false, crate);
            M(cards, "three_guo_jia", "郭嘉", "魏", "三国", 207, 89, false, crate);
            M(cards, "three_jia_xu", "贾诩", "魏", "三国", 210, 90, false, crate);
            M(cards, "three_cheng_yu", "程昱", "魏", "三国", 210, 81, false, crate);
            M(cards, "three_cao_cao", "曹操", "魏", "三国", 155, 100, true, crate,
                pFatherDisplayName: "曹嵩", pMotherDisplayName: "不详",
                pBiography: "东汉末年政治家、军事家和魏国奠基者，统一北方并为曹魏政权建立制度与军事基础。");
            M(cards, "three_sima_yi", "司马懿", "魏", "三国", 234, 98, true, crate);
            M(cards, "three_zhang_liao", "张辽", "魏", "三国", 215, 91, true, crate);
            M(cards, "three_deng_ai", "邓艾", "魏", "三国", 255, 88, true, crate);
            M(cards, "three_zhuge_liang", "诸葛亮", "蜀汉", "三国", 220, 98, false, crate);
            M(cards, "three_pang_tong", "庞统", "蜀汉", "三国", 215, 86, false, crate);
            M(cards, "three_fa_zheng", "法正", "蜀汉", "三国", 215, 82, false, crate);
            M(cards, "three_zhao_yun", "赵云", "蜀汉", "三国", 225, 92, true, crate);
            M(cards, "three_jiang_wei", "姜维", "蜀汉", "三国", 250, 86, true, crate);
            M(cards, "three_zhou_yu", "周瑜", "吴", "三国", 208, 98, true, crate);
            M(cards, "three_lu_su", "鲁肃", "吴", "三国", 215, 84, false, crate);
            M(cards, "three_lu_meng", "吕蒙", "吴", "三国", 220, 87, true, crate);
            M(cards, "three_lu_xun", "陆逊", "吴", "三国", 230, 98, true, crate);
            M(cards, "three_wang_dao", "王导", "晋", "东晋", 320, 83, false, crate);
            M(cards, "three_xie_an", "谢安", "晋", "东晋", 370, 90, false, crate);
            M(cards, "three_zu_ti", "祖逖", "晋", "东晋", 315, 82, true, crate);
            M(cards, "three_sima_zhao", "司马昭", "魏", "三国", 255, 84, true, crate);
            M(cards, "three_chen_shou", "陈寿", "晋", "西晋", 280, 80, false, crate);
            M(cards, "three_pei_xiu", "裴秀", "晋", "西晋", 260, 78, false, crate);
            M(cards, "three_wang_meng", "王猛", "秦", "十六国", 360, 90, false, crate);
            M(cards, "three_murong_ke", "慕容恪", "燕", "十六国", 350, 85, true, crate);
            M(cards, "three_huan_wen", "桓温", "晋", "东晋", 350, 78, true, crate);
            M(cards, "three_tao_kan", "陶侃", "晋", "东晋", 320, 82, true, crate);
            M(cards, "three_liu_kun", "刘琨", "晋", "西晋", 310, 77, true, crate);
            M(cards, "three_zu_chongzhi", "祖冲之", "宋", "南朝", 470, 85, false, crate);
            M(cards, "three_shen_yue", "沈约", "梁", "南朝", 500, 79, false, crate);
            M(cards, "three_cui_hao", "崔浩", "魏", "北朝", 430, 83, false, crate);
            M(cards, "three_gao_huan", "高欢", "齐", "北朝", 530, 84, true, crate);
            M(cards, "three_yuwen_tai", "宇文泰", "周", "北朝", 540, 86, true, crate);
            M(cards, "three_chen_qingzhi", "陈庆之", "梁", "南朝", 525, 88, true, crate);
            M(cards, "three_hou_jing", "侯景", "梁", "南朝", 545, 74, true, crate);
            M(cards, "three_wang_sengbian", "王僧辩", "梁", "南朝", 550, 76, true, crate);
            M(cards, "three_ren_fang", "任昉", "梁", "南朝", 500, 73, false, crate);
            M(cards, "three_xiao_ziyun", "萧子云", "梁", "南朝", 520, 69, false, crate);
            M(cards, "three_yuan_zan", "元赞", "魏", "北朝", 480, 67, false, crate);
            M(cards, "three_wu_mingche", "吴明彻", "陈", "南朝", 560, 65, true, crate);
            M(cards, "three_chen_qun", "陈群", "魏", "三国", 210, 87, false, crate,
                pBiography: "陈群在曹魏参与选官制度建设，提出九品中正制以评定士人门第与才能。制度最初用于稳定地方人才供给，后来却加强了士族垄断，对魏晋南北朝政治结构影响深远。");
            M(cards, "three_xu_shu", "徐庶", "蜀汉", "三国", 205, 84, false, crate,
                pBiography: "徐庶早年在荆州结交刘备，向其推荐诸葛亮，后因母亲被曹操控制而转投曹魏。他的经历连接刘备集团的人才网络与曹魏的政治吸纳，也成为三国人物选择与家属牵制的典型。");
            M(cards, "three_man_chong", "满宠", "魏", "三国", 220, 83, true, crate,
                pBiography: "满宠长期镇守淮南和合肥，主持魏吴边境防务，善于修城、整军和利用水陆地形。他在孙权多次进攻中保持防线，体现曹魏南线将领以据守和后勤取胜的特点。");
            M(cards, "three_lu_dai", "吕岱", "吴", "三国", 230, 78, true, crate,
                pBiography: "吕岱参与平定交州与岭南地方势力，长期负责吴国南方军政和交通。他兼顾征讨、安抚与地方行政，使江东政权能够把影响力延伸到南方边疆。");
            M(cards, "three_wang_jun", "王濬", "晋", "西晋", 280, 88, true, crate,
                pBiography: "王濬奉晋武帝之命建造大船、训练水军，沿长江顺流攻灭东吴，完成西晋统一。他把造船、江河运输和军事突袭结合起来，显示统一战争对水军技术的高度依赖。");
            M(cards, "three_du_yu", "杜预", "晋", "西晋", 280, 91, false, crate,
                pBiography: "杜预参与灭吴战争并在荆州建立军政秩序，精通律学和《左传》，推动晋代法律与经学整理。他主张先积蓄力量再出兵，体现西晋统一战略中财政、军粮和地方治理的结合。");
            M(cards, "three_xie_xuan", "谢玄", "晋", "东晋", 380, 90, true, crate,
                pBiography: "谢玄组建北府兵，在淝水之战中率军击败前秦主力，保护东晋江南政权。他善于选拔刘牢之等将领并整合流民军队，北府兵后来成为东晋军政格局的核心力量。");
            M(cards, "three_liu_yu", "刘裕", "宋", "南朝", 410, 95, true, crate,
                pBiography: "刘裕从北府兵将领起兵，先后平定桓玄、南燕和后秦，扩大东晋疆域并最终建立刘宋。他依靠军功重建中央权力，同时开启南朝以寒门武人取代门阀主导的政治转型。");
            M(cards, "three_tao_hongjing", "陶弘景", "梁", "南朝", 500, 80, false, crate,
                pBiography: "陶弘景隐居茅山，整理本草、道教典籍与炼养知识，梁武帝多次向其咨询军国和养生事务。其学术横跨医学、宗教和自然知识，体现南朝士人超越单一官职的知识网络。");
            M(cards, "three_xiao_daocheng", "萧道成", "齐", "南朝", 470, 86, true, crate,
                pBiography: "萧道成以南朝宋将领身份掌握禁军和朝政，最终受禅建立南齐。他在政权更替中整顿军队、控制京口与建康，反映南朝皇位转换往往依赖核心将领的兵权。");
        }

        private static void AddSuiTang(List<HistoricalFigureCardDefinition> cards)
        {
            const string crate = "sui_tang";
            M(cards, "sui_gao_jiong", "高颎", "隋", "隋", 590, 87, false, crate);
            M(cards, "sui_yang_su", "杨素", "隋", "隋", 590, 89, true, crate);
            M(cards, "sui_changsun_wuji", "长孙无忌", "唐", "唐", 630, 90, false, crate);
            M(cards, "sui_fang_xuanling", "房玄龄", "唐", "唐", 630, 98, false, crate);
            M(cards, "sui_du_ruhui", "杜如晦", "唐", "唐", 630, 91, false, crate);
            M(cards, "sui_wei_zheng", "魏征", "唐", "唐", 630, 98, false, crate);
            M(cards, "sui_li_jing", "李靖", "唐", "唐", 630, 98, true, crate);
            M(cards, "sui_qin_qiong", "秦琼", "唐", "唐", 630, 84, true, crate);
            M(cards, "sui_yuchi_jingde", "尉迟敬德", "唐", "唐", 630, 84, true, crate);
            M(cards, "sui_hou_junji", "侯君集", "唐", "唐", 640, 78, true, crate);
            M(cards, "sui_xu_shiji", "徐世勣", "唐", "唐", 640, 88, true, crate);
            M(cards, "sui_di_renjie", "狄仁杰", "唐", "唐", 690, 92, false, crate);
            M(cards, "sui_yao_chong", "姚崇", "唐", "唐", 710, 87, false, crate);
            M(cards, "sui_song_jing", "宋璟", "唐", "唐", 710, 86, false, crate);
            M(cards, "sui_zhang_jiuling", "张九龄", "唐", "唐", 730, 84, false, crate);
            M(cards, "sui_guo_ziyi", "郭子仪", "唐", "唐", 760, 98, true, crate);
            M(cards, "sui_li_guangbi", "李光弼", "唐", "唐", 760, 88, true, crate);
            M(cards, "sui_yan_zhenqing", "颜真卿", "唐", "唐", 760, 90, false, crate);
            M(cards, "sui_li_mi", "李泌", "唐", "唐", 780, 82, false, crate);
            M(cards, "sui_pei_du", "裴度", "唐", "唐", 820, 80, false, crate);
            M(cards, "sui_yuwen_kai", "宇文恺", "隋", "隋", 600, 75, false, crate);
            M(cards, "sui_pei_ju", "裴矩", "隋", "隋", 610, 76, false, crate);
            M(cards, "sui_su_wei", "苏威", "隋", "隋", 590, 72, false, crate);
            M(cards, "sui_li_shiji", "李世勣", "唐", "唐", 640, 88, true, crate);
            M(cards, "sui_cheng_yaojin", "程咬金", "唐", "唐", 630, 83, true, crate);
            M(cards, "sui_xue_rengui", "薛仁贵", "唐", "唐", 660, 87, true, crate);
            M(cards, "sui_pei_xingjian", "裴行俭", "唐", "唐", 680, 82, true, crate);
            M(cards, "sui_zhang_renyuan", "张仁愿", "唐", "唐", 700, 78, true, crate);
            M(cards, "sui_zhang_yue", "张说", "唐", "唐", 710, 76, false, crate);
            M(cards, "sui_lu_huaishen", "卢怀慎", "唐", "唐", 710, 70, false, crate);
            M(cards, "sui_li_linfu", "李林甫", "唐", "唐", 740, 73, false, crate);
            M(cards, "sui_yang_guozhong", "杨国忠", "唐", "唐", 750, 68, false, crate);
            M(cards, "sui_an_lushan", "安禄山", "唐", "唐", 750, 76, true, crate);
            M(cards, "sui_bai_juyi", "白居易", "唐", "唐", 820, 86, false, crate);
            M(cards, "sui_han_yu", "韩愈", "唐", "唐", 820, 85, false, crate);
            M(cards, "sui_liu_zongyuan", "柳宗元", "唐", "唐", 810, 81, false, crate);
            M(cards, "sui_du_fu", "杜甫", "唐", "唐", 760, 91, false, crate);
            M(cards, "sui_xue_song", "薛嵩", "唐", "唐", 780, 69, true, crate);
            M(cards, "sui_li_keyong", "李克用", "唐", "唐", 900, 79, true, crate);
            M(cards, "sui_du_you", "杜佑", "唐", "唐", 800, 74, false, crate);
            M(cards, "sui_zhang_jian", "张俭", "唐", "唐", 650, 79, false, crate,
                pBiography: "张俭在唐太宗时期任地方与边防官员，重视安置流民、修筑城防和恢复生产。他以谨慎清廉获得信任，代表贞观时期将行政、治安和边疆经营结合的地方官传统。");
            M(cards, "sui_zhang_jianzhi", "张柬之", "唐", "唐", 705, 90, false, crate,
                pBiography: "张柬之联合敬晖等大臣发动神龙政变，迫使武则天传位给中宗，恢复李唐皇统。他此前长期任职地方和中枢，政变后因功受封却很快遭到权力斗争排挤。");
            M(cards, "sui_gao_shilian", "高士廉", "唐", "唐", 630, 87, false, crate,
                pBiography: "高士廉是长孙皇后兄长，参与唐初政权建设和贵族秩序安排，主持修订氏族志。他既具外戚身份又有行政经验，在贞观政治中承担连接皇室、士族和官僚的作用。");
            M(cards, "sui_zhang_sun_wuji", "长孙顺德", "唐", "唐", 620, 76, true, crate,
                pBiography: "长孙顺德参加唐初统一战争和玄武门之变，后来担任重要军政职务。他因贪赃受到惩处，唐太宗仍以功劳处理，体现开国功臣纪律与皇权信任之间的张力。");
            M(cards, "sui_han_xiu", "韩休", "唐", "唐", 730, 82, false, crate,
                pBiography: "韩休任唐玄宗宰相时敢于直谏，指出财政、宫廷和地方治理中的问题。他的进谏使玄宗有所收敛，却也因与张说等人的政治关系而多次进退，反映开元中枢的党争。");
            M(cards, "sui_zhang_yi", "张易之", "周", "武周", 700, 68, false, crate,
                pBiography: "张易之兄弟凭借文学和宫廷关系受到武则天宠信，参与内廷文书与政治活动。神龙政变中二人被杀，其兴衰说明武周晚期内廷近臣与外朝宰相之间的权力冲突。");
            M(cards, "sui_li_deyu", "李德裕", "唐", "唐", 830, 86, false, crate,
                pBiography: "李德裕在唐武宗时期主持削弱藩镇、平定泽潞并处理回鹘问题，是牛李党争中的重要政治家。他重视边防与中枢权力，晚年因宣宗即位后的政治清算被贬死崖州。");
            M(cards, "sui_li_jiang", "李绛", "唐", "唐", 805, 78, false, crate,
                pBiography: "李绛任宰相时多次劝谏唐宪宗，主张节制宦官、整顿藩镇并减轻财政压力。他的政策受到权力结构限制，但留下了中唐士大夫试图恢复外朝治理的鲜明记录。");
            M(cards, "sui_li_shuo", "李硕", "唐", "唐", 800, 70, true, crate,
                pBiography: "李硕参与唐代河朔与西北军务，负责军镇之间的联络和粮道保护。他的职任体现中唐国家在藩镇林立的环境下，需要依靠熟悉地方军政的将领维持边境秩序。");
            M(cards, "sui_liu_yan", "刘晏", "唐", "唐", 760, 94, false, crate,
                pBiography: "刘晏主持唐代漕运、盐政和财政重建，改革转运制度以保障长安粮食供应。他善于利用市场价格和地方仓储调节国家财赋，是安史之乱后恢复唐朝财政能力的关键人物。");
        }

        private static void AddFiveSong(List<HistoricalFigureCardDefinition> cards)
        {
            const string crate = "five_song";
            M(cards, "song_zhao_pu", "赵普", "宋", "北宋", 960, 89, false, crate);
            M(cards, "song_kou_zhun", "寇准", "宋", "北宋", 1005, 85, false, crate);
            M(cards, "song_fan_zhongyan", "范仲淹", "宋", "北宋", 1040, 94, false, crate);
            M(cards, "song_han_qi", "韩琦", "宋", "北宋", 1050, 80, false, crate);
            M(cards, "song_fu_bi", "富弼", "宋", "北宋", 1050, 78, false, crate);
            M(cards, "song_wang_anshi", "王安石", "宋", "北宋", 1070, 94, false, crate);
            M(cards, "song_sima_guang", "司马光", "宋", "北宋", 1070, 92, false, crate);
            M(cards, "song_su_shi", "苏轼", "宋", "北宋", 1080, 94, false, crate);
            M(cards, "song_wen_yanbo", "文彦博", "宋", "北宋", 1050, 77, false, crate);
            M(cards, "song_bao_zheng", "包拯", "宋", "北宋", 1050, 91, false, crate);
            M(cards, "song_yue_fei", "岳飞", "宋", "南宋", 1130, 98, true, crate);
            M(cards, "song_han_shizhong", "韩世忠", "宋", "南宋", 1130, 86, true, crate);
            M(cards, "song_zong_ze", "宗泽", "宋", "南宋", 1125, 84, true, crate);
            M(cards, "song_li_gang", "李纲", "宋", "北宋", 1120, 83, false, crate);
            M(cards, "song_di_qing", "狄青", "宋", "北宋", 1045, 86, true, crate);
            M(cards, "song_yu_yunwen", "虞允文", "宋", "南宋", 1160, 80, false, crate);
            M(cards, "song_xin_qiji", "辛弃疾", "宋", "南宋", 1170, 92, true, crate);
            M(cards, "song_yelu_xiuge", "耶律休哥", "辽", "辽", 990, 82, true, crate);
            M(cards, "song_han_derang", "韩德让", "辽", "辽", 1000, 81, false, crate);
            M(cards, "song_wanyan_zongbi", "完颜宗弼", "金", "金", 1130, 87, true, crate);
            M(cards, "song_li_chuyun", "李处耘", "宋", "北宋", 960, 74, true, crate);
            M(cards, "song_shi_shouxin", "石守信", "宋", "北宋", 960, 76, true, crate);
            M(cards, "song_pan_mei", "潘美", "宋", "北宋", 970, 82, true, crate);
            M(cards, "song_cao_bin", "曹彬", "宋", "北宋", 970, 86, true, crate);
            M(cards, "song_yang_ye", "杨业", "宋", "北宋", 980, 88, true, crate);
            M(cards, "song_yang_yanzhao", "杨延昭", "宋", "北宋", 1000, 85, true, crate);
            M(cards, "song_ouyang_xiu", "欧阳修", "宋", "北宋", 1050, 88, false, crate);
            M(cards, "song_shen_kuo", "沈括", "宋", "北宋", 1080, 86, false, crate);
            M(cards, "song_wen_tianxiang", "文天祥", "宋", "南宋", 1270, 98, false, crate);
            M(cards, "song_lu_you", "陆游", "宋", "南宋", 1180, 83, false, crate);
            M(cards, "song_qin_hui", "秦桧", "宋", "南宋", 1140, 70, false, crate);
            M(cards, "song_han_tuozhou", "韩侂胄", "宋", "南宋", 1200, 68, false, crate);
            M(cards, "song_jia_sidao", "贾似道", "宋", "南宋", 1260, 67, false, crate);
            M(cards, "song_meng_gong", "孟珙", "宋", "南宋", 1230, 82, true, crate);
            M(cards, "song_wu_jie", "吴玠", "宋", "南宋", 1130, 80, true, crate);
            M(cards, "song_liu_qi", "刘锜", "宋", "南宋", 1140, 81, true, crate);
            M(cards, "song_zhang_jun", "张浚", "宋", "南宋", 1140, 74, false, crate);
            M(cards, "song_yao_lin", "姚麟", "宋", "南宋", 1160, 67, true, crate);
            M(cards, "song_wang_dan", "王旦", "宋", "北宋", 1010, 75, false, crate);
            M(cards, "song_chen_liang", "陈亮", "宋", "南宋", 1190, 74, false, crate);
            M(cards, "song_li_hang", "李沆", "宋", "北宋", 1000, 84, false, crate,
                pBiography: "李沆任宋真宗宰相时重视谨慎用事和稳定财政，反对轻率扩大边事。他的治政风格为北宋初年皇权、文官和军队之间建立了较稳固的日常秩序。");
            M(cards, "song_cao_liyong", "曹利用", "宋", "北宋", 1005, 80, false, crate,
                pBiography: "曹利用代表宋真宗参与澶渊之盟谈判，负责在辽宋战争压力下确定岁币与边境安排。和议暂时稳定北方局势，但他后来因宫廷斗争失势，最终被贬途中自尽。");
            M(cards, "song_wang_qinruo", "王钦若", "宋", "北宋", 1005, 76, false, crate,
                pBiography: "王钦若长期任宋真宗近臣，参与封禅、祥瑞和宫廷文书活动，后来主持地方与中央政务。他善于迎合皇帝意志，在寇准等人的政治竞争中成为北宋真宗朝党争的重要角色。");
            M(cards, "song_zhang_dun", "章惇", "宋", "北宋", 1090, 82, false, crate,
                pBiography: "章惇支持王安石变法，主持西南开边和地方行政，曾在荆湖、夔州等地处理边疆事务。哲宗亲政后任宰相，继续推行绍述政策，成为北宋新旧党争的强硬人物。");
            M(cards, "song_lv_huiqing", "吕惠卿", "宋", "北宋", 1070, 78, false, crate,
                pBiography: "吕惠卿参与熙宁变法，协助推行青苗、免役和财政改革，曾与王安石关系密切。两人后来因政治分歧决裂，吕惠卿在党争中屡起屡落，体现改革集团内部的权力变化。");
            M(cards, "song_zhang_xian", "张宪", "宋", "南宋", 1130, 77, true, crate,
                pBiography: "张宪是岳家军重要将领，参与襄阳、郾城等战役并承担前线统兵与防守。他与岳飞关系密切，岳飞被害后同遭牵连，反映南宋军队在政治清算中的脆弱处境。");
            M(cards, "song_liu_zhang", "刘锜", "宋", "南宋", 1140, 84, true, crate,
                pBiography: "刘锜在顺昌等战役中以少量兵力击退金军，善于利用城防、士气和地形作战。他在南宋主战与议和之间多次进退，晚年仍承担淮河防务，是宋金战争中的重要将领。");
            M(cards, "song_wen_tianxiang2", "陆秀夫", "宋", "南宋", 1279, 88, false, crate,
                pBiography: "陆秀夫在南宋末年辅佐幼帝辗转海上，崖山败局后背负幼帝投海殉国。他主持流亡政权的文书与军政事务，成为南宋灭亡时坚持政权名义和忠节叙事的代表。");
            M(cards, "song_xie_fangshu", "谢枋得", "宋", "南宋", 1270, 79, false, crate,
                pBiography: "谢枋得参与南宋末年抗元，战败后隐居并拒绝出仕元朝。他以文章、气节和地方组织活动保存遗民传统，反映宋元易代后士人的身份选择和文化抵抗。");
            M(cards, "song_ye_shi", "叶适", "宋", "南宋", 1200, 77, false, crate,
                pBiography: "叶适重视功利、财政和军政实际，批评空疏议论，曾为南宋边防与人才政策提出建议。他的永嘉学派强调经世致用，反映南宋商品经济与国家危机下思想界的转向。");
        }

        private static void AddYuanMingQing(List<HistoricalFigureCardDefinition> cards)
        {
            const string crate = "yuan_ming_qing";
            M(cards, "yuan_yelu_chucai", "耶律楚材", "元", "元", 1220, 90, false, crate);
            M(cards, "yuan_shi_tianze", "史天泽", "元", "元", 1250, 78, true, crate);
            M(cards, "yuan_bayan", "伯颜", "元", "元", 1270, 85, true, crate);
            M(cards, "yuan_tuotuo", "脱脱", "元", "元", 1340, 82, false, crate);
            M(cards, "ming_liu_bowen", "刘伯温", "明", "明", 1368, 98, false, crate);
            M(cards, "ming_li_shanchang", "李善长", "明", "明", 1368, 84, false, crate);
            M(cards, "ming_xu_da", "徐达", "明", "明", 1368, 98, true, crate);
            M(cards, "ming_chang_yuchun", "常遇春", "明", "明", 1368, 88, true, crate);
            M(cards, "ming_lan_yu", "蓝玉", "明", "明", 1380, 82, true, crate);
            M(cards, "ming_yu_qian", "于谦", "明", "明", 1449, 98, false, crate);
            M(cards, "ming_wang_yangming", "王阳明", "明", "明", 1510, 94, false, crate);
            M(cards, "ming_zhang_juzheng", "张居正", "明", "明", 1570, 98, false, crate);
            M(cards, "ming_qi_jiguang", "戚继光", "明", "明", 1560, 98, true, crate);
            M(cards, "ming_yu_dayou", "俞大猷", "明", "明", 1560, 84, true, crate);
            M(cards, "ming_yuan_chonghuan", "袁崇焕", "明", "明", 1625, 86, true, crate);
            M(cards, "ming_sun_chuanzing", "孙传庭", "明", "明", 1635, 78, true, crate);
            M(cards, "ming_hong_chengchou", "洪承畴", "明", "明", 1635, 76, true, crate);
            M(cards, "qing_dorgon", "多尔衮", "清", "清", 1644, 90, true, crate);
            M(cards, "qing_fan_wencheng", "范文程", "清", "清", 1644, 82, false, crate);
            M(cards, "qing_zeng_guofan", "曾国藩", "清", "清", 1860, 92, true, crate);
            M(cards, "qing_li_hongzhang", "李鸿章", "清", "清", 1870, 88, false, crate);
            M(cards, "qing_zuo_zongtang", "左宗棠", "清", "清", 1870, 98, true, crate);
            M(cards, "yuan_muqali", "木华黎", "元", "元", 1215, 87, true, crate);
            M(cards, "yuan_guo_shoujing", "郭守敬", "元", "元", 1280, 84, false, crate);
            M(cards, "yuan_zhao_mengfu", "赵孟頫", "元", "元", 1290, 80, false, crate);
            M(cards, "ming_yao_guangxiao", "姚广孝", "明", "明", 1400, 83, false, crate);
            M(cards, "ming_zheng_he", "郑和", "明", "明", 1410, 86, true, crate);
            M(cards, "ming_hai_rui", "海瑞", "明", "明", 1560, 87, false, crate);
            M(cards, "ming_li_shizhen", "李时珍", "明", "明", 1580, 82, false, crate);
            M(cards, "ming_tang_xianzu", "汤显祖", "明", "明", 1590, 78, false, crate);
            M(cards, "ming_li_dingguo", "李定国", "明", "明", 1650, 83, true, crate);
            M(cards, "ming_shi_kefa", "史可法", "明", "明", 1645, 86, false, crate);
            M(cards, "ming_wu_sangui", "吴三桂", "明", "明", 1644, 75, true, crate);
            M(cards, "qing_zhang_tingyu", "张廷玉", "清", "清", 1730, 78, false, crate);
            M(cards, "qing_zhang_zhidong", "张之洞", "清", "清", 1890, 76, false, crate);
            M(cards, "qing_shen_baozhen", "沈葆桢", "清", "清", 1870, 72, false, crate);
            M(cards, "qing_lin_zexu", "林则徐", "清", "清", 1840, 88, false, crate);
            M(cards, "qing_gong_zizhen", "龚自珍", "清", "清", 1830, 73, false, crate);
            M(cards, "qing_zhang_xun", "张勋", "清", "清", 1910, 65, true, crate);
            M(cards, "qing_liang_qichao", "梁启超", "清", "清", 1900, 82, false, crate);
            M(cards, "yuan_hao_jing", "郝经", "元", "元", 1255, 86, false, crate,
                pBiography: "郝经奉忽必烈之命出使南宋，长期被扣留仍坚持议和与统一主张，归元后参与典章和文书建设。他代表儒士在蒙古政权中推动以中原制度处理南北关系的努力。");
            M(cards, "yuan_zhang_hongfan", "张弘范", "元", "元", 1279, 85, true, crate,
                pBiography: "张弘范统率元军沿江南下，参与襄阳、临安和崖山战事，最终消灭南宋残余政权。他的军事成功推动元朝统一，但也使宋元易代中的忠节与征服记忆长期交织。");
            M(cards, "yuan_wang_baobao", "王保保", "元", "元", 1360, 88, true, crate,
                pBiography: "王保保在元末拥护扩廓帖木儿，长期与朱元璋和明军争夺北方。他善于骑兵机动和地方动员，北元失败后仍坚持抵抗，成为元明战争中最有影响的蒙古将领之一。");
            M(cards, "yuan_xu_heng", "许衡", "元", "元", 1260, 84, false, crate,
                pBiography: "许衡在元初主持经学教育和官僚培养，参与国子学制度建设，试图把儒学教育纳入多民族帝国的行政体系。他强调经世与自守，对元代儒学官学化影响深远。");
            M(cards, "yuan_liu_bingzhong", "刘秉忠", "元", "元", 1260, 92, false, crate,
                pBiography: "刘秉忠辅佐忽必烈规划大都城和元朝官制，参与确定国号、礼制与中书省体系。他把汉地文书传统与蒙古统治结构结合，为元朝从草原政权转向定都中原提供制度设计。");
            M(cards, "ming_yang_shiqi", "杨士奇", "明", "明", 1420, 86, false, crate,
                pBiography: "杨士奇在明仁宗、宣宗时期主持内阁，参与休养生息、减轻赋役和处理边务，形成仁宣之治的文官核心。他以谨慎持重著称，长期调和皇权、内阁和地方行政关系。");
            M(cards, "ming_xu_jie", "徐阶", "明", "明", 1560, 84, false, crate,
                pBiography: "徐阶在嘉靖末年联合高拱等人处理严嵩专权，主持内阁并调整财政与边务。他提拔张居正参与政务，晚年却因党争退出中枢，体现明代内阁政治的继承与反复。");
            M(cards, "ming_gu_yanwu", "顾炎武", "明", "明末", 1650, 88, false, crate,
                pBiography: "顾炎武经历明清易代后长期游历，考察山川、户籍、漕运和地方制度，主张经世致用并反思空谈。他以日知录等著作保存明遗民的学术与政治关怀，影响清代考据和史学。");
            M(cards, "qing_ji_yun", "纪昀", "清", "清", 1770, 82, false, crate,
                pBiography: "纪昀主持《四库全书》编纂和目录整理，参与清代文献分类、审校与禁毁制度。他以文学和考据才能服务乾隆朝，作品同时保留了清代知识管理和士人处世的复杂面貌。");
            M(cards, "qing_zeng_guozhi", "曾国荃", "清", "清", 1860, 78, true, crate,
                pBiography: "曾国荃率湘军参与攻克安庆、南京等太平天国据点，依靠地方团练和水陆运输推进战事。战后出任地方督抚，显示晚清军事集团如何转化为地方行政与财政力量。");
        }

        private static void M(List<HistoricalFigureCardDefinition> cards,
            string pId, string pName, string pKingdom, string pEra, int pYear,
            int pFame, bool pMilitary, string pCollection,
            string pFatherDisplayName = "", string pMotherDisplayName = "",
            string pBiography = "")
        {
            string family = FamilyName(pName);
            string given = pName.Substring(family.Length);
            string biography = HistoricalFigureCardNarratives.MinisterBiography(
                pId, pName, pKingdom, pEra, pMilitary, pBiography);
            string background = HistoricalFigureCardNarratives.MinisterBackground(
                pId, pName, pKingdom, pEra, pMilitary);
            string detail = HistoricalFigureCardNarratives.MinisterDetailed(
                pId, pName, pKingdom, pEra, pMilitary, pBiography);
            cards.Add(new HistoricalFigureCardDefinition(
                pId, pName, family, family, given, pEra, pKingdom, pEra,
                HistoricalFigureCardCatalog.UnknownYear,
                HistoricalFigureCardCatalog.UnknownYear, pYear, pFame,
                RarityForFame(pFame), HistoricalFigureSex.Male,
                pBiography: biography, pFatherCardId: "",
                pFatherDisplayName: pFatherDisplayName, pMotherCardId: "",
                pMotherDisplayName: pMotherDisplayName, pPortraitPath: "",
                pLegacyFigureId: "", pLegacyRegistryIndex: -1,
                pCombatHealth: pMilitary ? 1800 : 1300,
                pCombatTraits: pMilitary ? new[] { "warrior" } : Array.Empty<string>(),
                pBackgroundSummary: background,
                pDetailedBiography: detail,
                pRole: HistoricalFigureCardRole.Minister,
                pMinisterType: pMilitary
                    ? HistoricalFigureCardMinisterType.MilitaryGeneral
                    : HistoricalFigureCardMinisterType.CivilOfficial,
                pCollectionId: pCollection));
        }

        private static string FamilyName(string pName)
        {
            string[] compounds =
            {
                "司马", "诸葛", "欧阳", "长孙", "尉迟", "宇文", "慕容",
                "拓跋", "耶律", "完颜", "独孤", "上官"
            };
            return compounds.FirstOrDefault(pName.StartsWith) ??
                   pName.Substring(0, 1);
        }

        private static HistoricalFigureCardRarity RarityForFame(int pFame)
        {
            if (pFame >= 98) return HistoricalFigureCardRarity.Gold;
            if (pFame >= 90) return HistoricalFigureCardRarity.Red;
            if (pFame >= 75) return HistoricalFigureCardRarity.Pink;
            if (pFame >= 55) return HistoricalFigureCardRarity.Purple;
            return HistoricalFigureCardRarity.Blue;
        }
    }
}
