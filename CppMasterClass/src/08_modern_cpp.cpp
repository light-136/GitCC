// ============================================================
// 08_modern_cpp.cpp — 现代C++特性 (C++11/14/17/20)
// 覆盖：lambda、auto/decltype、结构化绑定、
//       if constexpr、optional/variant/any、
//       string_view、初始化列表
// ============================================================

#include <iostream>
#include <string>
#include <vector>
#include <algorithm>
#include <optional>
#include <variant>
#include <any>
#include <string_view>
#include <tuple>
#include <functional>
#include <map>
#include <type_traits>

// ---- if constexpr (C++17) — 编译期分支 ----
template<typename T>
std::string type_description(T val) {
    if constexpr (std::is_integral_v<T>) {
        return "整数: " + std::to_string(val);
    } else if constexpr (std::is_floating_point_v<T>) {
        return "浮点: " + std::to_string(val);
    } else {
        return "其他类型";
    }
}

// ---- string_view 演示 ----
void print_view(std::string_view sv) {
    std::cout << "  string_view: \"" << sv << "\" (长度=" << sv.size() << ")\n";
}

void run_modern_cpp() {
    // ---- 1. Lambda 表达式 ----
    std::cout << "\n--- Lambda 表达式 ---\n";

    // 基本 lambda
    auto add = [](int a, int b) { return a + b; };
    std::cout << "  基本: add(3,4) = " << add(3, 4) << "\n";

    // 捕获变量
    int multiplier = 10;
    auto multiply = [multiplier](int x) { return x * multiplier; };
    std::cout << "  捕获: multiply(5) = " << multiply(5) << "\n";

    // 引用捕获
    int sum = 0;
    auto accumulate = [&sum](int x) { sum += x; };
    for (int i = 1; i <= 5; ++i) accumulate(i);
    std::cout << "  引用捕获: sum(1..5) = " << sum << "\n";

    // 泛型 lambda (C++14)
    auto generic_add = [](auto a, auto b) { return a + b; };
    std::cout << "  泛型: int=" << generic_add(1, 2)
              << " double=" << generic_add(1.5, 2.5)
              << " string=" << generic_add(std::string("Hello"), std::string(" World")) << "\n";

    // lambda 作为回调
    std::vector<int> nums = {3, 1, 4, 1, 5, 9, 2, 6};
    std::sort(nums.begin(), nums.end(), [](int a, int b) { return a > b; });
    std::cout << "  降序排序: ";
    for (auto n : nums) std::cout << n << " ";
    std::cout << "\n";

    // ---- 2. auto / decltype ----
    std::cout << "\n--- auto / decltype ---\n";
    auto x = 42;
    auto y = 3.14;
    auto z = std::string("text");
    decltype(x) x2 = 100;  // x2 的类型与 x 相同 (int)
    std::cout << "  auto: x=" << x << "(int) y=" << y << "(double) z=" << z << "(string)\n";
    std::cout << "  decltype(x) x2 = " << x2 << " (类型推导为int)\n";

    // ---- 3. 结构化绑定 (C++17) ----
    std::cout << "\n--- 结构化绑定 ---\n";

    // pair
    auto [first, second] = std::make_pair("key", 42);
    std::cout << "  pair: " << first << " = " << second << "\n";

    // tuple
    auto [name, age, score] = std::make_tuple("Alice", 25, 95.5);
    std::cout << "  tuple: name=" << name << " age=" << age << " score=" << score << "\n";

    // map 遍历
    std::map<std::string, int> settings = {{"speed", 100}, {"accel", 500}, {"decel", 500}};
    std::cout << "  map 遍历:\n";
    for (const auto& [key, value] : settings) {
        std::cout << "    " << key << " = " << value << "\n";
    }

    // ---- 4. if constexpr (C++17) ----
    std::cout << "\n--- if constexpr (编译期分支) ---\n";
    std::cout << "  " << type_description(42) << "\n";
    std::cout << "  " << type_description(3.14) << "\n";
    std::cout << "  (不满足条件的分支在编译期被丢弃)\n";

    // ---- 5. std::optional (C++17) ----
    std::cout << "\n--- std::optional ---\n";
    auto find_user = [](int id) -> std::optional<std::string> {
        if (id == 1) return "admin";
        if (id == 2) return "operator";
        return std::nullopt;  // 无值
    };

    auto user1 = find_user(1);
    auto user3 = find_user(3);
    std::cout << "  查找ID=1: " << (user1.has_value() ? user1.value() : "未找到") << "\n";
    std::cout << "  查找ID=3: " << user3.value_or("未找到") << "\n";

    // ---- 6. std::variant (C++17) — 类型安全的联合 ----
    std::cout << "\n--- std::variant (类型安全联合) ---\n";

    using ConfigValue = std::variant<int, double, std::string, bool>;
    std::vector<std::pair<std::string, ConfigValue>> config = {
        {"port", 5000},
        {"timeout", 30.5},
        {"host", std::string("192.168.1.100")},
        {"enabled", true}
    };

    for (const auto& [key, val] : config) {
        std::cout << "  " << key << " = ";
        std::visit([](const auto& v) {
            using T = std::decay_t<decltype(v)>;
            if constexpr (std::is_same_v<T, bool>)
                std::cout << (v ? "true" : "false");
            else
                std::cout << v;
        }, val);
        std::cout << "\n";
    }

    // ---- 7. std::any (C++17) — 任意类型容器 ----
    std::cout << "\n--- std::any ---\n";
    std::any a1 = 42;
    std::any a2 = std::string("Hello");
    std::cout << "  a1 类型: " << a1.type().name() << " 值: " << std::any_cast<int>(a1) << "\n";
    std::cout << "  a2 类型: " << a2.type().name() << " 值: " << std::any_cast<std::string>(a2) << "\n";

    // ---- 8. string_view (C++17) — 零拷贝字符串视图 ----
    std::cout << "\n--- string_view (零拷贝) ---\n";
    std::string full_path = "/usr/local/bin/program";
    print_view(full_path);
    print_view(full_path.substr(0, 4));      // 子串会拷贝
    print_view(std::string_view(full_path).substr(0, 4));  // 零拷贝
    print_view("字面量也可以");  // const char* 隐式转换

    // ---- 9. 初始化列表 ----
    std::cout << "\n--- 初始化列表 ---\n";
    auto print_list = [](std::initializer_list<int> list) {
        std::cout << "  { ";
        for (auto v : list) std::cout << v << " ";
        std::cout << "}\n";
    };
    print_list({1, 2, 3, 4, 5});
    print_list({10, 20, 30});

    std::cout << "\n[现代C++特性模块完成]\n";
}
