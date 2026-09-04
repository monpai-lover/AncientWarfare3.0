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
            return cards;
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
            M(cards, "qin_bai_qi", "白起", "秦", "战国", -260, 94, true, crate);
            M(cards, "qin_wang_jian", "王翦", "秦", "战国", -250, 91, true, crate);
            M(cards, "qin_wang_ben", "王贲", "秦", "战国", -230, 78, true, crate);
            M(cards, "qin_meng_tian", "蒙恬", "秦", "战国", -220, 88, true, crate);
            M(cards, "qin_li_si", "李斯", "秦", "战国", -280, 92, false, crate);
            M(cards, "qin_wei_liao", "尉缭", "秦", "战国", -250, 76, false, crate);
            M(cards, "qin_lu_buwei", "吕不韦", "秦", "战国", -260, 83, false, crate);
            M(cards, "qin_wu_qi", "吴起", "楚", "战国", -410, 89, true, crate);
            M(cards, "qin_sun_wu", "孙武", "吴", "春秋", -500, 95, true, crate);
            M(cards, "qin_sun_bin", "孙膑", "齐", "战国", -330, 86, true, crate);
            M(cards, "qin_lian_po", "廉颇", "赵", "战国", -327, 88, true, crate);
            M(cards, "qin_li_mu", "李牧", "赵", "战国", -245, 93, true, crate);
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
            M(cards, "han_wei_qing", "卫青", "汉", "西汉", -106, 91, true, crate);
            M(cards, "han_huo_qubing", "霍去病", "汉", "西汉", -121, 94, true, crate);
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
        }

        private static void AddThreeSix(List<HistoricalFigureCardDefinition> cards)
        {
            const string crate = "three_six_dynasties";
            M(cards, "three_xun_yu", "荀彧", "魏", "三国", 200, 92, false, crate);
            M(cards, "three_xun_you", "荀攸", "魏", "三国", 205, 84, false, crate);
            M(cards, "three_guo_jia", "郭嘉", "魏", "三国", 207, 89, false, crate);
            M(cards, "three_jia_xu", "贾诩", "魏", "三国", 210, 90, false, crate);
            M(cards, "three_cheng_yu", "程昱", "魏", "三国", 210, 81, false, crate);
            M(cards, "three_sima_yi", "司马懿", "魏", "三国", 234, 94, true, crate);
            M(cards, "three_zhang_liao", "张辽", "魏", "三国", 215, 91, true, crate);
            M(cards, "three_deng_ai", "邓艾", "魏", "三国", 255, 88, true, crate);
            M(cards, "three_zhuge_liang", "诸葛亮", "蜀汉", "三国", 220, 98, false, crate);
            M(cards, "three_pang_tong", "庞统", "蜀汉", "三国", 215, 86, false, crate);
            M(cards, "three_fa_zheng", "法正", "蜀汉", "三国", 215, 82, false, crate);
            M(cards, "three_zhao_yun", "赵云", "蜀汉", "三国", 225, 92, true, crate);
            M(cards, "three_jiang_wei", "姜维", "蜀汉", "三国", 250, 86, true, crate);
            M(cards, "three_zhou_yu", "周瑜", "吴", "三国", 208, 94, true, crate);
            M(cards, "three_lu_su", "鲁肃", "吴", "三国", 215, 84, false, crate);
            M(cards, "three_lu_meng", "吕蒙", "吴", "三国", 220, 87, true, crate);
            M(cards, "three_lu_xun", "陆逊", "吴", "三国", 230, 93, true, crate);
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
        }

        private static void AddSuiTang(List<HistoricalFigureCardDefinition> cards)
        {
            const string crate = "sui_tang";
            M(cards, "sui_gao_jiong", "高颎", "隋", "隋", 590, 87, false, crate);
            M(cards, "sui_yang_su", "杨素", "隋", "隋", 590, 89, true, crate);
            M(cards, "sui_changsun_wuji", "长孙无忌", "唐", "唐", 630, 90, false, crate);
            M(cards, "sui_fang_xuanling", "房玄龄", "唐", "唐", 630, 96, false, crate);
            M(cards, "sui_du_ruhui", "杜如晦", "唐", "唐", 630, 91, false, crate);
            M(cards, "sui_wei_zheng", "魏征", "唐", "唐", 630, 94, false, crate);
            M(cards, "sui_li_jing", "李靖", "唐", "唐", 630, 93, true, crate);
            M(cards, "sui_qin_qiong", "秦琼", "唐", "唐", 630, 84, true, crate);
            M(cards, "sui_yuchi_jingde", "尉迟敬德", "唐", "唐", 630, 84, true, crate);
            M(cards, "sui_hou_junji", "侯君集", "唐", "唐", 640, 78, true, crate);
            M(cards, "sui_xu_shiji", "徐世勣", "唐", "唐", 640, 88, true, crate);
            M(cards, "sui_di_renjie", "狄仁杰", "唐", "唐", 690, 92, false, crate);
            M(cards, "sui_yao_chong", "姚崇", "唐", "唐", 710, 87, false, crate);
            M(cards, "sui_song_jing", "宋璟", "唐", "唐", 710, 86, false, crate);
            M(cards, "sui_zhang_jiuling", "张九龄", "唐", "唐", 730, 84, false, crate);
            M(cards, "sui_guo_ziyi", "郭子仪", "唐", "唐", 760, 94, true, crate);
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
            M(cards, "song_yue_fei", "岳飞", "宋", "南宋", 1130, 96, true, crate);
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
            M(cards, "song_wen_tianxiang", "文天祥", "宋", "南宋", 1270, 95, false, crate);
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
        }

        private static void AddYuanMingQing(List<HistoricalFigureCardDefinition> cards)
        {
            const string crate = "yuan_ming_qing";
            M(cards, "yuan_yelu_chucai", "耶律楚材", "元", "元", 1220, 90, false, crate);
            M(cards, "yuan_shi_tianze", "史天泽", "元", "元", 1250, 78, true, crate);
            M(cards, "yuan_bayan", "伯颜", "元", "元", 1270, 85, true, crate);
            M(cards, "yuan_tuotuo", "脱脱", "元", "元", 1340, 82, false, crate);
            M(cards, "ming_liu_bowen", "刘伯温", "明", "明", 1368, 94, false, crate);
            M(cards, "ming_li_shanchang", "李善长", "明", "明", 1368, 84, false, crate);
            M(cards, "ming_xu_da", "徐达", "明", "明", 1368, 94, true, crate);
            M(cards, "ming_chang_yuchun", "常遇春", "明", "明", 1368, 88, true, crate);
            M(cards, "ming_lan_yu", "蓝玉", "明", "明", 1380, 82, true, crate);
            M(cards, "ming_yu_qian", "于谦", "明", "明", 1449, 96, false, crate);
            M(cards, "ming_wang_yangming", "王阳明", "明", "明", 1510, 94, false, crate);
            M(cards, "ming_zhang_juzheng", "张居正", "明", "明", 1570, 96, false, crate);
            M(cards, "ming_qi_jiguang", "戚继光", "明", "明", 1560, 94, true, crate);
            M(cards, "ming_yu_dayou", "俞大猷", "明", "明", 1560, 84, true, crate);
            M(cards, "ming_yuan_chonghuan", "袁崇焕", "明", "明", 1625, 86, true, crate);
            M(cards, "ming_sun_chuanzing", "孙传庭", "明", "明", 1635, 78, true, crate);
            M(cards, "ming_hong_chengchou", "洪承畴", "明", "明", 1635, 76, true, crate);
            M(cards, "qing_dorgon", "多尔衮", "清", "清", 1644, 90, true, crate);
            M(cards, "qing_fan_wencheng", "范文程", "清", "清", 1644, 82, false, crate);
            M(cards, "qing_zeng_guofan", "曾国藩", "清", "清", 1860, 92, true, crate);
            M(cards, "qing_li_hongzhang", "李鸿章", "清", "清", 1870, 88, false, crate);
            M(cards, "qing_zuo_zongtang", "左宗棠", "清", "清", 1870, 90, true, crate);
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
        }

        private static void M(List<HistoricalFigureCardDefinition> cards,
            string pId, string pName, string pKingdom, string pEra, int pYear,
            int pFame, bool pMilitary, string pCollection)
        {
            string family = FamilyName(pName);
            string given = pName.Substring(family.Length);
            string role = pMilitary ? "武将" : "文臣";
            string biography = pName + "是" + pEra + "时期" + pKingdom +
                "的重要" + role + "，其事迹见于相关正史与编年记载。";
            string background = pName + "活跃于" + pKingdom + "，属于" +
                pEra + "历史人物。";
            string detail = biography + "这张卡记录其历史身份、所属政权与主要活动时期，部署后按" +
                (pMilitary ? "军事官员" : "文官") + "处理。";
            cards.Add(new HistoricalFigureCardDefinition(
                pId, pName, family, family, given, pEra, pKingdom, pEra,
                HistoricalFigureCardCatalog.UnknownYear,
                HistoricalFigureCardCatalog.UnknownYear, pYear, pFame,
                RarityForFame(pFame), HistoricalFigureSex.Male,
                pBiography: biography, pFatherCardId: "",
                pFatherDisplayName: "", pMotherCardId: "",
                pMotherDisplayName: "", pPortraitPath: "",
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
