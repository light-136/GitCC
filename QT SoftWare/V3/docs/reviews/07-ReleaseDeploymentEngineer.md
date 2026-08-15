# 07 — Release / Deployment Engineer 审查（V3 草案攻击）

> **角色**：Release / Deployment Engineer（发布工程）
> **阶段**：第二阶段——对 V3 草案第十九节「发布设计」的逐条攻击
> **日期**：2026-08-15
> **审查对象**：`V3/docs/00-V3架构审查与草案.md`（第十九节 + 第二十一节第 7 条）
> **现实约束**：Windows 11 本机开发，Qt 5.12.2 mingw73_64 + MinGW 7.3.0 + CMake + Ninja，**无远程 CI 服务器**

---

## 零、审查规划（先列规划，再给结论）

1. 逐条核对草案第十九节 4 条发布设计，判断其是「目标态」还是「可落地路径」。
2. 用 V2 真实代码/仓库状态作为证据，验证草案对 V2 发布痛点的描述是否准确、是否更严重。
3. 六个攻击视角逐一硬碰硬：CI 现实性 / 版本一致 / windeployqt 脆弱性 / 发布包验证 / 依赖管理 / 可复现构建。
4. 每个攻击点给出「基于现实约束的可行方案」（PowerShell / CMake / CTest，不引入远程依赖）。
5. 按严重度（P0/P1/P2）排序，输出 verdict。

---

## 一、同意点（草案对的部分，简要）

1. **e2e probe 方向对**：`tests/e2e/probe_simulator.ps1` 对「独立发布包里的 simulator.exe」发真实 TCP 协议帧（AA55/CRC16/0x83 应答），验证的不是「能启动」而是「设备侧协议链路完整」——这是发布包验证该有的高度，V3 保留它完全正确。
2. **「编译产物与源码分离」方向对**：`build/`、`build_release/` 确实已在根 `.gitignore` 中忽略（V2 已做一半）。但「`deploy/` 不进版本库」这一半 V2 是反着做的（见攻击点 6），草案点对了痛点。
3. **「版本一致」痛点真实**：V2 主程序与 simulator 版本号确实不一致，且比草案说的更糟（见攻击点 2，simulator 现在写的是 `2.0.0`，不是草案说的 1.0.0）。
4. **「windeployqt 静默失败要变硬失败」方向对**：V2 的 `ds_deploy_qt()` 确实是静默跳过，草案抓住了要害。
5. **「domain/protocol 零 Qt」对发布有正面价值**：协议层抽成纯 C++17 后，`dscope_protocol`/`dscope_domain` 可以脱离 Qt 事件循环用 gcc + gtest 独立测试，甚至被非 Qt 工程复用——这是 V2 发布里不存在的好处。但草案漏了 ABI 盲区（见攻击点 5）。

---

## 二、攻击点（主体，逐条硬碰硬）

### 攻击点 1：「CI 自动化发布」是一个没有落地路径的「理想国」（P0）

草案第十九节第 1 条写「一键构建 + 测试 + 打包，不在本机手工 windeployqt」，第二十一节第 7 条把「CI 自动化发布在无远程 CI 服务器下是否可行」列为可推翻假设。**审查结论：这一条在用户当前环境不可行，草案写的是目标态，没有给出本机可执行的替代路径。**

证据：

- 全仓库 **没有任何 CI 配置**：`Glob **/.github/**/*`、`**/*.{yml,yaml}` 均为空，只有 `tests/e2e/probe_simulator.ps1` 与 `tests/smoke/smoke.sh` 两个手工脚本。
- 「一键」所需的编排脚本**不存在**：没有 `scripts/release.*`、没有 `.cmd`/`.ps1` 一键入口，构建靠文档《05-构建与部署说明》里手抄的 `cmake -S/-B/-D...` 命令。
- 更关键：**e2e 与 smoke 根本没进 CTest**。`CMakeLists.txt` 里只有 21 个 `add_test(NAME tst_* ...)`，`probe_simulator.ps1` 和 `smoke.sh` 从未被 `add_test` 注册，因此 `ctest` 门禁覆盖不到发布包验证——草案说「做成 CI 门禁」，但门禁引擎（CTest）现在根本不认识这两个脚本。
- GitHub Actions / 本地 self-hosted runner **都依赖一个远程 GitHub repo**。用户「暂时没 GitHub」，那么「本机 GitHub Actions」「本地 runner」全部落空——没有 repo 就没有 runner 可以挂。

