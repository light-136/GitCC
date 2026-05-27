// ============================================================
// 05_stl.cpp — STL 容器与算法
// 覆盖：vector、list、map、set、unordered_map、
//       迭代器、算法(sort/find/transform/accumulate)、
//       性能对比
// ============================================================

#include <iostream>
#include <vector>
#include <list>
#include <map>
#include <set>
#include <unordered_map>
#include <algorithm>
#include <numeric>
#include <string>
#include <chrono>
#include <random>
#include <functional>

void run_stl() {
    // ---- 1. vector — 动态数组 ----
    std::cout << "\n--- vector ---\n";
    std::vector<int> vec = {5, 3, 8, 1, 9, 2, 7};
    std::cout << "原始: ";
    for (auto v : vec) std::cout << v << " ";
    std::cout << "\n";

    std::sort(vec.begin(), vec.end());
    std::cout << "排序后: ";
    for (auto v : vec) std::cout << v << " ";
    std::cout << "\n";

    // 二分查找
    bool found = std::binary_search(vec.begin(), vec.end(), 7);
    std::cout << "查找7: " << (found ? "找到" : "未找到") << "\n";

    // 统计
    auto minmax = std::minmax_element(vec.begin(), vec.end());
    std::cout << "最小=" << *minmax.first << " 最大=" << *minmax.second << "\n";
    int total = std::accumulate(vec.begin(), vec.end(), 0);
    std::cout << "总和=" << total << " 平均=" << (double)total / vec.size() << "\n";

    // ---- 2. map — 有序键值对 ----
    std::cout << "\n--- map (有序映射) ---\n";
    std::map<std::string, int> scores;
    scores["Alice"] = 95;
    scores["Bob"] = 87;
    scores["Charlie"] = 92;
    scores["Diana"] = 98;

    for (const auto& [name, score] : scores) {
        std::cout << "  " << name << " : " << score << "分\n";
    }
    std::cout << "(自动按key排序)\n";

    // 查找
    if (auto it = scores.find("Bob"); it != scores.end()) {
        std::cout << "找到 Bob: " << it->second << "分\n";
    }

    // ---- 3. set — 有序集合(自动去重) ----
    std::cout << "\n--- set (有序集合) ---\n";
    std::set<int> s = {3, 1, 4, 1, 5, 9, 2, 6, 5, 3};
    std::cout << "插入 {3,1,4,1,5,9,2,6,5,3} 后: ";
    for (auto v : s) std::cout << v << " ";
    std::cout << "\n(自动去重+排序)\n";

    // ---- 4. unordered_map — 哈希表(O(1)查找) ----
    std::cout << "\n--- unordered_map (哈希映射) ---\n";
    std::unordered_map<std::string, std::string> config;
    config["host"] = "192.168.1.100";
    config["port"] = "5000";
    config["protocol"] = "HSMS";
    config["timeout"] = "30";

    for (const auto& [key, value] : config) {
        std::cout << "  " << key << " = " << value << "\n";
    }
    std::cout << "(无序，但O(1)查找)\n";

    // ---- 5. list — 双向链表 ----
    std::cout << "\n--- list (双向链表) ---\n";
    std::list<int> lst = {10, 20, 30, 40, 50};
    lst.push_front(5);   // O(1) 头部插入
    lst.push_back(60);   // O(1) 尾部插入
    std::cout << "list: ";
    for (auto v : lst) std::cout << v << " ";
    std::cout << "\n";

    // ---- 6. 算法演示 ----
    std::cout << "\n--- STL算法 ---\n";

    // transform — 对每个元素应用函数
    std::vector<int> src = {1, 2, 3, 4, 5};
    std::vector<int> dst(src.size());
    std::transform(src.begin(), src.end(), dst.begin(), [](int x) { return x * x; });
    std::cout << "transform(平方): ";
    for (auto v : dst) std::cout << v << " ";
    std::cout << "\n";

    // count_if — 条件计数
    auto even_count = std::count_if(src.begin(), src.end(), [](int x) { return x % 2 == 0; });
    std::cout << "偶数个数: " << even_count << "\n";

    // remove_if + erase — 删除满足条件的元素
    std::vector<int> data = {1, 2, 3, 4, 5, 6, 7, 8, 9, 10};
    auto it = std::remove_if(data.begin(), data.end(), [](int x) { return x % 3 == 0; });
    data.erase(it, data.end());
    std::cout << "删除3的倍数: ";
    for (auto v : data) std::cout << v << " ";
    std::cout << "\n";

    // for_each
    std::cout << "for_each(翻倍): ";
    std::for_each(data.begin(), data.end(), [](int& x) { x *= 2; });
    for (auto v : data) std::cout << v << " ";
    std::cout << "\n";

    // ---- 7. 性能对比：vector vs list 插入 ----
    std::cout << "\n--- 性能对比 ---\n";
    const int N = 100000;

    auto bench = [](const std::string& name, std::function<void()> fn) {
        auto start = std::chrono::high_resolution_clock::now();
        fn();
        auto end = std::chrono::high_resolution_clock::now();
        auto us = std::chrono::duration_cast<std::chrono::microseconds>(end - start).count();
        std::cout << "  " << name << ": " << us << " us\n";
    };

    bench("vector 尾部插入 10万", [&]() {
        std::vector<int> v;
        for (int i = 0; i < N; ++i) v.push_back(i);
    });

    bench("list   尾部插入 10万", [&]() {
        std::list<int> l;
        for (int i = 0; i < N; ++i) l.push_back(i);
    });

    bench("vector 排序 10万", [&]() {
        std::vector<int> v(N);
        std::iota(v.begin(), v.end(), 0);
        std::shuffle(v.begin(), v.end(), std::mt19937{42});
        std::sort(v.begin(), v.end());
    });

    bench("map    插入 10万", [&]() {
        std::map<int, int> m;
        for (int i = 0; i < N; ++i) m[i] = i;
    });

    bench("unordered_map 插入 10万", [&]() {
        std::unordered_map<int, int> m;
        for (int i = 0; i < N; ++i) m[i] = i;
    });

    std::cout << "\n[STL容器与算法模块完成]\n";
}
