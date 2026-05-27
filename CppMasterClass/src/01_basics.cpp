// ============================================================
// 01_basics.cpp — C++ 基础语法
// 覆盖：基本类型、sizeof、auto、const、constexpr、枚举、
//       控制流(if/switch/for/while)、数组、字符串
// ============================================================

#include <iostream>
#include <string>
#include <array>
#include <cstdint>

// constexpr 编译期计算 — 阶乘
constexpr int factorial(int n) {
    return n <= 1 ? 1 : n * factorial(n - 1);
}

// 强类型枚举(C++11) — 避免命名冲突、有明确底层类型
enum class Color : uint8_t {
    Red = 0,
    Green = 1,
    Blue = 2
};

// 普通函数：演示值传递
int add(int a, int b) {
    return a + b;
}

void run_basics() {
    // ---- 1. 基本类型与 sizeof ----
    std::cout << "\n--- 基本类型与大小 ---\n";
    std::cout << "bool:        " << sizeof(bool)        << " 字节\n";
    std::cout << "char:        " << sizeof(char)        << " 字节\n";
    std::cout << "short:       " << sizeof(short)       << " 字节\n";
    std::cout << "int:         " << sizeof(int)         << " 字节\n";
    std::cout << "long:        " << sizeof(long)        << " 字节\n";
    std::cout << "long long:   " << sizeof(long long)   << " 字节\n";
    std::cout << "float:       " << sizeof(float)       << " 字节\n";
    std::cout << "double:      " << sizeof(double)      << " 字节\n";
    std::cout << "long double: " << sizeof(long double)  << " 字节\n";
    std::cout << "指针:        " << sizeof(void*)       << " 字节 (" << sizeof(void*) * 8 << "位系统)\n";

    // ---- 2. auto 自动类型推导(C++11) ----
    std::cout << "\n--- auto 类型推导 ---\n";
    auto intVar = 42;           // int
    auto doubleVar = 3.14;      // double
    auto strVar = std::string("Hello C++");  // std::string
    auto boolVar = true;        // bool
    std::cout << "intVar = " << intVar << " (int)\n";
    std::cout << "doubleVar = " << doubleVar << " (double)\n";
    std::cout << "strVar = " << strVar << " (string)\n";
    std::cout << "boolVar = " << boolVar << " (bool)\n";

    // ---- 3. const 与 constexpr ----
    std::cout << "\n--- const 与 constexpr ---\n";
    const double PI = 3.14159265358979;
    constexpr int FACT_10 = factorial(10);  // 编译期计算
    std::cout << "PI = " << PI << " (const，运行时常量)\n";
    std::cout << "10! = " << FACT_10 << " (constexpr，编译期计算)\n";
    constexpr int FACT_12 = factorial(12);
    std::cout << "12! = " << FACT_12 << " (编译期确定)\n";

    // ---- 4. 强类型枚举 ----
    std::cout << "\n--- 强类型枚举 (enum class) ---\n";
    Color c = Color::Green;
    // 不能隐式转int，必须显式转换
    std::cout << "Color::Green = " << static_cast<int>(c) << "\n";
    // switch 配合枚举
    switch (c) {
        case Color::Red:   std::cout << "红色\n"; break;
        case Color::Green: std::cout << "绿色\n"; break;
        case Color::Blue:  std::cout << "蓝色\n"; break;
    }

    // ---- 5. 控制流 ----
    std::cout << "\n--- 控制流 ---\n";

    // for 循环 + 范围for(C++11)
    std::array<int, 5> nums = {10, 20, 30, 40, 50};
    std::cout << "数组元素: ";
    for (const auto& n : nums) {
        std::cout << n << " ";
    }
    std::cout << "\n";

    // while 计算：找到第一个大于100的斐波那契数
    int a = 1, b = 1;
    while (b <= 100) {
        int temp = a + b;
        a = b;
        b = temp;
    }
    std::cout << "第一个 >100 的斐波那契数: " << b << "\n";

    // if 带初始化语句(C++17)
    if (auto result = add(17, 25); result > 30) {
        std::cout << "17+25=" << result << " > 30 (C++17 if初始化)\n";
    }

    // ---- 6. 字符串操作 ----
    std::cout << "\n--- 字符串操作 ---\n";
    std::string s1 = "Hello";
    std::string s2 = " World";
    std::string s3 = s1 + s2;  // 拼接
    std::cout << "拼接: " << s3 << "\n";
    std::cout << "长度: " << s3.length() << "\n";
    std::cout << "子串: " << s3.substr(0, 5) << "\n";
    std::cout << "查找 'World': 位置 " << s3.find("World") << "\n";

    // ---- 7. 类型转换 ----
    std::cout << "\n--- 类型转换 ---\n";
    double d = 3.99;
    int i = static_cast<int>(d);  // 截断
    std::cout << "static_cast<int>(3.99) = " << i << " (截断，不四舍五入)\n";

    char ch = 'A';
    int ascii = static_cast<int>(ch);
    std::cout << "字符 'A' 的ASCII码 = " << ascii << "\n";

    std::cout << "\n[基础语法模块完成]\n";
}