**结论**：草案的「CI 自动化」是「理想国 CI」。现实里唯一落地路径是**本机脚本 + CTest 作为门禁引擎**，GitHub Actions 留作「未来有远程 repo 后的同源迁移」，不是现在的主路径。

**可行方案（本机 CI，零远程依赖）**：

- 写一个 `scripts/release.ps1`（PowerShell，Windows 原生，不依赖 bash），串起 5 步：
  1. `configure`（固化 `-S/-B/-DCMAKE_PREFIX_PATH/-DCMAKE_CXX_COMPILER` 参数，消除手抄路径差异）；
  2. `build`（`cmake --build build_release`）；
  3. `test`（`ctest --test-dir build_release --output-on-failure`）；
  4. `e2e`（把 probe/smoke 注册进 CTest，见攻击点 4）；
  5. `package`（把 `deploy/` 目录打成带版本号的 zip + 生成 `buildinfo.txt`）。
- 把 e2e/smoke 用 `add_test` 收编进 CTest（见攻击点 4 的具体写法），这样 `ctest` 本身就是「本机 CI 门禁」，`release.ps1` 只是它的壳。
- 预留 `.github/workflows/release.yml` 但注释标注「需远程 repo 就绪后生效」，其 `run:` 段与 `scripts/release.ps1` 同源，保证「本地=远程」将来零迁移成本。

---

### 攻击点 2：版本一致性——「单一版本源」从未建立，5 处硬编码 + 0 处传播（P0，比草案更严重）

草案第十九节第 2 条说「主程序与 simulator 同版本号（V2 曾 1.0.0 vs 0.1.0 不一致）」，并隐含「从 CMake 单一版本源自动保证」。**审查结论：V2 的 `project(DataScope VERSION 0.1.0)` 是一个从未被消费的死声明，版本号全靠 5 处手写字符串，simulator 现在写的是 `2.0.0`，比草案描述的 1.0.0 更离谱；Windows PE 资源里连版本信息都没有。**

