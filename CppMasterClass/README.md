# C++ 完整语言学习项目 (CppMasterClass)

## 项目概述

系统性展示 C++ 语言的所有核心特性，10 个模块从基础到实战，每个模块都有可运行的实际结果。

## 快速开始

### 方式一：CMake 构建（推荐）

```bash
mkdir build && cd build
cmake .. -DCMAKE_BUILD_TYPE=Release
cmake --build .
./CppMasterClass
```

### 方式二：单命令编译（无需 CMake）

```bash
# g++ (Linux/macOS)
g++ -std=c++20 -O2 -pthread src/*.cpp -o CppMasterClass

# MSVC (Windows)
cl /std:c++20 /EHsc /O2 src\*.cpp /Fe:CppMasterClass.exe

# MinGW (Windows)
g++ -std=c++20 -O2 src/*.cpp -o CppMasterClass.exe
```

## 模块一览

| 模块 | 文件 | 核心特性 |
|------|------|---------|
| 01 基础语法 | `01_basics.cpp` | 类型系统、sizeof、auto、const/constexpr、枚举、控制流 |
| 02 指针与引用 | `02_pointers.cpp` | 原始指针、指针算术、new/delete、引用、nullptr、智能指针概述 |
| 03 面向对象 | `03_oop.cpp` | 类、构造/析构、拷贝/移动、继承、虚函数、抽象类、运算符重载、友元 |
| 04 模板编程 | `04_templates.cpp` | 函数模板、类模板、特化、可变参数模板、SFINAE、C++20 concepts |
| 05 STL | `05_stl.cpp` | vector/list/map/set/unordered_map、迭代器、算法、性能对比 |
| 06 内存管理 | `06_memory.cpp` | RAII、unique_ptr/shared_ptr/weak_ptr、移动语义、完美转发 |
| 07 并发编程 | `07_concurrency.cpp` | thread、mutex、condition_variable、future/async、atomic、线程池 |
| 08 现代C++ | `08_modern_cpp.cpp` | lambda、auto/decltype、结构化绑定、optional/variant/any、string_view |
| 09 设计模式 | `09_design_patterns.cpp` | 工厂方法、单例、观察者、策略、CRTP(静态多态) |
| 10 实战 | `10_real_world.cpp` | 模拟运动控制器：多轴并行、梯形速度规划、状态机、搬运流程 |

## C++ 语言优势

### 1. 极致性能 — 零成本抽象

C++ 的核心哲学是"你不用的不会产生开销"。模板、内联、constexpr 都在编译期完成，运行时零额外成本。

```
对比：C++ 排序 10 万整数 ≈ 3ms，Python ≈ 50ms，Java ≈ 15ms
原因：无GC暂停、内存布局紧凑(cache友好)、编译器深度优化
```

### 2. 硬件级控制 — 直接操作内存

指针和引用允许直接控制内存布局、对齐、位操作。适合嵌入式、驱动、实时系统。

```cpp
// 直接操作寄存器（嵌入式开发）
volatile uint32_t* GPIO_REG = reinterpret_cast<uint32_t*>(0x40020000);
*GPIO_REG |= (1 << 5);  // 设置第5位
```

### 3. 模板元编程 — 编译期计算

C++ 模板在编译期生成代码，实现类型安全的泛型和编译期计算，运行时完全零开销。

```cpp
constexpr int factorial(int n) { return n <= 1 ? 1 : n * factorial(n-1); }
constexpr int result = factorial(12);  // 编译期算出 479001600
```

### 4. 确定性资源管理 — RAII

对象离开作用域时析构函数自动调用，资源释放时机精确可控。不依赖 GC、不会泄漏。

```cpp
{
    std::unique_ptr<Sensor> sensor = std::make_unique<Sensor>();
    sensor->read();
}   // 自动释放，不需要 try-finally 或 using
```

### 5. 跨平台 — 同一代码多架构

C++ 代码可以编译到 x86、ARM、RISC-V、MIPS 等所有主流架构，同一份代码在 Windows/Linux/macOS/嵌入式上运行。

### 6. 工业标准 — 关键领域首选

| 领域 | C++ 应用 |
|------|---------|
| 游戏引擎 | Unreal Engine、Unity 底层 |
| 操作系统 | Windows、Linux 内核模块 |
| 嵌入式 | 汽车ECU、工业PLC、医疗设备 |
| 金融 | 高频交易系统(微秒级延迟) |
| 半导体 | 设备控制软件(运动控制、视觉检测) |
| AI推理 | TensorFlow、PyTorch C++后端 |

## C++ vs 其他语言

| 特性 | C++ | C# | Python | Java |
|------|-----|-----|--------|------|
| 运行速度 | 最快 | 快 | 慢 | 快 |
| 内存控制 | 完全手动 | GC | GC | GC |
| 编译期计算 | 强(constexpr/模板) | 弱 | 无 | 弱 |
| 嵌入式支持 | 原生 | 有限 | 有限 | 有限 |
| 学习曲线 | 陡峭 | 中等 | 平缓 | 中等 |
| 二进制大小 | 小 | 需要运行时 | 需要解释器 | 需要JVM |

## 项目结构

```
CppMasterClass/
├── CMakeLists.txt          # 构建配置 (CMake 3.20+, C++20)
├── README.md               # 本文件
└── src/
    ├── main.cpp            # 菜单入口
    ├── 01_basics.cpp       # 基础语法
    ├── 02_pointers.cpp     # 指针与引用
    ├── 03_oop.cpp          # 面向对象
    ├── 04_templates.cpp    # 模板编程
    ├── 05_stl.cpp          # STL容器与算法
    ├── 06_memory.cpp       # 内存管理
    ├── 07_concurrency.cpp  # 并发编程
    ├── 08_modern_cpp.cpp   # 现代C++特性
    ├── 09_design_patterns.cpp  # 设计模式
    └── 10_real_world.cpp   # 实战:运动控制器
```

## 编译环境要求

- **编译器**：支持 C++17（C++20 可选，部分特性会自动降级）
  - GCC 10+、Clang 12+、MSVC 2019+ 均可
- **CMake**：3.20+（可选，也可直接用 g++ 编译）

---

*CppMasterClass V1.0 — C++ 完整语言学习项目*
*最后更新：2026-05-09*
