# 07 — 完成定义（Definition of Done）

> ⚠️ **本文为早期草案（v1 之前），已被 `AUTHORITATIVE-SPEC-v1.md` 取代**：凡与 v1 冲突以 v1 为准；对齐落地设计见 `docs/11-工程设计说明.md`。

> **文档性质**：V3 中"一个模块/阶段/版本怎样才算完成"的可执行判据。核心反对 V2 的"定性愿望式 DoD"（"失败场景必须有测试"没有量化），改为**可机械验证的判据**。

---

## 1. 一个模块（Module）完成 = 以下全部为真

> ⚠️ **本文为早期草案（v1 之前），已被 `AUTHORITATIVE-SPEC-v1.md` 取代**：凡与 v1 冲突以 v1 为准；对齐落地设计见 `docs/11-工程设计说明.md`。

| # | 判据 | 验证方式 |
|---|------|---------|
| M1 | 该模块的公开接口与 02 模块契约一致，禁止事项无违规 | Code Review＋头文件扫描 |
| M2 | 依赖符合 03 依赖规则的 target 白名单（CMake 断言通过） | configure 期 `FATAL_ERROR` 不触发 |
| M3 | 每个失败路径至少一条**断言 ErrorCode 或状态转移**的测试（非"不崩"） | Code Review 逐条核对 |
| M4 | 该模块的单测在 Unit/Component 层通过，且**属于正确的层**（链接白名单验证） | `ctest` |
| M5 | 变异冒烟通过：删掉一行关键逻辑，测试转红 | 抽查至少 1 处关键逻辑 |
| M6 | 无"禁止清单"（05 第 6 节）任何一条 | Code Review |

## 2. 一个阶段（Phase/Milestone）完成 = 以下全部为真

> ⚠️ **本文为早期草案（v1 之前），已被 `AUTHORITATIVE-SPEC-v1.md` 取代**：凡与 v1 冲突以 v1 为准；对齐落地设计见 `docs/11-工程设计说明.md`。

| # | 判据 | 验证方式 |
|---|------|---------|
| P1 | 本阶段所有模块满足第 1 节 | 汇总 |
| P2 | `build_and_verify.ps1` 返回 0（configure＋build＋ctest＋smoke＋e2e 全绿） | 实际执行 |
| P3 | Unit＋Component 全绿 ≤ 2 分钟，Integration ≤ 1 分钟 | `ctest` 计时 |
| P4 | 新增失败场景测试覆盖 06 第 4 节表格中"本阶段涉及"的行 | 逐行核对 |
| P5 | 文档同步：若本阶段变更了架构/契约，01~09 相应更新 | 检查文档一致性 |

## 3. 一个版本（Release）完成 = 以下全部为真

> ⚠️ **本文为早期草案（v1 之前），已被 `AUTHORITATIVE-SPEC-v1.md` 取代**：凡与 v1 冲突以 v1 为准；对齐落地设计见 `docs/11-工程设计说明.md`。

| # | 判据 | 验证方式 |
|---|------|---------|
| R1 | 满足第 2 节全部 | — |
| R2 | 版本号单一来源：`project(VERSION)` 一处改，`DataScope.exe`/`simulator.exe` 的 PE 资源 FileVersion 与 `setApplicationVersion` 三者一致 | `version.h/version.rc` 由 `configure_file` 生成 |
| R3 | windeployqt **配置期硬失败**（找不到就 `FATAL_ERROR`），且部署后校验 `platforms/qwindows.dll`、`Qt5Core.dll` 存在 | `release.ps1` |
| R4 | e2e 探针对 `deploy/` 里的 simulator **和**主程序（`--version/--selftest`）都做了协议级/自检级验证 | `add_test` 注册的 e2e |
| R5 | DLL 完整性自动化断言通过（缺一个 DLL 门禁就红） | `verify_deploy.ps1` |
| R6 | `deploy/` 二进制不在 git（`.gitignore` 覆盖），发布包是带版本号 zip | `git ls-files` 为空 |

## 4. 反例（V2 未达标的 DoD，V3 明令禁止的"假完成"）

> ⚠️ **本文为早期草案（v1 之前），已被 `AUTHORITATIVE-SPEC-v1.md` 取代**：凡与 v1 冲突以 v1 为准；对齐落地设计见 `docs/11-工程设计说明.md`。

| V2 现象 | 为什么不算完成 | V3 对应判据 |
|---------|--------------|-----------|
| 163 用例"全绿"但混进 `tst_signalslotdemo`（测已删对象） | 删生产代码＋测试一起删照样绿 | M5 变异冒烟 |
| smoke 目录曾经为空 | 分层靠注释自证，无人执行 | M4 链接白名单 |
| e2e/smoke 不在 CTest | 门禁引擎不认识它们 | P2/R4 收编进 CTest |
| simulator 写 `2.0.0`、主程序写 `0.1.0` | 版本号 5 处硬编码 | R2 单一版本源 |
| windeployqt 找不到就静默跳过 | 构建绿、运行崩 | R3 配置期硬失败 |
| `deploy/` 20MB DLL 进 git | 产物入库 | R6 出库 |

## 5. DoD 的强制力来源

> ⚠️ **本文为早期草案（v1 之前），已被 `AUTHORITATIVE-SPEC-v1.md` 取代**：凡与 v1 冲突以 v1 为准；对齐落地设计见 `docs/11-工程设计说明.md`。

- 本文件所有"可机械验证"判据（M2/M4/P2/P3/R2/R3/R4/R5/R6）**不依赖开发者自觉**，由 CMake 断言、脚本退出码、`ctest` 计时保证。
- 剩余定性判据（M1/M3/M5/M6/P4/P5）由 Code Review 保证，但 Code Review 本身**必须**在进入下一阶段前完成，且由 Principal Architect 签字。

---

*DoD 是"完成"的唯一口径。任何"完成了但没跑门禁""完成了但测试超时""完成了但版本不一致"的声明，一律视为未完成。*
