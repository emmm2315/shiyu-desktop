using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DeskSprout
{
    internal sealed class LearningTask
    {
        internal int Day, Minutes;
        internal string Title, Detail, NextStep;
        internal LearningTask(int day,int minutes,string title,string detail,string nextStep)
        {Day=day;Minutes=minutes;Title=title;Detail=detail;NextStep=nextStep;}
    }
    internal sealed class LearningPackDefinition
    {
        internal readonly string Id,Title,Summary;
        internal readonly LearningTask[] Tasks;
        internal LearningPackDefinition(string id,string title,string summary,LearningTask[] tasks) {Id=id;Title=title;Summary=summary;Tasks=tasks;}
        internal int Days {get {int last=0;foreach(LearningTask task in Tasks)last=Math.Max(last,task.Day);return last+1;}}
    }
    internal static class LearningPack
    {
        internal const string Id="8086-registers-v1";
        internal static readonly LearningTask[] Tasks={
            new LearningTask(0,10,"8086 · 画一张寄存器分类图",
                "目标：认识8086的寄存器。\r\n练习：把AX、BX、CX、DX、SP、BP、SI、DI、CS、DS、SS、ES、IP、FLAGS分组。给每个标上16位；再标出AX/BX/CX/DX可拆成的高、低8位。\r\n完成标准：不看书重画一次，能指出AH与AL属于AX。\r\n工具：沿用课程工具；今天纸笔就够。",
                "先闭卷写出AX、BX、CX、DX，以及AH/AL。"),
            new LearningTask(0,20,"8086 · 找到调试器里的寄存器",
                "目标：把书上的名字与调试窗口对应。\r\n练习：用课程提供的8086模板进入调试，找到AX、BX、CX、DX、CS、IP、SS、SP。在自己的笔记中记录一组初始值。\r\n完成标准：标出哪些是16位寄存器；知道不同模板的初始值可以不同。\r\n只观察寄存器，不修改CS、SS或SP。",
                "打开课程示例，找到AX与IP，记下当前值。"),
            new LearningTask(1,10,"8086 · 拆分AH与AL",
                "目标：理解16位与两个8位部分的关系。\r\n手算：AX=1234H，AH=12H，AL=34H；只把AL改为56H，AX应为1256H。再把AH改为0ABH，AX应为0AB56H。\r\n练习：另选3个16位十六进制数，分别拆出高、低字节。\r\n完成标准：解释为什么改AL不会改AH。",
                "画出AX的16格，左8格标AH，右8格标AL。"),
            new LearningTask(1,20,"8086 · 单步验证高低字节",
                "在课程模板的可执行代码位置依次写：\r\nMOV AX,1234H\r\nMOV AL,56H\r\nMOV AH,0ABH\r\n\r\n先预测每行后的AX、AH、AL，再单步检查。\r\n完成标准：AX依次为1234H、1256H、0AB56H；把预测与实测写成三行表。\r\n数字以A至F开头时，MASM风格写法前面补0。",
                "先预测MOV AL,56H执行后的AX，不急着运行。"),
            new LearningTask(2,10,"8086 · 分清MOV的源和目的",
                "目标：熟悉课程常用的MOV 目的,源写法。\r\n练习：给MOV AX,BX和MOV CL,AL画出传送箭头；解释复制后源寄存器是否改变。再写5条同宽寄存器传送。\r\n完成标准：能说明MOV AX,BX改变AX，而不改变BX。\r\n先用通用寄存器，8位对8位、16位对16位。",
                "写下MOV 目的,源，然后给AX与BX画一条箭头。"),
            new LearningTask(2,20,"8086 · 验证五条寄存器传送",
                "先给AX、BX、CX、DX设置不同的16位值，再挑5条合法的寄存器传送指令。\r\n每条执行前预测目的寄存器的新值，执行后核对源是否保持。另试写MOV AL,BX并观察汇编器诊断，说明它为什么不是同宽传送；随后删除或注释这条非法指令，再重新汇编和调试。\r\n完成标准：5条预测正确；能指出8位与16位的区别。",
                "先写MOV AX,1234H和MOV BX,5678H作为已知初值。"),
            new LearningTask(3,10,"8086 · 手算连续MOV的值表",
                "先不要运行，逐行填写AX、BX：\r\nMOV AX,1234H\r\nMOV BX,AX\r\nMOV AL,56H\r\nMOV AH,0ABH\r\nMOV BL,AH\r\n\r\n完成标准：最终AX=0AB56H，BX=12ABH；能解释为什么修改AX不会自动修改之前复制出的BX。",
                "先在纸上画AX、BX两列，每执行一行就补一行。"),
            new LearningTask(3,20,"8086 · 用单步检查值表",
                "把上一项的5条MOV放入课程模板，逐条执行。每行对照自己的值表，检查AX/BX及AH/AL/BH/BL。\r\n再把最后一条改成MOV BH,AL，重新预测最终BX；重新从第一条执行完整程序，再验证。\r\n完成标准：原程序BX=12ABH；改写程序BX=5634H，AX仍为0AB56H。",
                "把手算表放在调试窗口旁，先核对第一条指令。"),
            new LearningTask(4,10,"8086 · 用临时寄存器交换两个数",
                "设AX=1234H、BX=5678H。尝试只用MOV完成交换。\r\n提示：需要先把一个旧值存到第三个通用寄存器，例如CX。\r\n完成标准：交换后AX=5678H、BX=1234H；能解释连续MOV AX,BX / MOV BX,AX为什么不能完成交换。",
                "先把旧AX保存到CX，再修改AX。"),
            new LearningTask(4,20,"8086 · 对比MOV交换与XCHG",
                "两种方法开始前都初始化AX=1234H、BX=5678H、CX=09ABCH。\r\n先单步验证：\r\nMOV CX,AX\r\nMOV AX,BX\r\nMOV BX,CX\r\n\r\n重新设置这三个初值，再执行XCHG AX,BX。\r\n完成标准：两种方法都交换成功；MOV方法把CX改为1234H，XCHG方法保持CX=9ABCH。\r\n扩展：交换AH与AL，预测AX的新值再验证。",
                "恢复AX、BX初值，先完成三条MOV交换。"),
            new LearningTask(5,10,"8086 · 认识段寄存器和IP",
                "目标：先认清寄存器的角色。\r\n用自己的话写：CS与代码段、DS与数据段、SS与栈段、ES与附加段的关系；CS:IP用于定位下一条将取执行的指令。\r\n完成标准：能把CS/DS/SS/ES/IP与课本图对应。\r\n物理地址计算留到课程讲分段寻址时继续。",
                "先找课本里的CS:IP，给它写一句自己的解释。"),
            new LearningTask(5,20,"8086 · 观察IP并辨认SP",
                "使用前几天的MOV程序，单步观察每条执行后的IP，记录变化。\r\n阅读课本中SS、SP、BP的作用，画出SS:SP指向栈顶的示意图。SP和BP都是16位，不能像AX一样拆成AH/AL。\r\n完成标准：知道IP变化与指令编码长度有关；知道SP记录栈顶偏移。\r\n此项只观察，不手动改CS、SS或SP。课程尚未讲栈时，SS/SP/BP先只识别名称，示意图留到课上再补。",
                "打开熟悉的MOV程序，记下执行前的IP。"),
            new LearningTask(6,10,"8086 · 寄存器闭卷十题",
                "闭卷回答：\r\n1 AX多少位？ 2 AH多少位？ 3 AX=9A7CH时AL是多少？\r\n4 只改AH会改AL吗？ 5 MOV AX,BX的目的是谁？\r\n6 复制后BX会跟着AX改变吗？ 7 MOV AL,BX为什么宽度不匹配？\r\n8 哪个段寄存器与代码有关？ 9 SS:SP描述什么？\r\n10 哪4个16位通用寄存器可拆成高、低8位？\r\n完成标准：先自己作答，再查课本；把错题记下来。",
                "合上书，先回答AX与AH分别是多少位。"),
            new LearningTask(6,20,"8086 · 综合验证并记下下一步",
                "给AX=1357H、BX=2468H，自己编写一段6至8条MOV/XCHG指令，至少包含一次高字节修改、一次低字节修改和一次寄存器复制。\r\n先逐行预测，再单步核对。\r\n完成标准：提交一张预测/实测值表，解释至少一处原先容易混淆的地方；在“明天接着做”写一句具体下一步。\r\n下一周可接分段寻址和内存读写，跟随课程进度。",
                "先为AX=1357H、BX=2468H画出四个高低字节。")
        };
        internal static readonly LearningPackDefinition[] All={
            new LearningPackDefinition(Id,"8086 · 寄存器入门","7天，每天10分钟概念 + 20分钟练习，共14项。\n使用你课程的8086模板与工具，日期可自行调整。",Tasks),
            new LearningPackDefinition("os-principles-v1","操作系统原理","14天，每天10分钟概念 + 20分钟习题，共28项。\n从基础概念到进程、同步、内存、文件与I/O。",UniversityPacks.OperatingSystems),
            new LearningPackDefinition("algorithm-design-v1","算法分析与设计","14天，每天10分钟概念 + 20分钟习题，共28项。\n复杂度、分治、贪心、动态规划、回溯与图算法。",UniversityPacks.Algorithms)
        };
        internal static LearningPackDefinition Find(string id)
        {
            foreach(LearningPackDefinition pack in All)if(pack.Id==id)return pack;
            throw new ArgumentException("学习任务包不存在。","id");
        }
        internal static string PlanText() {return PlanText(All[0]);}
        internal static string PlanText(LearningPackDefinition pack)
        {
            StringBuilder s=new StringBuilder(pack.Title+"（"+pack.Days+"天 / "+pack.Tasks.Length+"项）\r\n\r\n"+pack.Summary.Replace("\n","\r\n")+"\r\n\r\n");
            foreach(LearningTask task in pack.Tasks)s.Append("第").Append(task.Day+1).Append("天 · ").Append(task.Minutes).Append("分钟 · ").Append(task.Title).Append("\r\n").Append(task.Detail).Append("\r\n\r\n");
            if(pack.Id==Id)s.Append("参考：Intel《The 8086 Family User's Manual》(1979)，CPU寄存器及指令章节。\r\nhttps://www.inf.pucrs.br/~calazans/undergrad/orgcomp_EC/mat_microproc/intel-8086_family_Users_Manual.pdf\r\n");
            else s.Append(UniversityPacks.Sources).Append("\r\n");
            s.Append("练习安排、题目与验收条件为本组件编写。完成时间是建议，可以调整任务日期或按自己的进度学习。\r\n");return s.ToString();
        }
        internal static void Export(string path) {File.WriteAllText(path,PlanText(),new UTF8Encoding(true));}
        internal static void Export(string id,string path) {File.WriteAllText(path,PlanText(Find(id)),new UTF8Encoding(true));}
    }
    public sealed partial class WidgetCore
    {
        public int Import8086Registers(DateTime startDate) {return ImportLearningPack(LearningPack.Id,startDate);}
        public int ImportLearningPack(string id,DateTime startDate)
        {
            LearningPackDefinition pack=LearningPack.Find(id);
            if(Extras.ImportedLearningPacks.Contains(pack.Id))return 0;
            startDate=startDate.Date;
            if(startDate>DateTime.MaxValue.Date.AddDays(-(pack.Days-1)))throw new ArgumentOutOfRangeException("startDate");
            List<PlanItem> added=new List<PlanItem>();
            foreach(LearningTask task in pack.Tasks) {
                PlanItem item=new PlanItem {Id=Guid.NewGuid().ToString("N"),Title=task.Title,Date=startDate.AddDays(task.Day),
                    Kind=PlanKind.Plan,Repeat=RepeatKind.None,ReminderEnabled=false,EstimatedMinutes=task.Minutes,DetailNote=task.Detail};
                added.Add(item);
            }
            Items.AddRange(added);
            for(int day=0;day<pack.Days;day++) {
                DailyExtraRecord record=ExtrasFor(startDate.AddDays(day));
                if(record.PriorityTaskIds.Count==0)
                    for(int task=0;task<added.Count && record.PriorityTaskIds.Count<2;task++)if(pack.Tasks[task].Day==day)record.PriorityTaskIds.Add(added[task].Id);
            }
            for(int i=0;i<added.Count;i++)Extras.ResumeNotes.Add(new ResumeNote {TaskId=added[i].Id,Text=pack.Tasks[i].NextStep});
            Extras.ImportedLearningPacks.Add(pack.Id);SaveAfterChange();return added.Count;
        }
    }
}