证据（`D:\Gemi\QT SoftWare\DataScope\`）：

| 位置 | 内容 | 是否由 `project(VERSION)` 传播 |
|------|------|-------------------------------|
| `CMakeLists.txt:11` | `project(DataScope VERSION 0.1.0 ...)` | 唯一的版本声明 |
| `src/app/main.cpp:32` | `setApplicationVersion("0.1.0")` | **否，硬编码** |
| `src/app/main.cpp:39` | `"DataScope Studio 启动 v0.1.0"` | **否，硬编码** |
| `src/ui/mainwindow.cpp:125` | `"版本 0.1.0 ｜ Qt 5.12.2"` | **否，硬编码** |
| `src/simulator/main.cpp:75` | `setApplicationVersion("2.0.0")` | **否，硬编码，且与主程序不一致** |

- 全工程 **没有任何 `.rc` / `.rc.in` / `version` 文件**（`Glob **/*.{rc,rc.in,version}` 无结果），也**没有 `configure_file` 生成 version 头的代码**。`project(VERSION 0.1.0)` 只是声明了 `${PROJECT_VERSION}` 变量，从未被 `add_definitions`、`configure_file` 或 RC 资源消费。
- 后果一：`DataScope.exe` 与 `simulator.exe` 的 Windows PE 资源里**没有 FileVersion/ProductVersion**，右键属性看不到版本号，发布包无法靠文件属性做版本追踪。
- 后果二：改版本要手工同步 5 个文件，漏一处就重现 V2 的「0.1.0 vs 2.0.0」事故。这正是「单一版本源」要解决的，而 V2 一点没做。

**可行方案（CMake 单一版本源 → 三处自动一致）**：

- 保留 `project(DataScope VERSION x.y.z)` 作为**唯一版本源**，用 `configure_file` 生成两个文件：
  1. `src/version.h.in` → `src/version.h`，生成 `DATASCOPE_VERSION_MAJOR/MINOR/PATCH/STRING` 宏，`main.cpp` 与 `simulator/main.cpp` 的 `setApplicationVersion` 都改用 `DATASCOPE_VERSION_STRING`；
  2. `src/version.rc.in` → `src/version.rc`，生成 Windows 版本资源（`FILEVERSION 0,1,0,0` 逗号形式 + `"0.1.0"` 字符串形式），用 `target_sources(DataScope PRIVATE version.rc)` / `target_sources(simulator PRIVATE version.rc)` 挂到两个 exe（MinGW 的 `windres` 原生支持 `.rc`）。
- 这样**版本号只改一处**（`project` 行），宏、应用版本、PE 文件属性三处自动同步。改版脚本 `scripts/bump_version.ps1` 只改 `CMakeLists.txt` 里的 `project(VERSION ...)`。

---

### 攻击点 3：windeployqt 的静默失败——三处缺陷，找到就静默跳过（P1）

草案第十九节第 1 条要「不在本机手工 windeployqt」，隐含要自动化且可靠。**审查结论：V2 的 `ds_deploy_qt()` 把「找不到 windeployqt」静默吞掉，构建照样绿、运行必崩，而且它的查找逻辑与文档自己承诺的「不依赖 PATH」自相矛盾。**

证据（`CMakeLists.txt:178-188`）：

```cmake
function(ds_deploy_qt TARGET_NAME)
    if(WIN32)
        find_program(QT_WINDEPLOYQT windeployqt HINTS "${CMAKE_PREFIX_PATH}/bin")
        if(QT_WINDEPLOYQT)
            add_custom_command(TARGET ${TARGET_NAME} POST_BUILD ...)   # 找不到 → 整段被跳过
        endif()
    endif()
endfunction()
```

三个缺陷：

1. **`find_program` 无 `REQUIRED`**：找不到 `windeployqt` 时 `QT_WINDEPLOYQT` 为空，`if(QT_WINDEPLOYQT)` 为假，`add_custom_command` 整段不生成，构建**无任何告警**继续成功。随后 exe 因缺 `Qt5Core.dll`/`platforms/qwindows.dll` 启动即 0xc000007b 崩溃。
2. **HINTS 只给 `${CMAKE_PREFIX_PATH}/bin`，且会静默 fallback 到 `PATH`**：`find_program` 默认还会搜 `PATH`。文档《05》1.2 节明确「不建议依赖 PATH」，但函数实际在 `CMAKE_PREFIX_PATH` 没配好时**就是**退而依赖 PATH——承诺与实现相反。若 `CMAKE_PREFIX_PATH` 用反斜杠传参（文档 5.4 节已知的坑），HINTS 路径损坏，函数悄悄依赖 PATH 或直接失败。
3. **部署结果无校验**：`add_custom_command` 执行了 `windeployqt` 也不代表成功——没有后续检查 `platforms/qwindows.dll`、`Qt5Core.dll` 是否真的落地。

**可行方案（静默失败 → 配置期/构建期硬失败）**：

- **配置期硬失败**：从 `Qt5::qmake` 推导 windeployqt 真实路径，找不到就 `message(FATAL_ERROR)`：
  ```cmake
  get_target_property(_qmake Qt5::qmake IMPORTED_LOCATION)
  get_filename_component(_qt_bin "${_qmake}" DIRECTORY)
  find_program(QT_WINDEPLOYQT windeployqt HINTS "${_qt_bin}" NO_DEFAULT_PATH)
  if(NOT QT_WINDEPLOYQT)
      message(FATAL_ERROR "找不到 windeployqt（Qt bin: ${_qt_bin}），无法部署 Qt 运行库。请检查 CMAKE_PREFIX_PATH 是否指向 Qt 安装根目录。")
  endif()
  ```
  `NO_DEFAULT_PATH` 去掉 PATH fallback，`FATAL_ERROR` 把静默失败变成配置期硬失败，构建根本起不来，而不是「起来了但崩在运行时」。
- **构建期硬失败 + 校验**：`windeployqt` 后加一个校验命令（检查目标 exe 同目录的 `platforms/qwindows.dll` 与 `Qt5Core.dll` 存在），不存在则 `cmake -E` 报错退出。

---

### 攻击点 4：发布包验证——e2e 只覆盖 simulator 半条链路，主程序 GUI 零协议级验证，smoke 依赖 bash（P1）

草案第十九节第 3 条要「自动化校验 DLL 完整性 + 独立环境运行」，第二十一节认为「e2e probe 方向对」。**审查结论：方向对，但 V2 现状是——probe 只打 simulator 不打主程序、DLL 完整性靠人工清单、smoke 是 bash 脚本在 Windows 本机需要额外装 Git Bash，且二者都不在 CTest 门禁里。**

证据：

- `probe_simulator.ps1` 全文只启动 `simulator.exe` 做 TCP 协议验证（第 54 行 `Start-Process $Exe` 默认指向 `deploy/simulator.exe`），**从不验证 `DataScope.exe` 主程序**。主程序 GUI 能不能独立运行，只有 `smoke.sh` 第 38 行 `run_alive "$BUILD_DIR/DataScope.exe"` 的「后台启动 + 1.5 秒存活」——这不是协议级验证，只是「没在 1.5 秒内崩」。
- `smoke.sh` 是 **bash 脚本**（`#!/usr/bin/env bash`），Windows 11 本机跑它需要 Git Bash / MSYS2 环境，与 `probe_simulator.ps1` 的 PowerShell 是**两套 shell**，发布门禁因此依赖两个不同的运行时环境。
- **DLL 完整性校验不存在**：`deploy/` 目录下 DLL 靠文档《05》4.4 节「手工检查清单」+ 人工看 `dumpbin /dependents`，没有自动化脚本断言「缺一个 DLL 门禁就红」。

**可行方案（PowerShell + CTest 收编，去掉 bash 依赖）**：

- **把 e2e probe 收编进 CTest**（这就是「CI 门禁」的落地）：
  ```cmake
  add_test(NAME e2e_probe_simulator
      COMMAND powershell -NoProfile -ExecutionPolicy Bypass
              -File ${CMAKE_SOURCE_DIR}/tests/e2e/probe_simulator.ps1
              -Exe $<TARGET_FILE:simulator>)
  set_tests_properties(e2e_probe_simulator PROPERTIES TIMEOUT 60)
  ```
- **用 PowerShell 重写 smoke.sh** 为 `tests/smoke/smoke.ps1`（等价逻辑：后台启动 → 1.5 秒探活 → 收尾），同样 `add_test` 注册，消除 bash 依赖。
- **新增 `tests/e2e/verify_deploy.ps1` 做 DLL 完整性自动化**：
  1. 断言清单：`Qt5Core.dll`、`Qt5Gui.dll`、`Qt5Widgets.dll`、`Qt5Network.dll`、`libgcc_s_seh-1.dll`、`libstdc++-6.dll`、`libwinpthread-1.dll`、`platforms/qwindows.dll` 必须全部存在，缺一个 exit 非 0；
  2. 解析两个 exe 的 PE 导入表（PowerShell 读 `IMAGE_IMPORT_DESCRIPTOR`），断言所有导入 DLL 要么在系统目录、要么在 `deploy/` 目录内，抓出「漏部署的依赖」；
  3. 干净目录启动：把 `deploy/` 拷到临时目录，`Start-Process` 主程序 + simulator，验证非零退出码。
- **给主程序加 `--version` / `--selftest` 参数**（Qt 5.12 支持 `QCommandLineParser`）：`DataScope.exe --version` 打印版本后退出、`--selftest` 用 `-platform offscreen` 无窗口自检后退出，让主程序也能被 e2e 脚本做「无窗口、可断言」的发布验证，而不是靠「存活 1.5 秒」。

---

### 攻击点 5：依赖管理——固定本地安装但零锁定；「domain/protocol 零 Qt」的 ABI 盲区草案没考虑（P1）

草案第十九节 + 第六节说「domain/protocol 零 Qt 依赖」。**审查结论：零 Qt 让 domain/protocol 可独立于 Qt 测试/发布是真实收益，但草案漏了两点——(a) 纯 std 库在 Windows 下的 MSVC/MinGW ABI 不兼容没有考虑；(b) simulator 根本不是零 Qt（依赖 QtNetwork），草案第十四节的「模拟设备=反角色」与「零 Qt」在发布上是两回事。**

证据：

- `dscope_protocol`/`dscope_domain` 抽成纯 C++17（`std::string`/`std::vector<uint8_t>`）后，确实可以**只用 MinGW g++ 编译 + gtest 测试，不拉 Qt**——这是发布层面的真实红利：协议层回归不再依赖 windeployqt，CI 门禁里跑协议单测不需要部署 Qt DLL。
- 但 **ABI 盲区**：MinGW 用的 `libstdc++` 与 MSVC 的 MSVC STL **二进制不兼容**（`std::string`/`std::vector` 的对象布局、异常模型、`_GLIBCXX_USE_CXX11_ABI` 都不同）。只要发布包内 app/simulator/所有库都用同一套 MinGW 7.3.0 编译就没问题；但一旦未来想把这套「零 Qt」的头文件/静态库分发给 MSVC 消费者复用，`std::string` 跨工具链就是链接错误或运行时崩溃。草案的「可被非 Qt 代码消费」没有标注「仅限同工具链（MinGW）」这个前提。
- `simulator` **不是零 Qt**：`CMakeLists.txt:208-212` 里 `datascope_simulator` 链接 `Qt5::Core Qt5::Network`，`simulator` 可执行程序依赖 `QTcpServer`。所以「protocol 零 Qt」只惠及协议/领域层，**simulator 的发布仍依赖 Qt DLL**，不能像 domain/protocol 那样脱离 Qt 打包。草案第十四节把 simulator 描述为「真实设备的反角色」没问题，但发布视角上要明确区分「零 Qt 的部分（protocol/domain）可以独立测试/分发」和「非零 Qt 的部分（simulator/app/UI）仍需 windeployqt」。

**可行方案**：

- 发布文档明确写死：**仅支持 MinGW 7.3.0（posix-seh）工具链，禁止与 MSVC 混链**；「零 Qt 库可独立测试」是真，「零 Qt 库可跨工具链分发」是假。
- 在 CMake 里加**工具链版本硬校验**（见攻击点 6），把「换 MSVC 编译」在配置期就拦下来，而不是链接期出 undefined reference。
- `dscope_protocol`/`dscope_domain` 单独提供 `ctest -R protocol` 的无 Qt 门禁目标，作为「协议层独立于 Qt 发布验证」的落地证据。

---

### 攻击点 6：可复现构建——无依赖锁、无工具链版本校验，且 `deploy/` 二进制竟然被 git 追踪（P1）

草案第十九节第 4 条要「编译产物与源码分离：build/、deploy/ 不进版本库」，第二十一节隐含「换一台机器能复现同一份发布包」。**审查结论：可复现构建的三大支柱（依赖锁定 / 工具链版本校验 / 产物出库）V2 一个都没立起来，其中「deploy/ 不进版本库」这条 V2 是反着做的——deploy 目录里的 20MB 二进制 DLL 全在 git 里。**

证据：

- **`deploy/` 被 git 追踪**：`git ls-files "QT SoftWare/DataScope/deploy"` 返回几十个文件，包括 `deploy/DataScope.exe`、`deploy/Qt5Core.dll`、`deploy/D3Dcompiler_47.dll`（4MB）、`deploy/opengl32sw.dll`（20MB）等第三方二进制。根 `.gitignore` 只忽略了 `build/` 与 `build_release/`，**漏了 `deploy/`**。草案说「deploy 不进版本库」是愿望，V2 实际把 Qt 的 DLL 提交进了版本库。
- **无依赖锁定**：Qt 5.12.2 / MinGW 7.3.0 是本地安装，构建全靠 `-DCMAKE_PREFIX_PATH=D:/Qt/5.12.2/mingw73_64` 这种**绝对路径**硬编码在文档命令里。换一台机器 Qt 装在不同盘符/路径，命令就废。
- **无工具链版本校验**：`find_package(Qt5 COMPONENTS ... REQUIRED)` 接受**任意 Qt5 版本**（5.12、5.13、5.15 都能过），CMake 也不校验 MinGW 是不是 7.3.0、是不是 posix-seh。换一台装 Qt 5.15 的机器，构建能过但产物与「同一份发布包」对不上。
- **构建命令没有脚本化**：`-S/-B/-DCMAKE_PREFIX_PATH/-DCMAKE_CXX_COMPILER` 那一长串参数靠文档手抄，任何人漏一个参数、写错一个反斜杠，产出的发布包就不同。

**可行方案（三支柱补齐）**：

- **产物出库**：`.gitignore` 补 `QT SoftWare/DataScope/deploy/`，把已追踪的 deploy 二进制用 `git rm -r --cached` 移出索引；发布包改为「构建时由 `release.ps1` 生成的带版本号 zip」，zip 本身也不入库（或放独立 release 目录不入 git）。
- **工具链版本硬校验**：CMake 配置期断言：
  ```cmake
  if(NOT Qt5Core_VERSION VERSION_EQUAL "5.12.2")
      message(FATAL_ERROR "要求 Qt 5.12.2（当前 ${Qt5Core_VERSION}），换版本会导致产物不可复现。")
  endif()
  if(NOT CMAKE_CXX_COMPILER_ID STREQUAL "GNU" OR CMAKE_CXX_COMPILER_VERSION VERSION_LESS "7.3")
      message(FATAL_ERROR "要求 MinGW-w64 7.3.0 posix-seh（当前 ${CMAKE_CXX_COMPILER_ID} ${CMAKE_CXX_COMPILER_VERSION}）。")
  endif()
  ```
- **构建脚本化 + 构建记录**：`scripts/configure.ps1` 固化全套 `-D` 参数（消除手抄差异）；`release.ps1` 打包时生成 `buildinfo.txt`（CMake 版本 / Qt 版本 / 编译器版本 / git commit / 构建时间戳），附进 zip，换机器时能比对「这份发布包是用什么工具链产出的」。

---

## 三、按严重度排序的问题清单

| 严重度 | 问题 | 一句话定性 |
|--------|------|-----------|
| **P0** | 版本一致完全未落地 | `project(VERSION 0.1.0)` 是死声明：5 处硬编码版本、simulator 竟写 `2.0.0`、无 PE 版本资源，草案承诺的「单一版本源」V2 一个传播机制都没有。 |
| **P0** | 「CI 自动化」是无落地路径的理想国 | 无远程 repo、无 .github、无本地 runner 脚本，e2e/smoke 甚至不在 CTest，草案第十九节四项全是目标态。 |
| **P1** | `deploy/` 二进制被 git 追踪 | 草案要「deploy 不进版本库」，V2 反着做：20MB `opengl32sw.dll`、4MB `D3Dcompiler_47.dll` 等 Qt 二进制全提交进 git，`.gitignore` 漏了 `deploy/`。 |
| **P1** | windeployqt 静默失败 | `find_program` 无 REQUIRED、HINTS 仅 `CMAKE_PREFIX_PATH` 且 fallback PATH，找不到就跳过 `add_custom_command`，构建绿但运行崩，还与文档「不依赖 PATH」自相矛盾。 |
| **P1** | 发布包验证只覆盖 simulator 半条链路 | e2e probe 只打 simulator 不碰主程序 GUI，smoke 是 bash 脚本（Windows 本机要额外装 Git Bash），DLL 完整性靠人工清单，三者都不在 CTest 门禁。 |
| **P2** | 依赖管理零锁定 + ABI 盲区 | 固定本地安装但无锁文件；「domain/protocol 零 Qt」可独立测试是真，但 std 库 MSVC/MinGW ABI 不兼容草案没提，simulator 也并非零 Qt。 |
| **P2** | 可复现构建缺校验 | `find_package(Qt5 REQUIRED)` 接受任意 Qt5 版本、CMake 不校验 MinGW 版本、构建命令靠文档手抄，换机器无法保证「同一份发布包」。 |

---

## 四、verdict

**草案第十九节的发布设计是「方向正确的目标态，但四条全是愿望、没有一条落到用户『Windows 单机 + 固定 Qt 5.12.2/MinGW + 无远程 CI』的现实」——它不是错的，是空的。** 可落地路径是：**本机脚本（`release.ps1`）+ CTest 作为门禁引擎**替代「理想国 CI」；**`project(VERSION)` + `configure_file` 生成 version.h/version.rc** 建立真正的单一版本源；**`find_program(... REQUIRED)` + 部署后校验** 把 windeployqt 静默失败变成配置期硬失败；**e2e/smoke 用 `add_test` 收编进 CTest + PowerShell 重写去 bash 依赖 + 补主程序 `--selftest` 与 DLL 完整性脚本** 补全发布包验证；**`deploy/` 出库改 zip 化 + 工具链版本硬校验 + `buildinfo.txt`** 补上可复现构建。GitHub Actions 留作「未来远程 repo 就绪后的同源迁移」，不是 V3 现在的主路径。**结论：草案发布设计需要重写为「本机可执行」的落地方案，否则 V3 会重演 V2 的『构建绿、发布崩、版本乱』。**
