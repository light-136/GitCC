// ============================================================
// 04_templates.cpp — 模板编程
// 覆盖：函数模板、类模板、模板特化、可变参数模板、
//       SFINAE、C++20 concepts
// ============================================================

#include <iostream>
#include <string>
#include <vector>
#include <type_traits>
#include <numeric>

// ---- 1. 函数模板 ----
template<typename T>
T maximum(T a, T b) {
    return (a > b) ? a : b;
}

// ---- 2. 类模板 — 简易栈 ----
template<typename T, int MaxSize = 10>
class SimpleStack {
    T data_[MaxSize];
    int top_ = -1;

public:
    bool push(const T& val) {
        if (top_ >= MaxSize - 1) return false;
        data_[++top_] = val;
        return true;
    }

    bool pop(T& val) {
        if (top_ < 0) return false;
        val = data_[top_--];
        return true;
    }

    bool empty() const { return top_ < 0; }
    int size() const { return top_ + 1; }
};

// ---- 3. 模板特化 — 针对 const char* 的特殊处理 ----
template<typename T>
std::string type_name() { return "未知类型"; }

template<> std::string type_name<int>()         { return "int"; }
template<> std::string type_name<double>()      { return "double"; }
template<> std::string type_name<std::string>()  { return "std::string"; }
template<> std::string type_name<bool>()        { return "bool"; }

// ---- 4. 可变参数模板 (C++11) ----
// 递归终止
template<typename T>
void print_all(const T& val) {
    std::cout << val << "\n";
}

// 递归展开
template<typename T, typename... Args>
void print_all(const T& first, const Args&... rest) {
    std::cout << first << ", ";
    print_all(rest...);
}

// 折叠表达式求和 (C++17)
template<typename... Args>
auto sum_all(Args... args) {
    return (args + ...);  // 一元右折叠
}

// ---- 5. SFINAE — 根据类型特征选择不同实现 ----
template<typename T>
typename std::enable_if<std::is_integral<T>::value, std::string>::type
describe_type(T val) {
    return "整数类型, 值=" + std::to_string(val);
}

template<typename T>
typename std::enable_if<std::is_floating_point<T>::value, std::string>::type
describe_type(T val) {
    return "浮点类型, 值=" + std::to_string(val);
}

// ---- 6. C++20 Concepts ----
#if __cplusplus >= 202002L
template<typename T>
concept Numeric = std::is_arithmetic_v<T>;

template<typename T>
concept Addable = requires(T a, T b) {
    { a + b } -> std::convertible_to<T>;
};

template<Numeric T>
T safe_divide(T a, T b) {
    if (b == 0) return 0;
    return a / b;
}
#endif

// ---- 7. 编译期计算 (constexpr + 模板) ----
template<int N>
struct Fibonacci {
    static constexpr int value = Fibonacci<N-1>::value + Fibonacci<N-2>::value;
};

template<> struct Fibonacci<0> { static constexpr int value = 0; };
template<> struct Fibonacci<1> { static constexpr int value = 1; };

void run_templates() {
    // 函数模板
    std::cout << "\n--- 函数模板 ---\n";
    std::cout << "max(3, 7) = " << maximum(3, 7) << "\n";
    std::cout << "max(3.14, 2.71) = " << maximum(3.14, 2.71) << "\n";
    std::cout << "max(\"abc\", \"xyz\") = " << maximum(std::string("abc"), std::string("xyz")) << "\n";

    // 类模板
    std::cout << "\n--- 类模板(SimpleStack) ---\n";
    SimpleStack<int, 5> stack;
    stack.push(10);
    stack.push(20);
    stack.push(30);
    std::cout << "栈大小: " << stack.size() << "\n";
    int val;
    while (!stack.empty()) {
        stack.pop(val);
        std::cout << "弹出: " << val << "\n";
    }

    // 模板特化
    std::cout << "\n--- 模板特化 ---\n";
    std::cout << "int 类型名: " << type_name<int>() << "\n";
    std::cout << "double 类型名: " << type_name<double>() << "\n";
    std::cout << "string 类型名: " << type_name<std::string>() << "\n";
    std::cout << "bool 类型名: " << type_name<bool>() << "\n";

    // 可变参数模板
    std::cout << "\n--- 可变参数模板 ---\n";
    std::cout << "print_all: ";
    print_all(1, 2.5, "hello", true);
    std::cout << "sum_all(1,2,3,4,5) = " << sum_all(1, 2, 3, 4, 5) << "\n";
    std::cout << "sum_all(1.1, 2.2, 3.3) = " << sum_all(1.1, 2.2, 3.3) << "\n";

    // SFINAE
    std::cout << "\n--- SFINAE ---\n";
    std::cout << describe_type(42) << "\n";
    std::cout << describe_type(3.14) << "\n";

    // C++20 Concepts
#if __cplusplus >= 202002L
    std::cout << "\n--- C++20 Concepts ---\n";
    std::cout << "safe_divide(10, 3) = " << safe_divide(10, 3) << "\n";
    std::cout << "safe_divide(10.0, 3.0) = " << safe_divide(10.0, 3.0) << "\n";
    std::cout << "safe_divide(10, 0) = " << safe_divide(10, 0) << " (安全除零)\n";
#else
    std::cout << "\n--- C++20 Concepts (需要C++20编译器, 已跳过) ---\n";
#endif

    // 编译期斐波那契
    std::cout << "\n--- 编译期计算(模板元编程) ---\n";
    std::cout << "Fibonacci<0>  = " << Fibonacci<0>::value << "\n";
    std::cout << "Fibonacci<5>  = " << Fibonacci<5>::value << "\n";
    std::cout << "Fibonacci<10> = " << Fibonacci<10>::value << "\n";
    std::cout << "Fibonacci<20> = " << Fibonacci<20>::value << "\n";
    std::cout << "(以上全部在编译期计算完成，运行时零开销)\n";

    std::cout << "\n[模板编程模块完成]\n";
}
