// ============================================================
// main.cpp — C++ 完整语言学习项目 入口
// 菜单驱动：用户选择运行哪个模块
// ============================================================

#include <iostream>
#include <string>
#include <functional>
#include <vector>

// 各模块入口函数声明
void run_basics();           // 01 基础语法
void run_pointers();         // 02 指针与引用
void run_oop();              // 03 面向对象
void run_templates();        // 04 模板编程
void run_stl();              // 05 STL容器与算法
void run_memory();           // 06 内存管理
void run_concurrency();      // 07 并发编程
void run_modern_cpp();       // 08 现代C++特性
void run_design_patterns();  // 09 设计模式
void run_real_world();       // 10 实战：运动控制器

int main() {
    // 菜单项：编号、名称、入口函数
    struct MenuItem {
        int id;
        std::string name;
        std::function<void()> func;
    };

    std::vector<MenuItem> menu = {
        {1,  "基础语法 — 变量、类型、运算符、控制流",           run_basics},
        {2,  "指针与引用 — 原始指针、智能指针、引用",           run_pointers},
        {3,  "面向对象 — 类、继承、多态、虚函数",              run_oop},
        {4,  "模板编程 — 函数模板、类模板、概念(C++20)",       run_templates},
        {5,  "STL容器与算法 — vector/map/set + algorithms",   run_stl},
        {6,  "内存管理 — RAII、unique_ptr、shared_ptr、移动语义", run_memory},
        {7,  "并发编程 — thread、mutex、future、atomic",       run_concurrency},
        {8,  "现代C++特性 — lambda、auto、结构化绑定、optional", run_modern_cpp},
        {9,  "设计模式 — 工厂、单例、观察者、策略",             run_design_patterns},
        {10, "实战 — 模拟运动控制器(半导体设备)",               run_real_world},
        {0,  "运行全部模块",                                   nullptr},
    };

    while (true) {
        std::cout << "\n";
        std::cout << "╔══════════════════════════════════════════════════════╗\n";
        std::cout << "║        C++ 完整语言学习项目  (CppMasterClass)       ║\n";
        std::cout << "╠══════════════════════════════════════════════════════╣\n";
        for (auto& item : menu) {
            if (item.id == 0) {
                std::cout << "║  " << item.id << ". " << item.name;
                // 填充空格对齐
                for (size_t i = item.name.size(); i < 48; ++i) std::cout << " ";
                std::cout << "║\n";
            } else {
                std::cout << "║  " << item.id << ". " << item.name;
                for (size_t i = item.name.size() + (item.id < 10 ? 0 : 1); i < 48; ++i) std::cout << " ";
                std::cout << "║\n";
            }
        }
        std::cout << "║  q. 退出                                            ║\n";
        std::cout << "╚══════════════════════════════════════════════════════╝\n";
        std::cout << "请选择模块编号: ";

        std::string input;
        std::getline(std::cin, input);

        // 移除 Windows 换行符的 \r
        if (!input.empty() && input.back() == '\r') {
            input.pop_back();
        }

        if (input.empty() && std::cin.eof()) break;

        if (input == "q" || input == "Q") {
            std::cout << "再见！\n";
            break;
        }

        int choice = -1;
        try { choice = std::stoi(input); } catch (...) { continue; }

        if (choice == 0) {
            // 运行全部
            for (auto& item : menu) {
                if (item.func) {
                    std::cout << "\n\n========== 模块 " << item.id << ": " << item.name << " ==========\n";
                    item.func();
                }
            }
        } else {
            bool found = false;
            for (auto& item : menu) {
                if (item.id == choice && item.func) {
                    std::cout << "\n========== " << item.name << " ==========\n";
                    item.func();
                    found = true;
                    break;
                }
            }
            if (!found) std::cout << "无效选择\n";
        }
    }
    return 0;
}
