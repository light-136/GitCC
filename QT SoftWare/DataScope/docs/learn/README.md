# DataScope Qt 学习体系（docs/learn）

> **定位**：面向**已会 WPF/C#、正在学 Qt** 的开发者。
> 以真实工业项目 DataScope Studio 为教材——不是"学完再做项目"，而是**在项目里学**。
> 推荐先读 `docs/README.md`（项目文档总导航），再回到本页。

---

## 一、为什么用 DataScope 学 Qt

| 学习陷阱 | DataScope 的解法 |
|----------|------------------|
| 教程 demo 太小，学完不会架构 | 真实分层：UI → services → domain+protocol → utils（`architecture-walkthrough.md`） |
| 只会语法，不懂工程问题 | 每章从**工程问题**出发（`qt-concepts.md`）：为什么需要线程/状态机/元对象 |
| 没有 WPF 锚点 | 14 概念五段式映射（`wpf-qt-mapping.md`）：机制→对应→本质区别→为何不能一一对应→代码 |
| 学完不练 | Level 1-7 练习 + 验收 + 答案索引（`exercises.md`） |
| 不敢改代码 | 有 21 个测试套件兜底：改坏了跑 `ctest` 立刻知道 |

---

## 二、Level 1-7 路径图

```
Level 1  看懂代码    ──►  architecture-walkthrough（源码行走，一条数据流贯穿）
Level 2  修改功能    ──►  改通道数/量程/报警规则 + ctest 兜底
Level 3  独立增模块  ──►  仿 SimulatorDevice 新增"串口设备"占位（P9 预留）
Level 4  独立写软件  ──►  SignalSlotDemo 思路 → 写一个定时计数器
Level 5  设计架构    ──►  分层 + Model/View：给数据加第二消费者
Level 6  深入底层    ──►  线程/网络/协议/生命周期（异常注入 + 状态机 + 跨线程）
Level 7  读大项目    ──►  Qt 官方 example 对照本项目 + 迁移复盘
```

**每级做法**：载体（文档/代码）→ 练习（exercises.md 对应级）→ 验收标准 → 答案索引。

---

## 三、学习文档导航

| 文档 | 干什么 | 适合 | 前置 |
|------|--------|------|------|
| `README.md`（本页） | 路径图 + 导航 | 任何人 | — |
| `wpf-qt-mapping.md` | WPF→Qt 14 概念深入映射 | 先建立"新语言旧直觉" | — |
| `architecture-walkthrough.md` | 源码行走：从 main 到数据流 | Level 1 | mapping 第 15 节 |
| `qt-concepts.md` | 工程问题驱动的概念精讲 | Level 1-2 | architecture |
| `exercises.md` | Level 1-7 练习 + 验收 + 答案 | 逐级 | 对应级文档 |

---

## 四、与项目文档的关联

```
docs/learn/（学）           docs/（工程）
─────────────────────────────────────────────────
wpf-qt-mapping        ──►   07-使用说明 / 02-总体设计（术语对照）
architecture-walkthrough ─► 02-总体设计 + 03-详细设计（架构分层同源）
qt-concepts           ──►   contracts/03/05/06/07（协议/状态机细节）
exercises             ──►   06-测试文档 / tests/unittests/（答案落点）
```

**源码 ↔ 契约 ↔ 学习 三向对应**（完整表见 `docs/README.md` 第 5 节）：
- 想找"哪个类" → `docs/README.md` 的 源码↔文档 表 + 本页 architecture；
- 想找"为什么这么设计" → `wpf-qt-mapping.md` + `qt-concepts.md`；
- 想找"怎么验证" → `exercises.md` 答案索引 → `tests/`。

---

## 五、建议开始路径

1. 先读 `wpf-qt-mapping.md` 总表（10 分钟建立地图）；
2. 再走 `architecture-walkthrough.md` 数据流（30 分钟看懂全局）；
3. 打开源码按路线图行走（对照 architecture 第 5 节）；
4. 开始 Level 1 练习，之后每级按 exercises.md 推进；
5. 卡住时回查 `qt-concepts.md` 对应概念。

**记住**：`ctest` 是安全网——大胆改，绿灯兜底。

---

*入口页。其余四个学习文档见上方导航。*
