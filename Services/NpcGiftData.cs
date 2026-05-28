using LSMA.Models;

namespace LSMA.Services;

public static class NpcGiftData
{
    public static IReadOnlyList<NpcGiftRecord> All { get; } =
    [
        new() { NpcId = "Abigail", Npc = "阿比盖尔", Birthday = "秋季13", Loves = "紫水晶, 香蕉布丁, 黑莓馅饼, 巧克力蛋糕, 怪物图鉴, 河豚, 南瓜, 香辣鳗鱼", Likes = "石英", Hates = "黏土, 冬青", Neutral = "所有牛奶, 所有蘑菇(除红蘑菇)" },
        new() { NpcId = "Alex", Npc = "亚历克斯", Birthday = "夏季13", Loves = "完美早餐, 鲑鱼晚餐", Likes = "所有蛋", Hates = "冬青, 石英", Neutral = "所有水果, 所有牛奶, 所有蘑菇(除红蘑菇)" },
        new() { NpcId = "Caroline", Npc = "卡洛琳", Birthday = "冬季7", Loves = "鱼肉卷, 绿茶, 夏季亮片, 热带咖喱", Likes = "水仙花", Hates = "石英, 野莓", Neutral = "所有蛋, 所有水果, 所有牛奶" },
        new() { NpcId = "Clint", Npc = "克林特", Birthday = "冬季26", Loves = "紫水晶, 海蓝宝石, 绿宝石, 金锭, 铱锭, 翡翠, 万象晶球, 红宝石, 黄玉", Likes = "铜锭, 铁锭", Hates = "冬青", Neutral = "所有水果, 所有牛奶, 所有蘑菇(除红蘑菇)" },
        new() { NpcId = "Elliott", Npc = "艾利欧特", Birthday = "秋季5", Loves = "蟹饼, 鸭毛, 龙虾, 石榴, 鱿鱼墨汁, 冬阴功汤", Likes = "所有水果(除石榴), 章鱼, 鱿鱼", Hates = "石英, 野莓, 海参", Neutral = "所有蛋, 所有鱼, 彩虹贝壳, 海胆" },
        new() { NpcId = "Emily", Npc = "艾米丽", Birthday = "春季27", Loves = "紫水晶, 海蓝宝石, 布料, 绿宝石, 翡翠, 红宝石, 救生汉堡, 黄玉, 羊毛", Likes = "水仙花, 石英", Hates = "鱼肉卷, 冬青, 生鱼片, 鲑鱼晚餐", Neutral = "所有蛋, 所有水果, 所有牛奶, 所有蘑菇(除红蘑菇)" },
        new() { NpcId = "Haley", Npc = "海莉", Birthday = "春季14", Loves = "椰子, 水果沙拉, 粉红蛋糕, 向日葵", Likes = "水仙花", Hates = "所有鱼, 黏土, 五彩碎片, 辣根" },
        new() { NpcId = "Harvey", Npc = "哈维", Birthday = "冬季14", Loves = "咖啡, 腌菜, 超级餐, 松露油, 葡萄酒", Likes = "所有水果, 所有蘑菇(除红蘑菇)", Hates = "珊瑚, 冬青, 野莓, 辣根", Neutral = "所有蛋, 所有牛奶" },
        new() { NpcId = "Leah", Npc = "莉亚", Birthday = "冬季23", Loves = "山羊奶酪, 罂粟籽松饼, 沙拉, 炒菜, 松露, 蔬菜杂烩, 葡萄酒", Likes = "所有蛋, 所有水果, 所有牛奶, 所有蘑菇(除红蘑菇)", Hates = "面包, 煎饼, 披萨" },
        new() { NpcId = "Lewis", Npc = "刘易斯", Birthday = "春季7", Loves = "秋日恩赐, 琉璃山药, 绿茶, 辣椒, 蔬菜杂烩", Likes = "蓝莓, 椰子", Hates = "冬青, 石英", Neutral = "所有蛋, 所有水果, 所有蘑菇(除红蘑菇)" },
        new() { NpcId = "Linus", Npc = "莱纳斯", Birthday = "冬季3", Loves = "蓝莓馅饼, 仙人掌果, 椰子, 海之菜", Likes = "所有蛋, 所有水果, 所有牛奶, 所有蘑菇(除红蘑菇)", Hates = "", Neutral = "所有鱼" },
        new() { NpcId = "Marnie", Npc = "玛妮", Birthday = "秋季18", Loves = "钻石, 农夫午餐, 粉红蛋糕, 南瓜派", Likes = "所有蛋, 所有牛奶, 石英", Hates = "黏土, 冬青", Neutral = "所有水果, 所有蘑菇(除红蘑菇)" },
        new() { NpcId = "Maru", Npc = "玛鲁", Birthday = "夏季10", Loves = "电池, 花椰菜, 钻石, 金锭, 铱锭, 草莓", Likes = "铜锭, 铁锭, 橡树树脂, 松焦油, 石英", Hates = "冬青", Neutral = "所有蛋, 所有水果, 所有牛奶, 所有蘑菇(除红蘑菇)" },
        new() { NpcId = "Pam", Npc = "潘姆", Birthday = "春季18", Loves = "啤酒, 仙人掌果, 琉璃山药, 蜜酒, 淡啤酒, 防风草, 防风草汤", Likes = "所有水果(除仙人掌果), 所有牛奶, 水仙花", Hates = "冬青, 章鱼, 鱿鱼", Neutral = "所有鱼, 所有蘑菇(除红蘑菇)" },
        new() { NpcId = "Penny", Npc = "潘妮", Birthday = "秋季2", Loves = "所有书, 钻石, 绿宝石, 甜瓜, 罂粟, 红盘, 沙鱼, 冬阴功汤", Likes = "所有牛奶, 蒲公英, 韭葱", Hates = "啤酒, 葡萄, 冬青, 蜜酒", Neutral = "所有蛋, 所有水果, 所有蘑菇(除红蘑菇)" },
        new() { NpcId = "Robin", Npc = "罗宾", Birthday = "秋季21", Loves = "山羊奶酪, 桃子, 意面", Likes = "所有水果(除桃子), 所有牛奶, 硬木, 石英", Hates = "冬青", Neutral = "所有蛋, 所有蘑菇(除红蘑菇)" },
        new() { NpcId = "Sam", Npc = "山姆", Birthday = "夏季17", Loves = "仙人掌果, 枫糖棒, 披萨, 虎眼石", Likes = "所有蛋, 乔家可乐", Hates = "骨头碎片, 煤, 铜锭, 鸭蛋黄酱, 金锭, 铱锭, 铁锭, 蛋黄酱, 腌菜", Neutral = "所有水果(除仙人掌果), 所有牛奶" },
        new() { NpcId = "Sebastian", Npc = "塞巴斯蒂安", Birthday = "冬季10", Loves = "青蛙蛋, 泪晶, 黑曜石, 南瓜汤, 生鱼片, 虚空蛋", Likes = "比目鱼, 石英", Hates = "所有工匠品, 所有蛋(除虚空蛋), 黏土", Neutral = "所有鱼, 所有水果, 所有牛奶" },
        new() { NpcId = "Shane", Npc = "谢恩", Birthday = "春季20", Loves = "啤酒, 辣椒, 辣椒炸弹, 披萨", Likes = "所有蛋, 所有水果(除辣椒)", Hates = "腌菜, 石英", Neutral = "所有牛奶" },
        new() { NpcId = "Willy", Npc = "威利", Birthday = "夏季24", Loves = "鲶鱼, 钻石, 金锭, 铱锭, 蜜酒, 章鱼, 南瓜, 海参, 鲟鱼", Likes = "石英", Hates = "所有烹饪(除鱼菜), 冬青", Neutral = "所有蛋, 所有鱼, 所有水果, 所有牛奶" },
        new() { NpcId = "Wizard", Npc = "法师", Birthday = "冬季17", Loves = "紫蘑菇, 太阳精华, 超级黄瓜, 虚空精华", Likes = "所有晶洞矿物, 铱锭, 石英", Hates = "", Neutral = "所有水果" },
    ];
}
